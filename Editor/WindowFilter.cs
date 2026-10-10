using System;
using System.Collections.Generic;
using System.Reflection;

namespace EditorTranslator
{
    // Window-level opt-out: text drawn inside data-centric windows (Hierarchy, Project, Console,
    // Scene, Game...) must never be translated, because those strings are user asset names.
    // Uses the internal GUIView.current -> HostView.<window> chain via reflection.
    internal static class WindowFilter
    {
        static PropertyInfo _current;
        static bool _initTried;
        static readonly Dictionary<Type, PropertyInfo> _hostWindowProps = new Dictionary<Type, PropertyInfo>();

        static void Init()
        {
            if (_initTried) return;
            _initTried = true;
            try
            {
                // Unity 2021-2022: UnityEditor.GUIView; older/newer builds have used UnityEngine.GUIView.
                Type gvType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    gvType = asm.GetType("UnityEditor.GUIView", false)
                             ?? asm.GetType("UnityEngine.GUIView", false);
                    if (gvType != null) break;
                }
                if (gvType == null) return;
                _current = gvType.GetProperty("current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch { }
        }

        // The window member lives on HostView (a SUBclass of GUIView), and its name varies by
        // version ("hostWindow" older, "actualView" 2021+). So look up from the concrete view
        // instance type, walking its hierarchy, and cache per concrete type.
        static PropertyInfo GetHostWindowProp(Type viewType)
        {
            PropertyInfo p;
            if (_hostWindowProps.TryGetValue(viewType, out p)) return p;
            foreach (var name in new[] { "hostWindow", "actualView", "pane" })
            {
                for (var t = viewType; t != null; t = t.BaseType)
                {
                    p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && typeof(UnityEditor.EditorWindow).IsAssignableFrom(p.PropertyType))
                    {
                        _hostWindowProps[viewType] = p;
                        return p;
                    }
                }
            }
            _hostWindowProps[viewType] = null;
            return null;
        }

        // Temporary diagnostic: describe what the reflection chain sees right now.
        internal static string DescribeCurrentWindow()
        {
            try
            {
                Init();
                if (_current == null) return "no-current-prop";
                var view = _current.GetValue(null);
                if (view == null) return "view-null";
                var hw = GetHostWindowProp(view.GetType());
                if (hw == null) return "no-hostwindow-prop";
                var win = hw.GetValue(view);
                return win == null ? "win-null" : win.GetType().FullName;
            }
            catch (System.Exception e) { return "error:" + e.Message; }
        }

        public static bool AllowCurrentWindow(TranslatorSettings st)
        {
            try
            {
                Init();
                if (_current == null) return true;
                var view = _current.GetValue(null);
                if (view == null) return true;
                var hw = GetHostWindowProp(view.GetType());
                if (hw == null) return true;
                var win = hw.GetValue(view);
                if (win == null) return true;
                string typeName = win.GetType().FullName;
                var bl = st.windowBlacklist;
                if (bl == null) return true;
                for (int i = 0; i < bl.Count; i++)
                {
                    var pat = bl[i];
                    if (string.IsNullOrWhiteSpace(pat)) continue;
                    if (typeName.StartsWith(pat.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
                }
                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}

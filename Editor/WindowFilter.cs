using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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

        // Some draws (ShaderGUI / MaterialEditor panels, nested panes) run with GUIView.current
        // == null, so the window-type path above cannot classify them. Fall back to sniffing the
        // call stack: the first decisive frame tells us whether this is a data window (blocked by
        // the blacklist) or an inspector-ish context (allowed).
        static readonly string[] SafeStackMarkers =
        {
            "InspectorWindow", "MaterialEditor", "PropertyEditor", "ShaderGUI", "EditorWindow",
            "TooltipView", // floating IMGUI tooltip — no hostWindow, draws via GUI.Box
        };

        static bool JudgeByCallStack(TranslatorSettings st)
        {
            try
            {
                var stack = new System.Diagnostics.StackTrace(3, false);
                var bl = st.windowBlacklist;
                for (int i = 0; i < stack.FrameCount && i < 48; i++)
                {
                    var type = stack.GetFrame(i).GetMethod()?.DeclaringType;
                    var name = type?.FullName;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (bl != null)
                    {
                        for (int j = 0; j < bl.Count; j++)
                        {
                            var p = bl[j];
                            if (!string.IsNullOrWhiteSpace(p) && name.StartsWith(p.Trim(), StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                    }
                    for (int j = 0; j < SafeStackMarkers.Length; j++)
                        if (name.IndexOf(SafeStackMarkers[j], StringComparison.Ordinal) >= 0)
                            return true;
                }
            }
            catch { }
            return false; // unknown context — fail closed
        }

        // Frame-scoped memo: within one editor frame the current view never changes, so the
        // (reflection-heavy) verdict is computed once per view per frame instead of per control.
        static UnityEngine.Object _memoView;
        static bool _memoAllow;
        static int _memoFrame;

        public static bool AllowCurrentWindow(TranslatorSettings st)
        {
            // User opt-in: when enabled, data-centric windows are no longer excluded, so
            // scene objects / asset names get translated too (at the user's own risk).
            if (st.translateAssetNames) return true;
            try
            {
                Init();
                if (_current == null) return false;
                var view = _current.GetValue(null);
                if (view == null) return JudgeByCallStack(st);

                int frame = Time.frameCount;
                if (frame == _memoFrame && ReferenceEquals(view, _memoView)) return _memoAllow;

                bool allow;
                var hw = GetHostWindowProp(view.GetType());
                if (hw == null)
                {
                    allow = JudgeByCallStack(st);
                }
                else
                {
                    var win = hw.GetValue(view);
                    allow = win != null
                        ? MatchBlacklist(win.GetType().FullName, st)
                        : JudgeByCallStack(st); // nested pane without a host window
                }
                _memoView = view as UnityEngine.Object;
                _memoAllow = allow;
                _memoFrame = frame;
                return allow;
            }
            catch
            {
                return false;
            }
        }

        static bool MatchBlacklist(string typeName, TranslatorSettings st)
        {
            var bl = st.windowBlacklist;
            if (bl == null) return true;
            for (int i = 0; i < bl.Count; i++)
            {
                var pat = bl[i];
                if (!string.IsNullOrWhiteSpace(pat) && typeName.StartsWith(pat.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
    }
}

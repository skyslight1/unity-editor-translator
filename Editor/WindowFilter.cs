using System;
using System.Reflection;

namespace EditorTranslator
{
    // Window-level opt-out: text drawn inside data-centric windows (Hierarchy, Project, Console,
    // Scene, Game...) must never be translated, because those strings are user asset names.
    // Uses the internal GUIView.current -> HostView.hostWindow chain via reflection.
    internal static class WindowFilter
    {
        static PropertyInfo _current;
        static PropertyInfo _hostWindow;
        static bool _initTried;

        static void Init()
        {
            if (_initTried) return;
            _initTried = true;
            try
            {
                Type gvType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    gvType = asm.GetType("UnityEngine.GUIView", false);
                    if (gvType != null) break;
                }
                if (gvType == null) return;
                _current = gvType.GetProperty("current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _hostWindow = gvType.GetProperty("hostWindow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch { }
        }

        public static bool AllowCurrentWindow(TranslatorSettings st)
        {
            try
            {
                Init();
                if (_current == null) return true;
                var view = _current.GetValue(null);
                if (view == null) return true;
                var win = _hostWindow?.GetValue(view);
                if (win == null) return true;
                string typeName = win.GetType().FullName;
                var bl = st.windowBlacklist;
                if (bl == null) return true;
                for (int i = 0; i < bl.Count; i++)
                {
                    var p = bl[i];
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    if (typeName.StartsWith(p.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
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

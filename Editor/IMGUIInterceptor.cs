using System;
using System.Collections.Generic;
using UnityEngine;

namespace EditorTranslator
{
    // Harmony prefix/postfix around every GUIStyle.Draw(Rect, GUIContent, ...) overload.
    // The prefix swaps GUIContent.text to the bilingual form; the postfix restores the original
    // string immediately after the native draw call, so pooled GUIContent objects are never
    // left modified. IMGUI draws synchronously, so the swap is invisible outside the draw.
    internal static class IMGUIInterceptor
    {
        static readonly Stack<(GUIContent content, string original)> _restore = new Stack<(GUIContent, string)>(4);
        static int _debugCount;

        // The 'content' parameter name must match the original method's; Harmony injects by name.
        public static void Prefix(GUIContent content)
        {
            if (content == null) return;

            // Safety valve: if a draw ever threw between prefix and postfix, drain stale entries.
            if (_restore.Count > 8)
            {
                while (_restore.Count > 0)
                {
                    var t = _restore.Pop();
                    t.content.text = t.original;
                }
            }

            if (!TranslatorCore.IsRunning) return;

            if (TranslatorCore.DebugDiagnostics && _debugCount < 200)
            {
                _debugCount++;
                Debug.Log("[ET-debug#" + _debugCount + "] win=" + WindowFilter.DescribeCurrentWindow() + " | text=" + content.text);
            }

            string orig = content.text;
            if (string.IsNullOrEmpty(orig)) return;

            var st = TranslatorSettings.Instance;
            if (!WindowFilter.AllowCurrentWindow(st)) return;

            string zh = TranslationCache.Lookup(orig); // returns null unless a finished translation exists
            if (zh == null) return;

            string shown = Bilingual.Format(zh, orig, st);
            if (string.Equals(shown, orig, StringComparison.Ordinal)) return;

            _restore.Push((content, orig));
            content.text = shown;
        }

        public static void Postfix()
        {
            if (_restore.Count > 0)
            {
                var t = _restore.Pop();
                t.content.text = t.original;
            }
        }
    }
}

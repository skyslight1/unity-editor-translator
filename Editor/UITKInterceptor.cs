using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace EditorTranslator
{
    // UI Toolkit interception. The IMGUI hook cannot see UITK panels (lilxyzw's NonToon and
    // other newer shader GUIs are built on UIElements), so we hook the single choke point
    // every text-bearing control funnels through: TextElement.set_text (virtual, non-abstract).
    //
    // This is data-level replacement: while enabled, the element stores the bilingual string;
    // the original is kept in a weak table and restored when the toggle goes off. Input widgets
    // (TextField & friends) are excluded by walking the parent hierarchy.
    internal static class UITKInterceptor
    {
        class OriginalHolder { public string Text; }

        static readonly ConditionalWeakTable<TextElement, OriginalHolder> _originals = new ConditionalWeakTable<TextElement, OriginalHolder>();
        static readonly List<WeakReference> _tracked = new List<WeakReference>(64);
        static bool _suppress; // guard while we ourselves assign element.text

        // Diagnostics: total prefix invocations (any early-out still counts).
        internal static long CallCount;

        // Harmony prefix on TextElement.set_text. Rewriting 'value' stores the bilingual form.
        // NOTE: no window filtering here. In 2022.3 every data-centric window (Hierarchy,
        // Project, ...) is still IMGUI, while UITK builds run without a GUIView context —
        // window/stack classification would randomly block panel rebuilds. Revisit when
        // Unity ships UITK-based data windows.
        public static void Prefix(TextElement __instance, ref string value)
        {
            CallCount++;
            if (_suppress) return;
            if (!TranslatorCore.IsRunning) return;
            if (string.IsNullOrEmpty(value)) return;
            var st = TranslatorSettings.Instance;
            if (!st.enableUIToolkit) return;
            if (IsInsideTextInput(__instance)) return;

            if (_originals.TryGetValue(__instance, out var holder))
            {
                // Same original coming back (panel rebuild / data-binding refresh): re-swap
                // from cache if a translation is ready.
                if (string.Equals(value, holder.Text, StringComparison.Ordinal))
                {
                    string zh = TranslationCache.Lookup(value);
                    if (zh != null)
                    {
                        string shown = Bilingual.Format(zh, value, st);
                        if (!string.Equals(shown, value, StringComparison.Ordinal)) value = shown;
                    }
                    return;
                }
                // Genuinely different content — the old original no longer applies.
                _originals.Remove(__instance);
            }

            string orig = value;
            if (!TextClassifier.ShouldTranslate(orig, st)) return;
            string zh2 = TranslationCache.Lookup(orig); // null unless Done; enqueues otherwise
            // Track the element regardless of queue state: UITK is retained-mode, so when the
            // translation finishes RefreshTracked is the only thing that can push it onto the
            // already-built panel.
            _originals.Add(__instance, new OriginalHolder { Text = orig });
            _tracked.Add(new WeakReference(__instance));
            if (zh2 == null) return;
            string shown2 = Bilingual.Format(zh2, orig, st);
            if (string.Equals(shown2, orig, StringComparison.Ordinal)) return;
            value = shown2;
        }

        // Re-apply translations to tracked elements once their queue entries finish.
        // UITK is retained-mode: unlike IMGUI, nothing repaints automatically after a
        // background translation completes, so we push the new text ourselves.
        public static void RefreshTracked()
        {
            if (!TranslatorCore.IsRunning || _suppress) return;
            var st = TranslatorSettings.Instance;
            if (!st.enableUIToolkit) return;
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                if (!_tracked[i].IsAlive) { _tracked.RemoveAt(i); continue; }
                var el = _tracked[i].Target as TextElement;
                if (el == null || !_originals.TryGetValue(el, out var h)) { _tracked.RemoveAt(i); continue; }
                string zh = TranslationCache.Lookup(h.Text);
                if (zh == null) continue;
                string shown = Bilingual.Format(zh, h.Text, st);
                if (string.Equals(el.text, shown, StringComparison.Ordinal)) continue;
                _suppress = true;
                try { el.text = shown; }
                catch { /* element may be mid-teardown */ }
                finally { _suppress = false; }
            }
        }

        // Restore original texts and drop tracking (called when the toggle goes off).
        public static void RestoreAll()
        {
            _suppress = true;
            try
            {
                for (int i = _tracked.Count - 1; i >= 0; i--)
                {
                    if (!_tracked[i].IsAlive) continue;
                    var el = _tracked[i].Target as TextElement;
                    if (el != null && _originals.TryGetValue(el, out var h))
                    {
                        try { if (!string.Equals(el.text, h.Text, StringComparison.Ordinal)) el.text = h.Text; }
                        catch { }
                    }
                }
            }
            finally
            {
                _suppress = false;
                _tracked.Clear();
            }
        }

        // Never touch user input: text fields keep their TextElement-typed inner views inside
        // a TextInputBaseField/TextField subtree, detectable from the parent chain.
        static bool IsInsideTextInput(VisualElement e)
        {
            try
            {
                for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent)
                {
                    var n = p.GetType().Name;
                    if (n == "TextField" || n.StartsWith("TextInputBaseField") || n == "TextArea" || n == "MaskField" || n == "RectField" || n == "CurveField" || n == "GradientField" || n == "TagField" || n == "LayerField" || n == "EnumFlagsField")
                        return true;
                }
            }
            catch { }
            return false;
        }
    }
}

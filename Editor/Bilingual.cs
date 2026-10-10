using System;

namespace EditorTranslator
{
    // Bilingual display: keeps the original English visible together with the translation,
    // instead of replacing it. Inline is the default because IMGUI control heights are fixed
    // by the layout pass (we only change text at draw time), so multi-line content may clip.
    internal static class Bilingual
    {
        public static string Format(string zh, string en, TranslatorSettings st)
        {
            if (string.IsNullOrEmpty(zh)) return en;
            switch (st.displayMode)
            {
                case BilingualMode.ChineseOnly:
                    return zh;
                case BilingualMode.Stacked:
                    return st.overflowGuard && DisplayWidth(zh) + 1 + DisplayWidth(en) > 90
                        ? zh
                        : zh + "\n" + en;
                default:
                    string inline = zh + " (" + en + ")";
                    return st.overflowGuard && DisplayWidth(inline) > DisplayWidth(en) * 1.8f + 4
                        ? zh
                        : inline;
            }
        }

        // CJK glyphs are roughly twice as wide as Latin ones in the editor font.
        static int DisplayWidth(string s)
        {
            int w = 0;
            foreach (char c in s) w += c >= 0x2E80 ? 2 : 1;
            return w;
        }
    }
}

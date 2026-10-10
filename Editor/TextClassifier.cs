using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EditorTranslator
{
    // Heuristic filter: decides whether an IMGUI string is UI copy worth translating,
    // or data/identifier noise (asset paths, file names, acronyms, camelCase symbols...).
    internal static class TextClassifier
    {
        static readonly Dictionary<string, bool> _cache = new Dictionary<string, bool>(1024);

        static readonly HashSet<string> Acronyms = new HashSet<string>
        {
            "ID","GUID","UUID","URL","URI","URN","FPS","GPU","CPU","GUI","SDK","API","JSON","CSV","XML","HTML",
            "UV","UVW","UWP","DPI","PPI","PNG","JPG","JPEG","GIF","BMP","DDS","EXR","HDR","TGA","PSD","SVG",
            "FBX","OBJ","USD","USDA","USDC","USDZ","GLB","GLTF","DAE","WAV","MP3","OGG","FLAC","M4A",
            "MP4","AVI","MOV","MKV","WEBM","RPM","MS","KB","MB","GB","TB","V1","V2","V3","V4","V5",
            "XYZ","XY","RGB","RGBA","HSV","HSL","CMYK","LOD","MIP","PBS","PBR","SDR","LDR",
            "TODO","FIXME","NOTE","OK","NO","VR","AR","XR","MR","DX11","DX12","IOS","HTTP","HTTPS","TCP","UDP",
            "IP","DNS","SSH","SSL","TLS","CRC","MD5","SHA1","SHA256","UTF8","ASCII","NAN","INF","EOF","ETC","VS"
        };

        public static void ClearCache()
        {
            lock (_cache) _cache.Clear();
        }

        public static bool ShouldTranslate(string s, TranslatorSettings st)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(s, out var v)) return v;
                if (_cache.Count > 4096) _cache.Clear();
                v = Evaluate(s, st);
                _cache[s] = v;
                return v;
            }
        }

        static bool Evaluate(string s, TranslatorSettings st)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            int len = s.Length;
            if (len < 2 || len > st.maxTextLength) return false;
            if (s.IndexOf('\n') >= 0 || s.IndexOf('/') >= 0 || s.IndexOf('\\') >= 0) return false;
            if (s.IndexOf("://", StringComparison.Ordinal) >= 0) return false;
            if (s[0] == '{' && s[len - 1] == '}') return false;

            bool hasLetter = false, hasCJK = false, hasKana = false, hasDigit = false, hasUpper = false;
            foreach (char c in s)
            {
                if (c >= 'a' && c <= 'z') hasLetter = true;
                else if (c >= 'A' && c <= 'Z') { hasLetter = true; hasUpper = true; }
                else if (c >= 0x4E00 && c <= 0x9FFF) hasCJK = true;
                else if (c >= 0x3040 && c <= 0x30FF) hasKana = true;
                else if (c >= '0' && c <= '9') hasDigit = true;
            }
            // Already Chinese (or pure-kanji text we cannot safely distinguish) — skip.
            if (hasCJK && !hasKana) return false;
            if (!hasLetter && !hasKana) return false;

            bool hasSpace = s.IndexOf(' ') >= 0;
            if (!hasSpace)
            {
                if (hasDigit) return false;                                    // "2D", "H.264", "4096x2160"
                if (Acronyms.Contains(s)) return false;                        // "ID", "FBX"
                if (s.IndexOf('_') >= 0) return false;                         // snake_case identifiers
                if (len > 14 && !hasUpper && s[0] >= 'a' && s[0] <= 'z') return false; // "onValueChanged"
                if (len > 18 && CountUpper(s) >= 3) return false;              // PascalCase class names
            }

            var rx = st.ignoreRegex;
            if (rx != null && rx.Count > 0)
            {
                for (int i = 0; i < rx.Count; i++)
                {
                    var p = rx[i];
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    try { if (Regex.IsMatch(s, p)) return false; }
                    catch { }
                }
            }
            return true;
        }

        static int CountUpper(string s)
        {
            int n = 0;
            foreach (char c in s) if (c >= 'A' && c <= 'Z') n++;
            return n;
        }
    }
}

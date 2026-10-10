using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EditorTranslator
{
    // Background translation backends. All methods are blocking and meant to run on worker threads.
    internal static class TranslationService
    {
        /// Returns translations aligned with the input order.
        public static List<string> TranslateBatch(List<string> texts, TranslatorSettings st, string apiKey)
        {
            if (st.provider == TranslatorProvider.OpenAICompatible)
                return TranslateOpenAI(texts, st, apiKey);
            if (st.provider == TranslatorProvider.MyMemory)
                return TranslateMyMemory(texts, st);
            return TranslateGoogle(texts, st);
        }

        // ---------- MyMemory (free, anonymous, lenient rate limits) ----------

        static List<string> TranslateMyMemory(List<string> texts, TranslatorSettings st)
        {
            var results = new string[texts.Count];
            for (int i = 0; i < texts.Count; i++)
            {
                if (i > 0) Thread.Sleep(300);
                results[i] = MyMemoryOne(texts[i], st);
            }
            return results.ToList();
        }

        static string MyMemoryOne(string text, TranslatorSettings st)
        {
            // MyMemory needs an explicit source language; pick ja for kana text, en otherwise.
            bool hasKana = false;
            foreach (char c in text)
                if (c >= 0x3040 && c <= 0x30FF) { hasKana = true; break; }
            string src = hasKana ? "ja" : "en";
            string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) +
                         "&langpair=" + src + "%7C" + Uri.EscapeDataString(st.googleTarget);
            string resp = HttpJson(url, null, null, st.requestTimeout * 1000);
            var parsed = MiniJson.Parse(resp) as Dictionary<string, object>;
            var data = parsed?["responseData"] as Dictionary<string, object>;
            var translated = data?["translatedText"] as string;
            if (string.IsNullOrWhiteSpace(translated))
                throw new Exception("MyMemory returned no translation: " + Trunc(resp, 200));
            // The free tier sometimes answers with quota/error prose instead of a translation.
            if (translated.IndexOf("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                translated.IndexOf("QUERY LENGTH LIMIT", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new Exception("MyMemory quota/limit: " + Trunc(translated, 120));
            return System.Net.WebUtility.HtmlDecode(translated);
        }

        // ---------- OpenAI-compatible (chat/completions) ----------

        static List<string> TranslateOpenAI(List<string> texts, TranslatorSettings st, string apiKey)
        {
            var messages = new List<object>
            {
                new Dictionary<string, object> { { "role", "system" }, { "content", BuildPrompt(st) } },
                new Dictionary<string, object> { { "role", "user" }, { "content", MiniJson.Serialize(texts) } },
            };
            var req = new Dictionary<string, object>
            {
                { "model", st.openaiModel },
                { "temperature", 0.1 },
                { "messages", messages },
            };
            string url = st.openaiEndpoint.TrimEnd('/') + "/chat/completions";
            string resp = HttpJson(url, MiniJson.Serialize(req), string.IsNullOrEmpty(apiKey) ? null : "Bearer " + apiKey, st.requestTimeout * 1000);

            var parsed = MiniJson.Parse(resp) as Dictionary<string, object>;
            if (parsed == null || !parsed.TryGetValue("choices", out var choicesObj) || !(choicesObj is List<object> choices) || choices.Count == 0)
                throw new Exception("Unexpected LLM response: " + Trunc(resp, 300));
            var msg = (choices[0] as Dictionary<string, object>)?["message"] as Dictionary<string, object>;
            var content = msg?["content"] as string;
            if (string.IsNullOrEmpty(content))
                throw new Exception("Empty LLM content");

            // Strip markdown fences if the model wrapped its JSON.
            int l = content.IndexOf('{');
            int r = content.LastIndexOf('}');
            if (l < 0 || r <= l) throw new Exception("LLM returned no JSON object: " + Trunc(content, 300));
            var inner = MiniJson.Parse(content.Substring(l, r - l + 1)) as Dictionary<string, object>;
            if (inner == null || !(inner.TryGetValue("translations", out var trObj) && trObj is List<object> tr))
                throw new Exception("LLM JSON missing 'translations': " + Trunc(content, 300));

            var result = new List<string>(tr.Count);
            result.AddRange(tr.Select(t => t as string));
            return result;
        }

        static string BuildPrompt(TranslatorSettings st)
        {
            var sb = new StringBuilder();
            sb.Append("You are a professional localizer for the Unity Editor user interface. ");
            sb.Append("Every string you receive is UI copy drawn inside Unity Editor windows (Inspector, plugin windows, dialogs, settings panels): ");
            sb.Append("labels, buttons, headings, tooltips, field names.\n");
            sb.Append("Translate each string into ").Append(st.targetLang).Append(" following these rules:\n");
            sb.Append("1. TERMINOLOGY: always prefer the official Unity Simplified Chinese language pack terms when they exist. ");
            sb.Append("Key terms: GameObject=游戏对象, Component=组件, Material=材质, Shader=着色器, Texture=纹理, Mesh=网格, Transform=变换, ");
            sb.Append("Camera=摄像机, Light=灯光, Animation=动画, Animator=动画控制器, Collider=碰撞体, Rigidbody=刚体, Scene=场景, Project=项目, ");
            sb.Append("Prefab=预制件, Inspector=检查器, Script=脚本, Sprite=精灵, Render=渲染, Physics=物理, Import=导入, Export=导出, ");
            sb.Append("Save=保存, Load=加载, Apply=应用, Reset=重置, Create=创建, Delete=删除, Rename=重命名, Duplicate=复制, Settings=设置, Advanced=高级.\n");
            var g = st.GetGlossaryFast();
            if (g != null && g.Count > 0)
            {
                sb.Append("2. USER GLOSSARY (mandatory — when a string matches one of these exactly, use exactly this translation):\n");
                foreach (var kv in g)
                    sb.Append("  \"").Append(kv.Key).Append("\" => \"").Append(kv.Value).Append("\"\n");
            }
            sb.Append("3. STAY LITERAL: a single common word gets its single most common UI meaning. Do NOT invent context ");
            sb.Append("(e.g. \"All\"=全部, never a domain-specific reading). Do not add, omit or explain anything.\n");
            sb.Append("4. Keep product names, file names and proper nouns unchanged. Keep placeholders like {0}, {name}, %s, %d intact.\n");
            sb.Append("5. Keep it concise like UI labels. If a string is an identifier or code-like, return it unchanged.\n");
            sb.Append("Reply with ONLY a JSON object {\"translations\": [...]} with exactly the same number of items, in the same order.");
            return sb.ToString();
        }

        // ---------- Google free endpoint ----------

        static List<string> TranslateGoogle(List<string> texts, TranslatorSettings st)
        {
            // Strictly serial with a small delay: the free endpoint rate-limits hard (HTTP 429),
            // especially behind shared proxy exit IPs. Slow-but-steady beats burst-then-banned.
            var results = new string[texts.Count];
            for (int i = 0; i < texts.Count; i++)
            {
                if (i > 0) Thread.Sleep(400);
                results[i] = GoogleOne(texts[i], st);
            }
            return results.ToList();
        }

        static string GoogleOne(string text, TranslatorSettings st)
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=" +
                         Uri.EscapeDataString(st.googleTarget) + "&dt=t&q=" + Uri.EscapeDataString(text);
            string resp = HttpJson(url, null, null, st.requestTimeout * 1000);
            var outer = MiniJson.Parse(resp) as List<object>;
            if (outer == null || outer.Count == 0 || !(outer[0] is List<object> segs))
                throw new Exception("Unexpected Google response: " + Trunc(resp, 300));
            var sb = new StringBuilder();
            foreach (var seg in segs)
            {
                if (seg is List<object> pair && pair.Count > 0 && pair[0] is string piece)
                    sb.Append(piece);
            }
            var s = sb.ToString();
            if (string.IsNullOrWhiteSpace(s)) throw new Exception("Google returned empty translation");
            return s;
        }

        // ---------- HTTP ----------

        static string HttpJson(string url, string body, string auth, int timeoutMs)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = body != null ? "POST" : "GET";
            req.ContentType = "application/json";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "UnityEditorTranslator/0.1";
            if (!string.IsNullOrEmpty(auth)) req.Headers[HttpRequestHeader.Authorization] = auth;
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            }
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (WebException we)
            {
                int code = 0;
                string detail = "";
                try
                {
                    var hr = (HttpWebResponse)we.Response;
                    code = (int)hr.StatusCode;
                    using (var sr = new StreamReader(hr.GetResponseStream(), Encoding.UTF8))
                        detail = sr.ReadToEnd();
                }
                catch { }
                if (code == 429)
                    throw new Exception("HTTP 429 — 翻译服务限流（免费接口对共享 IP 很常见）。可在设置中改用 OpenAI 兼容后端。");
                throw new Exception("HTTP " + code + " — " + we.Message +
                                    (string.IsNullOrEmpty(detail) ? "" : " | " + Trunc(detail, 400)));
            }
        }

        static string Trunc(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}

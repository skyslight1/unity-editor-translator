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
            return TranslateGoogle(texts, st);
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

        static string BuildPrompt(TranslatorSettings st) =>
            "You are a professional UI localizer for Unity Editor plugins. " +
            "Translate each given UI string into " + st.targetLang + ". " +
            "Auto-detect the source language. Keep it concise like UI labels. " +
            "Preserve placeholders like {0}, {name}, %s, %d, and keep file formats and well-known technical terms untranslated. " +
            "If an item is an identifier or should not be translated, return it unchanged. " +
            "Reply with ONLY a JSON object {\"translations\": [...]} with exactly the same number of items, in the same order.";

        // ---------- Google free endpoint ----------

        static List<string> TranslateGoogle(List<string> texts, TranslatorSettings st)
        {
            var results = new string[texts.Count];
            using (var sem = new SemaphoreSlim(4))
            {
                var tasks = new Task[texts.Count];
                for (int i = 0; i < texts.Count; i++)
                {
                    int idx = i;
                    tasks[i] = Task.Run(async () =>
                    {
                        await sem.WaitAsync();
                        try { results[idx] = GoogleOne(texts[idx], st); }
                        finally { sem.Release(); }
                    });
                }
                Task.WaitAll(tasks);
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
                throw new Exception("HTTP " + code + " — " + we.Message +
                                    (string.IsNullOrEmpty(detail) ? "" : " | " + Trunc(detail, 400)));
            }
        }

        static string Trunc(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}

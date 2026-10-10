using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EditorTranslator
{
    // Browse / edit / ignore / delete cached translations, and import/export the cache.
    internal class TranslationManagerWindow : EditorWindow
    {
        Vector2 _scroll;
        string _search = "";

        public static void Open()
        {
            var w = GetWindow<TranslationManagerWindow>("Translation Manager");
            w.minSize = new Vector2(620, 420);
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _search = EditorGUILayout.TextField("搜索", _search);
                if (GUILayout.Button("重试失败项", GUILayout.Width(90)))
                {
                    int n = TranslationCache.ResetFailed();
                    EditorUtility.DisplayDialog("Editor Translator", "已重置 " + n + " 条失败记录，返回队列重试。", "好");
                }
                if (GUILayout.Button("立即翻译队列", GUILayout.Width(100)))
                    TranslationDispatcher.ForceTick();
                if (GUILayout.Button("导出", GUILayout.Width(48)))
                {
                    var p = EditorUtility.SaveFilePanel("导出翻译缓存", "", "editor-translator-cache", "json");
                    if (!string.IsNullOrEmpty(p))
                        File.WriteAllText(p, TranslationCache.ExportJson());
                }
                if (GUILayout.Button("导入", GUILayout.Width(48)))
                {
                    var p = EditorUtility.OpenFilePanel("导入翻译缓存", "", "json");
                    if (!string.IsNullOrEmpty(p))
                    {
                        try
                        {
                            int n = TranslationCache.ImportJson(File.ReadAllText(p));
                            EditorUtility.DisplayDialog("Editor Translator", "导入 " + n + " 条。", "好");
                        }
                        catch (System.Exception e)
                        {
                            EditorUtility.DisplayDialog("Editor Translator", "导入失败: " + e.Message, "好");
                        }
                    }
                }
            }
            EditorGUILayout.HelpBox(
                "状态：Pending=排队中  Done=已翻译  Failed=失败（3 次后停止）  Ignored=已忽略（永不翻译）。\n" +
                "直接编辑译文输入框可人工修正，回车生效。", MessageType.None);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var entries = TranslationCache.AllEntries();
            IEnumerable<CacheEntry> q = entries;
            if (!string.IsNullOrEmpty(_search))
            {
                var kw = _search.ToLowerInvariant();
                q = q.Where(e =>
                    (e.en != null && e.en.ToLowerInvariant().Contains(kw)) ||
                    (e.zh != null && e.zh.Contains(_search)));
            }
            int shown = 0;
            foreach (var e in q.Take(300))
            {
                DrawEntry(e);
                shown++;
            }
            EditorGUILayout.EndScrollView();

            int total = entries.Count;
            if (total > shown)
                EditorGUILayout.LabelField("… 其余 " + (total - shown) + " 条未显示（用搜索框过滤）");
        }

        static void DrawEntry(CacheEntry e)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(e.en, e.en), EditorStyles.miniLabel, GUILayout.Width(240));
                var nz = EditorGUILayout.TextField(e.zh ?? "");
                if (nz != (e.zh ?? "")) TranslationCache.SetManual(e.en, nz);
                GUILayout.Label(e.state.ToString(), EditorStyles.miniLabel, GUILayout.Width(54));
                if (e.state == EntryState.Ignored)
                {
                    if (GUILayout.Button("恢复", GUILayout.Width(42))) TranslationCache.SetIgnored(e.en, false);
                }
                else if (GUILayout.Button("忽略", GUILayout.Width(42)))
                {
                    TranslationCache.SetIgnored(e.en, true);
                }
                if (GUILayout.Button("删", GUILayout.Width(26))) TranslationCache.Remove(e.en);
            }
        }
    }
}

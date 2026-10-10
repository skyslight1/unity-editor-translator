using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace EditorTranslator
{
    internal class TranslatorSettingsWindow : EditorWindow
    {
        bool _showKey;
        Vector2 _scroll;
        string _testInput = "Generate Mesh";
        string _testResult = "";
        bool _testBusy;

        public static void Open()
        {
            var w = GetWindow<TranslatorSettingsWindow>(true, "Editor Translator 设置");
            w.minSize = new Vector2(520, 620);
        }

        void OnGUI()
        {
            var st = TranslatorSettings.Instance;
            EditorGUI.BeginChangeCheck();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "工作原理：拦截所有 IMGUI 绘制文本（GUIStyle.Draw），AI 翻译后以双语显示。\n" +
                "覆盖：Inspector、插件窗口、设置面板等。不覆盖：顶部/右键原生菜单、系统对话框、Tooltip、UI Toolkit 界面。",
                MessageType.Info);

            EditorGUILayout.Space(4);
            bool enabled = TranslatorSettings.Enabled;
            bool newEnabled = EditorGUILayout.ToggleLeft("  启用翻译（双语显示，Ctrl+Alt+T）", enabled, EditorStyles.boldLabel);
            if (newEnabled != enabled)
            {
                TranslatorSettings.Enabled = newEnabled;
                if (newEnabled) TranslatorCore.Enable(); else TranslatorCore.Disable();
                TranslationDispatcher.RepaintAllViews();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("翻译服务", EditorStyles.boldLabel);
            var newProvider = (TranslatorProvider)EditorGUILayout.EnumPopup(new GUIContent("后端", "Google 免费接口 / MyMemory 免费接口均无需密钥；OpenAI 兼容接口质量最高"), st.provider);
            if (newProvider != st.provider) { st.provider = newProvider; TextClassifier.ClearCache(); }

            if (st.provider == TranslatorProvider.OpenAICompatible)
            {
                st.openaiEndpoint = EditorGUILayout.TextField(new GUIContent("Endpoint", "OpenAI 兼容根地址，自动拼接 /chat/completions"), st.openaiEndpoint);
                st.openaiModel = EditorGUILayout.TextField("Model", st.openaiModel);
                DrawApiKey();
                EditorGUILayout.HelpBox("任意 OpenAI 兼容接口（/v1/chat/completions），支持各种中转站。", MessageType.None);
            }
            else if (st.provider == TranslatorProvider.MyMemory)
            {
                st.googleTarget = EditorGUILayout.TextField("目标语言代码", st.googleTarget);
                EditorGUILayout.HelpBox(
                    "MyMemory 免费接口（api.mymemory.translated.net，匿名可用，无需密钥）。\n" +
                    "匿名配额约 5000 字符/天，机器翻译质量一般；想要 AI/LLM 翻译质量请切换到 OpenAI 兼容后端。", MessageType.None);
            }
            else
            {
                st.googleTarget = EditorGUILayout.TextField("目标语言代码", st.googleTarget);
                EditorGUILayout.HelpBox(
                    "Google 免费接口（translate.googleapis.com，无需密钥），需要网络能访问 Google。\n" +
                    "共享代理 IP 下经常被限流 (429)，遇到限流请换 MyMemory 或 OpenAI 兼容后端。", MessageType.None);
            }
            st.targetLang = EditorGUILayout.TextField(new GUIContent("目标语言（提示词）", "仅 OpenAI 后端使用"), st.targetLang);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("双语显示", EditorStyles.boldLabel);
            st.displayMode = (BilingualMode)EditorGUILayout.EnumPopup(
                new GUIContent("格式", "Inline: 中文 (English)；Stacked: 中文+换行+English（可能被裁切）；ChineseOnly: 仅中文"),
                st.displayMode);
            st.overflowGuard = EditorGUILayout.Toggle(new GUIContent("过宽时仅显示译文", "译文+原文超出原宽度太多时只显示中文，避免截断"), st.overflowGuard);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("行为", EditorStyles.boldLabel);
            st.autoTranslate = EditorGUILayout.Toggle(new GUIContent("自动翻译新文本", "新出现的界面文本自动入队翻译（关闭后只翻译手动触发的内容）"), st.autoTranslate);
            bool prevAssetNames = st.translateAssetNames;
            st.translateAssetNames = EditorGUILayout.Toggle(new GUIContent("翻译资产名", "默认关闭：Hierarchy / Project / Scene 等窗口里的场景物体与资产名保持原文。\n开启后这些名字也会被 AI 翻译——它们是数据名称，译文可能不符合预期（如角色名被意译），可在 Manager 里逐条修正或忽略。"), st.translateAssetNames);
            if (st.translateAssetNames != prevAssetNames)
                TranslationCache.ClearSkips(); // let previously skipped asset names flow again
            st.maxBatch = EditorGUILayout.IntSlider("每批条数", st.maxBatch, 1, 60);
            st.maxInflight = EditorGUILayout.IntSlider("并发批数", st.maxInflight, 1, 6);
            st.maxTextLength = EditorGUILayout.IntSlider("最长字符数", st.maxTextLength, 40, 400);
            st.requestTimeout = EditorGUILayout.IntSlider("请求超时 (秒)", st.requestTimeout, 5, 120);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("窗口黑名单（每行一个，窗口类全名前缀匹配）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("默认已排除 Hierarchy / Project / Console / Scene / Game / Animator / Timeline —— 这些窗口显示的是你的资产与对象名，不能翻译。", MessageType.None);
            DrawListArea(v => st.windowBlacklist = v, st.windowBlacklist);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("忽略正则（每行一个，命中的文本永不翻译）", EditorStyles.boldLabel);
            DrawListArea(v => { st.ignoreRegex = v; TextClassifier.ClearCache(); }, st.ignoreRegex);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("工具", EditorStyles.boldLabel);
            _testInput = EditorGUILayout.TextField("测试文本", _testInput);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_testBusy || string.IsNullOrEmpty(_testInput)))
                {
                    if (GUILayout.Button("翻译测试", GUILayout.Width(90)))
                        RunTest(st);
                }
                if (GUILayout.Button("打开缓存目录", GUILayout.Width(110)))
                    EditorUtility.RevealInFinder("Library/EditorTranslator");
                if (GUILayout.Button("清空缓存", GUILayout.Width(90)))
                {
                    if (EditorUtility.DisplayDialog("Editor Translator", "确定清空全部翻译缓存？（设置文件不受影响）", "清空", "取消"))
                        TranslationCache.Clear();
                }
            }
            if (!string.IsNullOrEmpty(_testResult))
                EditorGUILayout.LabelField(_testResult, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(4);
            var err = TranslationDispatcher.LastError;
            if (!string.IsNullOrEmpty(err))
                EditorGUILayout.HelpBox("最近错误: " + err, MessageType.Warning);
            EditorGUILayout.LabelField("队列待翻译: " + TranslationCache.PendingCount + "    缓存条目: " + TranslationCache.TotalCount);

            EditorGUILayout.EndScrollView();

            if (EditorGUI.EndChangeCheck())
                TranslatorSettings.Save();
        }

        void RunTest(TranslatorSettings st)
        {
            _testBusy = true;
            _testResult = "翻译中…";
            try
            {
                var input = _testInput;
                var t = Task.Run(() => TranslationService.TranslateBatch(
                    new List<string> { input }, st, TranslatorSettings.ApiKey));
                t.Wait();
                _testResult = "结果: " + (t.Result.Count > 0 ? t.Result[0] : "(空)");
            }
            catch (Exception e)
            {
                var msg = e.Message;
                if (e.InnerException != null) msg = e.InnerException.Message;
                _testResult = "失败: " + msg;
            }
            _testBusy = false;
        }

        void DrawApiKey()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _showKey = EditorGUILayout.Toggle("显示", _showKey, GUILayout.Width(46));
                var key = TranslatorSettings.ApiKey;
                var nk = _showKey
                    ? EditorGUILayout.TextField("API Key", key)
                    : EditorGUILayout.PasswordField("API Key", key);
                if (nk != key) TranslatorSettings.ApiKey = nk;
            }
        }

        static void DrawListArea(Action<List<string>> setter, List<string> list)
        {
            var text = string.Join("\n", (list ?? new List<string>()).ToArray());
            var nt = EditorGUILayout.TextArea(text, GUILayout.MinHeight(56));
            if (nt != text)
            {
                setter(nt.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList());
                TranslatorSettings.Save();
            }
        }
    }
}

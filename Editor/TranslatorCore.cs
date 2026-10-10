using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EditorTranslator
{
    [InitializeOnLoad]
    internal static class TranslatorCore
    {
        public const string ToggleMenuPath = "Tools/Editor Translator/Enable Translation";
        const string HarmonyId = "com.kuro.editor-translator";

        static HarmonyBridge _bridge;
        static bool _patched;
        static MethodInfo _prefix;
        static MethodInfo _postfix;

        public static bool IsRunning => _patched;

        // Temporary switch for drawing-path diagnostics (set via reflection during testing).
        internal static bool DebugDiagnostics;

        static TranslatorCore()
        {
            TranslatorSettings.Load();
            TranslationCache.Load();
            TranslationDispatcher.Start();

            AssemblyReloadEvents.beforeAssemblyReload += TranslationCache.SaveNow;
            EditorApplication.quitting += TranslationCache.SaveNow;

            _prefix = typeof(IMGUIInterceptor).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _postfix = typeof(IMGUIInterceptor).GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (TranslatorSettings.Enabled) Enable();
        }

        public static void Enable()
        {
            if (_patched) return;
            if (_bridge == null) _bridge = HarmonyBridge.TryCreate(HarmonyId);
            if (_bridge == null)
            {
                Debug.LogError("[EditorTranslator] 0Harmony.dll 未加载 — 请确认包内 Editor/Plugins/0Harmony.dll 存在。");
                TranslatorSettings.Enabled = false;
                return;
            }

            int n = 0;
            // "Draw" covers label/button/box text; "DrawPrefixLabel" is the path EditorGUI.PrefixLabel
            // uses for inspector field labels. DrawCursor / DrawWithTextSelection (text fields) are
            // deliberately left alone so user-typed content and asset names are never rewritten.
            var targets = typeof(GUIStyle)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => string.Equals(m.Name, "Draw", StringComparison.Ordinal) ||
                            string.Equals(m.Name, "DrawPrefixLabel", StringComparison.Ordinal))
                .Where(m => !m.IsAbstract && !m.IsGenericMethod && m.DeclaringType == typeof(GUIStyle))
                .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(GUIContent) && p.Name == "content"));

            foreach (var m in targets)
                if (_bridge.Patch(m, _prefix, _postfix)) n++;

            if (n == 0)
            {
                Debug.LogError("[EditorTranslator] 找不到可拦截的 GUIStyle.Draw 重载（Unity 版本变更？）。翻译未启用。");
                TranslatorSettings.Enabled = false;
                return;
            }

            _patched = true;
            Debug.Log("[EditorTranslator] 已启用 — 拦截了 " + n + " 个 GUIStyle.Draw 重载。未见过的新文本会自动排队翻译。");
        }

        public static void Disable()
        {
            if (!_patched) return;
            _bridge?.UnpatchAll();
            _patched = false;
            Debug.Log("[EditorTranslator] 已关闭，界面恢复原文。");
        }

        [MenuItem(ToggleMenuPath, false, 0)]
        static void Toggle()
        {
            if (TranslatorSettings.Enabled)
            {
                TranslatorSettings.Enabled = false;
                Disable();
            }
            else
            {
                TranslatorSettings.Enabled = true;
                Enable();
            }
            Menu.SetChecked(ToggleMenuPath, TranslatorSettings.Enabled);
            TranslationDispatcher.RepaintAllViews();
        }

        [MenuItem(ToggleMenuPath, true, 0)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenuPath, TranslatorSettings.Enabled);
            return true;
        }

        [MenuItem("Tools/Editor Translator/Settings...", false, 1)]
        static void OpenSettings() => TranslatorSettingsWindow.Open();

        [MenuItem("Tools/Editor Translator/Translation Manager...", false, 2)]
        static void OpenManager() => TranslationManagerWindow.Open();
    }
}

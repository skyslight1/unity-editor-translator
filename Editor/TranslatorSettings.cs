using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EditorTranslator
{
    internal enum TranslatorProvider { GoogleFree = 0, MyMemory = 1, OpenAICompatible = 2 }
    internal enum BilingualMode { Inline = 0, Stacked = 1, ChineseOnly = 2 }

    [Serializable]
    internal class TranslatorSettings
    {
        public TranslatorProvider provider = TranslatorProvider.GoogleFree;
        public string targetLang = "Simplified Chinese";
        public string googleTarget = "zh-CN";
        public string openaiEndpoint = "https://api.openai.com/v1";
        public string openaiModel = "gpt-4o-mini";
        public BilingualMode displayMode = BilingualMode.Inline;
        public bool overflowGuard = true;
        public bool autoTranslate = true;
        public bool translateAssetNames = false;
        public bool enableUIToolkit = true;
        public int maxBatch = 20;
        public int maxInflight = 1;
        public int maxTextLength = 200;
        public int requestTimeout = 30;
        public List<string> windowBlacklist = DefaultBlacklist();
        public List<string> ignoreRegex = new List<string>();
        public List<string> glossary = new List<string>(); // "English=中文" per line

        public static List<string> DefaultBlacklist() => new List<string>
        {
            "UnityEditor.SceneHierarchyWindow",
            "UnityEditor.ProjectBrowser",
            "UnityEditor.ConsoleWindow",
            "UnityEditor.GameView",
            "UnityEditor.SceneView",
            "UnityEditor.Graphs.AnimatorControllerTool",
            "UnityEditor.Timeline.TimelineWindow",
            "UnityEditor.ObjectPicker",
        };

        const string SettingsPath = "ProjectSettings/EditorTranslator.json";
        const string ApiKeyPref = "EditorTranslator.ApiKey";

        static TranslatorSettings _instance;
        public static TranslatorSettings Instance => _instance ?? (_instance = new TranslatorSettings());

        // --- glossary fast lookup (rebuilt on every Save) ---
        internal Dictionary<string, string> GlossaryFast;
        internal int GlossaryVersion;

        public Dictionary<string, string> GetGlossaryFast()
        {
            if (GlossaryFast == null || GlossaryFastStamp != GlossaryVersion)
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (glossary != null)
                    foreach (var line in glossary)
                    {
                        var i = line.IndexOf('=');
                        if (i <= 0 || i == line.Length - 1) continue;
                        var en = line.Substring(0, i).Trim();
                        var zh = line.Substring(i + 1).Trim();
                        if (en.Length > 0 && zh.Length > 0) d[en] = zh;
                    }
                GlossaryFast = d;
                GlossaryFastStamp = GlossaryVersion;
            }
            return GlossaryFast;
        }

        internal int GlossaryFastStamp;

        public static void Load()
        {
            var s = new TranslatorSettings();
            try
            {
                if (File.Exists(SettingsPath))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(SettingsPath), s);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EditorTranslator] Failed to load settings, using defaults: " + e.Message);
            }
            _instance = s;
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(Instance, true));
                Instance.GlossaryVersion++; // invalidate fast glossary
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EditorTranslator] Failed to save settings: " + e.Message);
            }
        }

        // API key is stored in EditorPrefs (user-wide, never written into the project file).
        public static string ApiKey
        {
            get => EditorPrefs.GetString(ApiKeyPref, "");
            set => EditorPrefs.SetString(ApiKeyPref, value ?? "");
        }

        // Enable state is per-project (keyed by project path).
        static string EnabledKey => "EditorTranslator.Enabled." + Application.dataPath;

        public static bool Enabled
        {
            get => EditorPrefs.GetInt(EnabledKey, 0) == 1;
            set => EditorPrefs.SetInt(EnabledKey, value ? 1 : 0);
        }
    }
}

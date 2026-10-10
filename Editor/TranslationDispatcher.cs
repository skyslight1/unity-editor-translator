using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace EditorTranslator
{
    // EditorApplication.update-driven scheduler: batches pending strings, translates on worker
    // threads, writes results back into the cache and repaints views when new text arrives.
    internal static class TranslationDispatcher
    {
        static double _lastCheck;
        static int _inflight;
        static volatile string _lastError = "";
        static volatile bool _needsRepaint;
        static double _lastRepaint;

        public static string LastError => _lastError;

        public static void Start()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        public static void Stop()
        {
            EditorApplication.update -= Tick;
        }

        public static void RepaintAllViews() => RepaintViews();

        /// Force one dispatch round regardless of the autoTranslate switch (used by the manager window).
        public static void ForceTick()
        {
            _lastCheck = 0.0;
            SendBatch();
        }

        static void Tick()
        {
            TranslationCache.TickAutoSave();

            if (_needsRepaint && EditorApplication.timeSinceStartup - _lastRepaint > 1.0)
            {
                _needsRepaint = false;
                _lastRepaint = EditorApplication.timeSinceStartup;
                RepaintViews();
                UITKInterceptor.RefreshTracked(); // UITK is retained-mode: push finished translations
            }

            if (!TranslatorCore.IsRunning) return;
            if (!TranslatorSettings.Instance.autoTranslate) return;
            if (EditorApplication.timeSinceStartup - _lastCheck < 2.0) return;
            _lastCheck = EditorApplication.timeSinceStartup;

            SendBatch();
        }

        static void SendBatch()
        {
            var st = TranslatorSettings.Instance;
            int inflight = Interlocked.CompareExchange(ref _inflight, 0, 0);
            if (inflight >= Math.Max(1, st.maxInflight)) return;

            var batch = TranslationCache.SnapshotPending(st.maxBatch);
            if (batch.Count == 0) return;

            var texts = new List<string>(batch.Count);
            foreach (var e in batch) texts.Add(e.en);

            Interlocked.Increment(ref _inflight);
            var settings = st;
            var key = TranslatorSettings.ApiKey;
            Task.Run(() =>
            {
                try
                {
                    List<string> result = null;
                    Exception err = null;
                    try { result = TranslationService.TranslateBatch(texts, settings, key); }
                    catch (Exception ex) { err = ex; }

                    if (err != null)
                    {
                        _lastError = err.Message;
                        TranslationCache.SetFailed(texts);
                    }
                    else if (result == null || result.Count != texts.Count)
                    {
                        _lastError = "翻译服务返回条数不匹配 (" + (result?.Count.ToString() ?? "null") + "/" + texts.Count + ")";
                        TranslationCache.SetFailed(texts);
                    }
                    else
                    {
                        _lastError = "";
                        for (int i = 0; i < texts.Count; i++)
                        {
                            var zh = result[i];
                            if (string.IsNullOrWhiteSpace(zh)) TranslationCache.SetFailed(new List<string> { texts[i] });
                            else TranslationCache.SetDone(texts[i], zh);
                        }
                    }
                    _needsRepaint = true;
                }
                finally
                {
                    Interlocked.Decrement(ref _inflight);
                }
            });
        }

        static void RepaintViews()
        {
            try
            {
                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    w.Repaint();
            }
            catch { }
        }
    }
}

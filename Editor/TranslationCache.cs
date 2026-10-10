using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EditorTranslator
{
    internal enum EntryState { Pending = 0, Done = 1, Failed = 2, Ignored = 3 }

    internal class CacheEntry
    {
        public string en;
        public string zh;
        public EntryState state;
        public int failCount;
        public long ts;
    }

    // Thread-safe translation store: IMGUI hot path reads (main thread),
    // background batch workers write. Persisted to Library/EditorTranslator/cache.json.
    internal static class TranslationCache
    {
        const string CacheDir = "Library/EditorTranslator";
        const string CachePath = CacheDir + "/cache.json";
        const int RetryCooldownSec = 120;
        const int MaxFailCount = 3;

        static readonly object _lock = new object();
        static readonly Dictionary<string, CacheEntry> _map = new Dictionary<string, CacheEntry>(2048);
        static readonly HashSet<string> _skip = new HashSet<string>(); // runtime-only negative cache (classifier rejects)
        static bool _dirty;
        static double _nextSave;

        [Serializable] class Item { public string en; public string zh; public int state; }
        [Serializable] class CacheFile { public List<Item> items = new List<Item>(); }

        public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static void Load()
        {
            lock (_lock)
            {
                _map.Clear();
                _skip.Clear();
                try
                {
                    if (!File.Exists(CachePath)) return;
                    var cf = JsonUtility.FromJson<CacheFile>(File.ReadAllText(CachePath));
                    if (cf?.items == null) return;
                    foreach (var it in cf.items)
                    {
                        if (string.IsNullOrEmpty(it.en)) continue;
                        _map[it.en] = new CacheEntry { en = it.en, zh = it.zh, state = (EntryState)it.state, ts = Now() };
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[EditorTranslator] Failed to load cache: " + e.Message);
                }
            }
        }

        public static void SaveNow()
        {
            lock (_lock)
            {
                _dirty = false;
                _nextSave = EditorApplication.timeSinceStartup + 30.0;
                try
                {
                    Directory.CreateDirectory(CacheDir);
                    var cf = new CacheFile();
                    foreach (var kv in _map)
                    {
                        var e = kv.Value;
                        cf.items.Add(new Item { en = e.en, zh = e.zh, state = (int)e.state });
                    }
                    File.WriteAllText(CachePath, JsonUtility.ToJson(cf));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[EditorTranslator] Failed to save cache: " + e.Message);
                }
            }
        }

        public static void TickAutoSave()
        {
            if (!_dirty) return;
            if (EditorApplication.timeSinceStartup < _nextSave) return;
            SaveNow();
        }

        /// Called from the IMGUI hot path (main thread). Returns the translation if ready, else null
        /// (queueing new strings for translation as a side effect).
        public static string Lookup(string en)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(en, out var e))
                {
                    if (e.state == EntryState.Done && !string.IsNullOrEmpty(e.zh)) return e.zh;
                    return null; // Pending / Failed / Ignored
                }
                if (_skip.Contains(en)) return null;
                if (!TextClassifier.ShouldTranslate(en, TranslatorSettings.Instance))
                {
                    _skip.Add(en);
                    return null;
                }
                _map[en] = new CacheEntry { en = en, state = EntryState.Pending, ts = Now() };
                _dirty = true;
                return null;
            }
        }

        public static void SetDone(string en, string zh)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(en, out var e))
                {
                    if (e.state == EntryState.Ignored) return;
                    e.zh = zh;
                    e.state = EntryState.Done;
                    e.ts = Now();
                }
                else
                {
                    _map[en] = new CacheEntry { en = en, zh = zh, state = EntryState.Done, ts = Now() };
                }
                _dirty = true;
            }
        }

        public static void SetFailed(List<string> ens)
        {
            lock (_lock)
            {
                long now = Now();
                foreach (var en in ens)
                {
                    if (!_map.TryGetValue(en, out var e)) continue;
                    if (e.state != EntryState.Pending) continue;
                    e.failCount++;
                    e.state = e.failCount >= MaxFailCount ? EntryState.Failed : EntryState.Pending;
                    e.ts = now; // cooldown timestamp
                }
                _dirty = true;
            }
        }

        public static void SetIgnored(string en, bool ignored)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(en, out var e))
                {
                    e.state = ignored ? EntryState.Ignored : EntryState.Pending;
                    e.ts = Now();
                }
                else if (ignored)
                {
                    _map[en] = new CacheEntry { en = en, state = EntryState.Ignored, ts = Now() };
                }
                _dirty = true;
            }
        }

        public static void SetManual(string en, string zh)
        {
            lock (_lock)
            {
                if (!_map.TryGetValue(en, out var e))
                    _map[en] = e = new CacheEntry { en = en };
                e.zh = zh;
                e.state = string.IsNullOrEmpty(zh) ? EntryState.Pending : EntryState.Done;
                e.failCount = 0;
                e.ts = Now();
                _dirty = true;
            }
        }

        public static void Remove(string en)
        {
            lock (_lock)
            {
                if (_map.Remove(en)) _dirty = true;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _map.Clear();
                _skip.Clear();
                _dirty = true;
                SaveNow();
            }
        }

        /// Drops the runtime negative cache (classifier rejects). Needed when a settings change
        /// (e.g. "translate asset names" toggled on) makes previously-skipped strings translatable.
        public static void ClearSkips()
        {
            lock (_lock) _skip.Clear();
        }

        /// Failed (given up) entries become Pending again. Returns how many were reset.
        public static int ResetFailed()
        {
            lock (_lock)
            {
                int n = 0;
                long now = Now();
                foreach (var kv in _map)
                {
                    var e = kv.Value;
                    if (e.state != EntryState.Failed) continue;
                    e.state = EntryState.Pending;
                    e.failCount = 0;
                    e.ts = now;
                    n++;
                }
                if (n > 0) _dirty = true;
                return n;
            }
        }

        /// FIFO snapshot of translatable entries, skipping ones in retry cooldown.
        public static List<CacheEntry> SnapshotPending(int max)
        {
            lock (_lock)
            {
                var list = new List<CacheEntry>(Math.Min(max, 64));
                long now = Now();
                foreach (var kv in _map)
                {
                    var e = kv.Value;
                    if (e.state != EntryState.Pending) continue;
                    if (e.failCount > 0 && e.ts > now - RetryCooldownSec) continue;
                    list.Add(e);
                    if (list.Count >= max) break;
                }
                return list;
            }
        }

        public static List<CacheEntry> AllEntries()
        {
            lock (_lock) return new List<CacheEntry>(_map.Values);
        }

        public static int TotalCount { get { lock (_lock) return _map.Count; } }

        public static int PendingCount
        {
            get
            {
                lock (_lock)
                {
                    int n = 0;
                    foreach (var kv in _map)
                        if (kv.Value.state == EntryState.Pending) n++;
                    return n;
                }
            }
        }

        public static string ExportJson()
        {
            lock (_lock)
            {
                var cf = new CacheFile();
                foreach (var kv in _map)
                {
                    var e = kv.Value;
                    cf.items.Add(new Item { en = e.en, zh = e.zh, state = (int)e.state });
                }
                return JsonUtility.ToJson(cf, true);
            }
        }

        public static int ImportJson(string json)
        {
            var cf = JsonUtility.FromJson<CacheFile>(json);
            if (cf?.items == null) return 0;
            int n = 0;
            lock (_lock)
            {
                long now = Now();
                foreach (var it in cf.items)
                {
                    if (string.IsNullOrEmpty(it.en)) continue;
                    var state = (EntryState)it.state;
                    if (!_map.TryGetValue(it.en, out var e))
                    {
                        _map[it.en] = new CacheEntry { en = it.en, zh = it.zh, state = state, ts = now };
                        n++;
                    }
                    else if (e.state != EntryState.Done && e.state != EntryState.Ignored &&
                             (state == EntryState.Done || state == EntryState.Ignored))
                    {
                        e.zh = it.zh;
                        e.state = state;
                        n++;
                    }
                }
                _dirty = true;
            }
            return n;
        }
    }
}

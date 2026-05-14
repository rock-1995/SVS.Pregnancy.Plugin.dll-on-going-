using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using BepInEx.Logging;
using Il2CppInterop.Runtime;

namespace SVSPregnancy
{
    internal static class PregnancyPersistenceManager
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("SVSPregnancy.Save");

        // Path of the companion JSON for the currently loaded game slot
        private static string _currentPregDataPath;

#if SAVE_FSW
        // ── FileSystemWatcher ─────────────────────────────────────────────
        private static FileSystemWatcher _watcher;

        // Pending save set by FSW callback (background thread → main thread)
        private static volatile string _pendingSavePath;
        private static long _pendingSaveTicks;          // DateTime.UtcNow.Ticks
        private const int DebounceMs = 600;             // wait 600 ms after last FS event

        /// <summary>
        /// Start watching UserData/save/game/ for .dat writes.
        /// Call once from plugin Load().
        /// </summary>
        public static void SetupWatcher()
        {
            try
            {
                string dir = Path.Combine(BepInEx.Paths.GameRootPath, "UserData", "save", "game");
                if (!Directory.Exists(dir))
                {
                    Log.LogWarning($"[SVSPregnancy] Save dir not found: {dir}");
                    return;
                }

                _watcher = new FileSystemWatcher(dir, "*.dat")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    IncludeSubdirectories = false
                };
                _watcher.Changed += OnDatEvent;
                _watcher.Created += OnDatEvent;
                _watcher.EnableRaisingEvents = true;

                Log.LogInfo($"[SVSPregnancy] Watching {dir} for .dat saves");
            }
            catch (Exception e)
            {
                Log.LogError("[SVSPregnancy] SetupWatcher failed: " + e.Message);
            }
        }

        // Runs on a background thread — only set a flag, never touch IL2CPP objects
        private static void OnDatEvent(object sender, FileSystemEventArgs e)
        {
            string stem = Path.GetFileNameWithoutExtension(e.Name);
            // Only care about game slot saves (s_TIMESTAMP.dat) and auto-save (auto.dat)
            if (!stem.StartsWith("s_") && stem != "auto") return;

            string jsonPath = Path.Combine(Path.GetDirectoryName(e.FullPath)!, stem + "_pregnancy.json");
            _pendingSavePath = jsonPath;
            Interlocked.Exchange(ref _pendingSaveTicks, DateTime.UtcNow.Ticks);
            Log.LogInfo($"[SVSPregnancy] FSW: .dat write detected → pending {Path.GetFileName(jsonPath)}");
        }

        /// <summary>
        /// Call from main thread every frame (WorldController.Update).
        /// Flushes any pending FSW-triggered save after the debounce window.
        /// </summary>
        public static void FlushPendingSave()
        {
            string path = _pendingSavePath;
            if (path == null) return;

            long elapsed = (DateTime.UtcNow.Ticks - Interlocked.Read(ref _pendingSaveTicks)) / TimeSpan.TicksPerMillisecond;
            if (elapsed < DebounceMs) return;

            _pendingSavePath = null;
            Log.LogInfo($"[SVSPregnancy] FSW flush → saving {Path.GetFileName(path)}");
            Save(path);
        }
#endif // SAVE_FSW

        // ── Hook-driven save/load ─────────────────────────────────────────

        /// <summary>
        /// Called from WorldData.Load hook. Records the companion JSON path for this slot.
        /// Does NOT load immediately — call LoadCurrent() once the scene is fully initialized.
        /// </summary>
        public static void OnWorldLoad(string worldSavePath)
        {
            if (string.IsNullOrEmpty(worldSavePath)) return;

            if (Path.IsPathRooted(worldSavePath) || worldSavePath.Contains(Path.DirectorySeparatorChar) || worldSavePath.Contains('/'))
            {
                _currentPregDataPath = DerivePath(worldSavePath);
            }
            else
            {
                // Stem-only (e.g. "s_20260509163341782" or "auto") — resolve to save directory
                string saveDir = Path.Combine(BepInEx.Paths.GameRootPath, "UserData", "save", "game");
                string stem = Path.GetFileNameWithoutExtension(worldSavePath);
                _currentPregDataPath = Path.Combine(saveDir, stem + "_pregnancy.json");
            }

            Log.LogInfo($"[SVSPregnancy] OnWorldLoad: path queued → {Path.GetFileName(_currentPregDataPath)}");
        }

        /// <summary>
        /// Called from SimulationScene_Start_Hook after UpdateCharas(), when all
        /// PregnancyCharaControllers are fully initialized and ready to receive data.
        /// </summary>
        public static void LoadCurrent()
        {
            if (_currentPregDataPath == null)
            {
                Log.LogWarning("[SVSPregnancy] LoadCurrent: no path set, skipping");
                return;
            }
            if (!File.Exists(_currentPregDataPath))
            {
                Log.LogWarning($"[SVSPregnancy] LoadCurrent: file not found ({Path.GetFileName(_currentPregDataPath)})");
                return;
            }
            Load(_currentPregDataPath);
        }

#if SAVE_HOOK
        /// <summary>
        /// Called from WorldData.Save hooks. worldSavePath may be a full path,
        /// a relative path, or just a stem like "s_20260509161302781".
        /// </summary>
        public static void OnWorldSave(string worldSavePath)
        {
            if (string.IsNullOrEmpty(worldSavePath)) return;

            string? newPath;
            if (Path.IsPathRooted(worldSavePath) || worldSavePath.Contains(Path.DirectorySeparatorChar) || worldSavePath.Contains('/'))
            {
                // Full or relative path — derive normally
                newPath = DerivePath(worldSavePath);
            }
            else
            {
                // Just a stem/filename: resolve against the known save directory
                string saveDir = Path.Combine(BepInEx.Paths.GameRootPath, "UserData", "save", "game");
                string stem = Path.GetFileNameWithoutExtension(worldSavePath);
                newPath = Path.Combine(saveDir, stem + "_pregnancy.json");
            }

            if (newPath == null) return;
            _currentPregDataPath = newPath;
            Log.LogInfo($"[SVSPregnancy] WorldData.Save hook → saving to {Path.GetFileName(newPath)}");
            Save(_currentPregDataPath);
        }

        /// <summary>
        /// Called from Manager.Game.Save() / AutoSave() hooks if they ever fire.
        /// </summary>
        public static void SaveCurrent()
        {
            if (_currentPregDataPath == null) return;
            Save(_currentPregDataPath);
        }
#endif // SAVE_HOOK

        /// <summary>
        /// Called from ShiftFromSimNightToRoomMorning BEFORE the game reloads WorldData.
        /// Saves to the current slot AND to auto_pregnancy.json, because day-transition
        /// almost always reloads through auto.dat. This ensures OnWorldLoad finds data
        /// before the FSW debounce has a chance to flush.
        /// </summary>
        public static void SaveBeforeDayEnd()
        {
            if (_currentPregDataPath != null)
                Save(_currentPregDataPath);

            // Pre-emptive write to auto slot so day-transition WorldData.Load finds it
            string? dir = _currentPregDataPath != null
                ? Path.GetDirectoryName(_currentPregDataPath)
                : Path.Combine(BepInEx.Paths.GameRootPath, "UserData", "save", "game");
            if (dir != null)
            {
                string autoPath = Path.Combine(dir, "auto_pregnancy.json");
                if (autoPath != _currentPregDataPath)
                    Save(autoPath);
            }
        }

        // ── Internal I/O ──────────────────────────────────────────────────

        private static void Load(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                var worldCtrl = PregnancyPlugin._worldController;
                if (worldCtrl == null || !worldCtrl._inited) return;

                var entries = JsonSerializer.Deserialize<List<PregEntry>>(File.ReadAllText(path));
                if (entries == null) return;

                int loaded = 0;
                foreach (var entry in entries)
                {
                    var ctrlPtr = worldCtrl.FindPregnancyCharaControllerPtr(entry.CharaId);
                    if (ctrlPtr == IntPtr.Zero) continue;
                    var ctrl = ctrlPtr.ToObject<PregnancyCharaController>();
                    if (ctrl?._pregnancyInfo == null) continue;

                    ctrl._pregnancyInfo._day = entry.Day;
                    ctrl._pregnancyInfo._cooldown = entry.Cooldown;
                    ctrl._pregnancyInfo._currentMaximalPregnantDays = entry.MaxDays;
                    ctrl._pregnancyInfo._father_givenname = entry.FatherGivenName ?? "";
                    ctrl._pregnancyInfo._father_surname = entry.FatherSurName ?? "";
                    ctrl._pregnancyInfo._acceptance = entry.Acceptance;
                    ctrl._pregnancyInfo._broken = entry.Broken;
                    loaded++;
                }
                Log.LogInfo($"[SVSPregnancy] Loaded ({loaded}/{entries.Count} chars) from {Path.GetFileName(path)}");
            }
            catch (Exception e)
            {
                Log.LogError("[SVSPregnancy] Load failed: " + e.Message);
            }
        }

        private static void Save(string path)
        {
            try
            {
                var worldCtrl = PregnancyPlugin._worldController;
                if (worldCtrl == null) return;

                var entries = new List<PregEntry>();
                foreach (var ctrlPtr in worldCtrl._PregnancyCharaControllers)
                {
                    var ctrl = ctrlPtr.ToObject<PregnancyCharaController>();
                    if (ctrl?._pregnancyInfo == null) continue;

                    string name = "";
                    try { name = ctrl._chara.parameter.lastname + " " + ctrl._chara.parameter.firstname; }
                    catch { /* name stays empty */ }

                    entries.Add(new PregEntry
                    {
                        CharaId  = ctrl._charaId,
                        Name     = name,
                        Day      = ctrl._pregnancyInfo._day,
                        Cooldown = ctrl._pregnancyInfo._cooldown,
                        MaxDays  = ctrl._pregnancyInfo._currentMaximalPregnantDays,
                        FatherGivenName = ctrl._pregnancyInfo._father_givenname,
                        FatherSurName   = ctrl._pregnancyInfo._father_surname,
                        Acceptance = ctrl._pregnancyInfo._acceptance,
                        Broken     = ctrl._pregnancyInfo._broken
                    });
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
                Log.LogInfo($"[SVSPregnancy] Saved ({entries.Count} chars) to {Path.GetFileName(path)}");
            }
            catch (Exception e)
            {
                Log.LogError("[SVSPregnancy] Save failed: " + e.Message);
            }
        }

        private static string? DerivePath(string worldSavePath)
        {
            if (string.IsNullOrEmpty(worldSavePath)) return null;
            string? dir  = Path.GetDirectoryName(worldSavePath);
            string  name = Path.GetFileNameWithoutExtension(worldSavePath);
            return Path.Combine(dir ?? ".", name + "_pregnancy.json");
        }

        // ── Data model ────────────────────────────────────────────────────

        private class PregEntry
        {
            [JsonPropertyName("charaId")]         public int    CharaId         { get; set; }
            [JsonPropertyName("name")]            public string Name            { get; set; } = "";
            [JsonPropertyName("day")]             public int    Day             { get; set; }
            [JsonPropertyName("cooldown")]        public int    Cooldown        { get; set; }
            [JsonPropertyName("maxDays")]         public int    MaxDays         { get; set; }
            [JsonPropertyName("fatherGivenName")] public string FatherGivenName { get; set; } = "";
            [JsonPropertyName("fatherSurName")]   public string FatherSurName   { get; set; } = "";
            [JsonPropertyName("acceptance")]      public bool   Acceptance      { get; set; }
            [JsonPropertyName("broken")]          public bool   Broken          { get; set; }
        }
    }
}

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ILLGames.Unity.Component;
using Manager;
using SV.H;
using System;
//using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Il2CppInterop.Common.Attributes;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
//using Il2CppSystem;
//using Il2CppSystem.Collections.Generic;
//using Il2CppSystem.Reflection;
using Il2CppSystem.Runtime.CompilerServices;
using Il2CppSystem.Threading;
using ILLGames.Unity;
using System.Threading.Tasks;
using ADV;
using SaveData;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using ADV.Commands.Object;
using CharacterCreation.UI.View;
using SV.H.Motion.State;
using ILLGames.Extensions;
using UnityEngine.PlayerLoop;
using Il2CppInterop.Runtime.Injection;
using Character;
using ADV.Commands.Base;
using ILLGames.Component.UI.ToolTip;
using static SV.SimulationButtonAction;
using SV;
using Cysharp.Threading.Tasks;
using static SV.SimulationScene;
using SV.Talk;
using UnityEngine.Networking;

namespace SVSPregnancy
{

    [BepInPlugin(Constants.Prefix+PluginGuid, Constants.Prefix + " " + PluginName, PluginVersion)]
    public partial class PregnancyPlugin : BasePlugin
    {

        internal static Harmony _hi;
        internal static PregnancyPlugin _instance;

        public const string PluginGuid = ".SVSPregnancy";

        public const string PluginName = "SVSPregnancy";

        public const string Transplanter = "ジェンタイマン";
        public const string PluginVersion = "0.2.7";
        public static PregnancyWorldController _worldController;

        public static PregnancyAssetController _assetController;                    
        internal static PregnancyGameController? _gameController { get; set; } = null;


        public static ConfigEntry<bool> ConfigEnable { get; private set; }

        public static ConfigEntry<bool> ConfigLog { get; private set; }

        public static ConfigEntry<KeyCode> DebugUIKey { get; private set; }
        public static ConfigEntry<bool> FutanariCanInseminate { get; private set; }
        public static ConfigEntry<int> OvulationRateSafe { get; private set; }
        public static ConfigEntry<int> OvulationRateNormal { get; private set; }
        public static ConfigEntry<int> OvulationRateDanger { get; private set; }
        public static ConfigEntry<int> PregnancyProgressionSpeed { get; private set; }

        public static ConfigEntry<WpCutInMode_JP> CutInFertilize_JP { get; private set; }

        public static ConfigEntry<int> CutInFertilize_X { get; private set; }

        public static ConfigEntry<int> CutInFertilize_Y { get; private set; }


        public static ConfigEntry<int> CutInFertilize_Z { get; private set; }


        public static ConfigEntry<float> CutInFertilize_Wait { get; private set; }


        public static ConfigEntry<float> CutInFertilize_Loop { get; private set; }

        public static ConfigEntry<int> CutInFertilize_FPS { get; private set; }
        public static ConfigEntry<float> CutInFertilize_Ratio { get; private set; }
        // public static ConfigEntry<string> CutInFertilize_SoundPath { get; private set; }
#if SVS
        public static string CutInFertilize_SoundPath = "SVS_Pregnancy.Resources.SE.Fertilize.wav";
        //public static ConfigEntry<string> TextureFolder { get; private set; }
        public static string TextureFolder= "SVS_Pregnancy.Resources.CutIn.";
#endif
        public static ConfigEntry<DefaultTextureFormat> TextureFormat { get; private set; }

        


        public enum DefaultTextureFormat
        {

            dxt5,

            bc7
        }
        public enum WpCutInMode_JP
        {

            なし,

            右から左,

            左から右,

            上から下,

            下から上,

            奥から前,

            前から奥
        }

        public static PregnancyAssetController.Wipe.CutInMode CutInFertilize
        {
            get
            {

                switch (CutInFertilize_JP.Value)
                {
                    case WpCutInMode_JP.なし:
                        return PregnancyAssetController.Wipe.CutInMode.None;
                    case WpCutInMode_JP.右から左:
                        return PregnancyAssetController.Wipe.CutInMode.Right2Left;
                    case WpCutInMode_JP.左から右:
                        return PregnancyAssetController.Wipe.CutInMode.Left2Right;
                    case WpCutInMode_JP.上から下:
                        return PregnancyAssetController.Wipe.CutInMode.Top2Down;
                    case WpCutInMode_JP.下から上:
                        return PregnancyAssetController.Wipe.CutInMode.Down2Top;
                    case WpCutInMode_JP.奥から前:
                        return PregnancyAssetController.Wipe.CutInMode.Back2Front;
                    case WpCutInMode_JP.前から奥:
                        return PregnancyAssetController.Wipe.CutInMode.Front2Back;
                    default:
                        return PregnancyAssetController.Wipe.CutInMode.None;
                }
            }
        }
        public override void Load()
        {
            PregnancyPlugin._instance = this;

            if (_hi == null)
            {
                _hi = new Harmony(PluginGuid);
            }

            #region Register new classes
            


            Log.LogMessage("SVSPregnancy:Registering C# Types in Il2Cpp");
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<PregnancyAssetController>();
                ClassInjector.RegisterTypeInIl2Cpp<PregnancyHumanController>();
                ClassInjector.RegisterTypeInIl2Cpp<PregnancyCharaController>();
                ClassInjector.RegisterTypeInIl2Cpp<PregnancyWorldController>();
                ClassInjector.RegisterTypeInIl2Cpp<PregnancyGameController>();
                ClassInjector.RegisterTypeInIl2Cpp<PregnancyActorController>();
                ClassInjector.RegisterTypeInIl2Cpp<CustomGameController>();
                ClassInjector.RegisterTypeInIl2Cpp<CustomActorController>();
                ClassInjector.RegisterTypeInIl2Cpp<PregnancyDebugUI>();



            }
            catch
            {
                Log.LogError("SVSPregnancy:FAILED to Register Il2Cpp Type!");
            }


            #endregion

            #region Config  

            ConfigEnable = base.Config.Bind<bool>("General", "Enable", true, new ConfigDescription("", null, new object[]
            {
                new ConfigurationManagerAttributes
                            {
                                Order = new int?(100),
                                Browsable = new bool?(true)
                            }
            }));
            ConfigLog = base.Config.Bind<bool>("General", "Log Enable", true, new ConfigDescription("", null, new object[]
                  {
                      new ConfigurationManagerAttributes
                            {
                                Order = new int?(99),
                                Browsable = new bool?(true)
                            }
                  }));
            FutanariCanInseminate = base.Config.Bind<bool>("General", "Futanaris Can Inseminate", true, new ConfigDescription("フタナリたちも相手をできさせたことができる", null, new object[]
                  {
                      new ConfigurationManagerAttributes
                            {
                                Order = new int?(90),
                                Browsable = new bool?(true)
                            }
                  }));
            PregnancyProgressionSpeed = Config.Bind("General", "Pregnancy progression speed", 1,
                new ConfigDescription("How much faster does the in-game pregnancy progresses than the standard 40 weeks. " +
                                    "It also reduces the time characters leave school for after birth.\n\n" +
                                    "x1 is 40 weeks, x2 is 20 weeks, x4 is 10 weeks, etc.",
                                    new AcceptableValueList<int>(1, 2, 4, 7, 14, 30,60,100,365)));
            OvulationRateSafe = base.Config.Bind<int>("General", "Ovulation Rate in Safe Days", 0, new ConfigDescription("安全日での排卵率", new AcceptableValueRange<int>(0, 100), new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(91),
                                Browsable = new bool?(true)
                            }
            }));

            OvulationRateNormal = base.Config.Bind<int>("General", "Ovulation Rate in Normal Days", 5, new ConfigDescription("通常日での排卵率", new AcceptableValueRange<int>(0, 100), new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(92),
                                Browsable = new bool?(true)
                            }
            }));

            OvulationRateDanger = base.Config.Bind<int>("General", "Ovulation Rate in Dangerous Days", 50, new ConfigDescription("危険日での排卵率", new AcceptableValueRange<int>(0, 100), new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(93),
                                Browsable = new bool?(true)
                            }
            }));

            DebugUIKey = Config.Bind("Debug", "Debug UI Key",
                KeyCode.F8,
                new ConfigDescription("Key to toggle the pregnancy debug UI window.", null, new ConfigurationManagerAttributes { Order = 1, Browsable = true }));

            /*
            CutInFertilize_JP = base.Config.Bind<WpCutInMode_JP>("Cut-In", "受精カットインの方式", WpCutInMode_JP.奥から前, new ConfigDescription("", null, new object[]
                        {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(54)
                            }
                        }));
            CutInFertilize_X = base.Config.Bind<int>("Cut-In", "受精カットインのX位置(%)", -80, new ConfigDescription("", new AcceptableValueRange<int>(-100, 100), new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(53),
                                Browsable = new bool?(true)
                            }
            }));
            CutInFertilize_Y = base.Config.Bind<int>("Cut-In", "受精カットインのY位置(%)", -65, new ConfigDescription("", new AcceptableValueRange<int>(-100, 100), new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(52),
                                Browsable = new bool?(true)
                            }
            }));
            CutInFertilize_Z = base.Config.Bind<int>("Cut-In", "受精カットインの拡大率(%)", 25, new ConfigDescription("", new AcceptableValueRange<int>(1, 150), new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(51),
                                Browsable = new bool?(true)
                            }
            }));
            CutInFertilize_Ratio = base.Config.Bind<float>("Cut-In", "受精カットインの横縦比", 4f / 3f, new ConfigDescription("", new AcceptableValueRange<float>(0.1f, 10f), new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(50),
                                Browsable = new bool?(true)
                            }
            }));
            CutInFertilize_Wait = base.Config.Bind<float>("Cut-In", "受精カットインの待機秒数", 0.1f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 30f), new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(49),
                                Browsable = new bool?(true)
                            }
            }));

            CutInFertilize_Loop = base.Config.Bind<float>("Cut-In", "受精カットインの持続秒数", 7.9f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 30f), new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(48),
                                Browsable = new bool?(true)
                            }
            }));
            CutInFertilize_FPS = base.Config.Bind<int>("Cut-In", "受精カットインのフレームレート", 24, new ConfigDescription("", new AcceptableValueRange<int>(1, 60), new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(47),
                                Browsable = new bool?(true)
                            }
            }));

            /*
            CutInFertilize_SoundPath = base.Config.Bind<string>("Cut-In", "受精の效果音", "UserData\\soundeffect\\Fertilize.ogg", new ConfigDescription("受精するときの效果音", null, new object[]
            {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(46),
                                Browsable = new bool?(true)
                            }
            }));
           TextureFolder = base.Config.Bind<string>("Cut-In", "テクスチャフォルダ", "UserData\\Overlays\\LewdCrest", new ConfigDescription("テクスチャが保存されているフォルダを指定します。", null, new object[]
                        {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(70)
                            }
                        }));
            
            TextureFormat = base.Config.Bind<DefaultTextureFormat>("Cut-In", "Internal textures format", DefaultTextureFormat.dxt5, new ConfigDescription("Sets the textures format retained internally. bc7: Beautiful, dxt5: Memory saving", null, new object[]
                        {
                            new ConfigurationManagerAttributes
                            {
                                Order = new int?(77)
                            }
                        }));
            
         */


            #endregion
            #region Patch

            // MethodInfo oriInitialize = AccessTools.Method(typeof(Actor), "Initialize");
            // _hi.Patch(oriInitialize, null, new HarmonyMethod(Hooks.PostfixInitialize), null);


            // Load global belly deform settings (before any characters initialise)
            BellyDeformSettings.Load();

            _hi.PatchAll(typeof(Hooks));

            // Mesh-load spy: hook SkinnedMeshRenderer.set_sharedMesh globally
            TryPatchMeshSpy();

            // Patch save hooks individually so a missing method doesn't crash PatchAll
            TryPatchSaveHooks();

#if SAVE_FSW
            PregnancyPersistenceManager.SetupWatcher();
#endif

            #endregion
        }

        // ── Global SkinnedMeshRenderer.sharedMesh spy ─────────────────────────
        private static readonly BepInEx.Logging.ManualLogSource _meshSpyLog =
            BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.MeshSpy");

        private static string MeshSpyGetPath(Transform t)
        {
            if (t == null) return "(null)";
            var sb = new System.Text.StringBuilder();
            var parts = new System.Collections.Generic.List<string>();
            for (var c = t; c != null && parts.Count < 15; c = c.parent)
                parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void MeshSpyPostfix(SkinnedMeshRenderer __instance, Mesh value)
        {
            try
            {
                if (value == null) return;
                _meshSpyLog.LogInfo(
                    $"[MeshSpy] set_sharedMesh" +
                    $" smr=\"{__instance?.name}\"" +
                    $" mesh=\"{value.name}\"" +
                    $" verts={value.vertexCount}" +
                    $" readable={value.isReadable}" +
                    $" active={__instance?.gameObject?.activeInHierarchy}" +
                    $" path={MeshSpyGetPath(__instance?.transform)}");
            }
            catch { }
        }

        private void TryPatchMeshSpy()
        {
            try
            {
                var setter = AccessTools.Method(typeof(SkinnedMeshRenderer), "set_sharedMesh");
                if (setter != null)
                {
                    _hi.Patch(setter, postfix: new HarmonyMethod(typeof(PregnancyPlugin), nameof(MeshSpyPostfix)));
                    Log.LogInfo("[SVSPregnancy] MeshSpy: patched SkinnedMeshRenderer.set_sharedMesh");
                }
                else
                {
                    Log.LogWarning("[SVSPregnancy] MeshSpy: set_sharedMesh not found");
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[SVSPregnancy] MeshSpy patch failed: " + e.Message);
            }
        }

        private void TryPatchSaveHooks()
        {
            // SimulationScene._Start_b__ lambda — name changes across game versions, search at runtime
            try
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
                var simStartMethod = typeof(SimulationScene)
                    .GetMethods(flags)
                    .FirstOrDefault(m => m.Name.StartsWith("_Start_b__"));
                if (simStartMethod != null)
                {
                    _hi.Patch(simStartMethod, prefix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.SimulationScene_Start_Hook)));
                    Log.LogInfo($"[SVSPregnancy] Patched SimulationScene.{simStartMethod.Name}");
                }
                else
                    Log.LogWarning("[SVSPregnancy] SimulationScene._Start_b__ not found, skipping");
            }
            catch (Exception e)
            {
                Log.LogWarning("[SVSPregnancy] SimulationScene hook skipped: " + e.Message);
            }

#if SAVE_HOOK
            var saveWithPath = AccessTools.Method(typeof(Manager.Game), "Save", new Type[] { typeof(string) });
            if (saveWithPath != null)
            {
                _hi.Patch(saveWithPath, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.ManagerGameSaveWithPathHook)));
                Log.LogInfo("[SVSPregnancy] Patched Game.Save(string)");
            }
            else
                Log.LogWarning("[SVSPregnancy] Game.Save(string) not found, save-to-slot hook skipped");

            var save = AccessTools.Method(typeof(Manager.Game), "Save", new Type[] { });
            if (save != null)
            {
                _hi.Patch(save, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.ManagerGameSaveHook)));
                Log.LogInfo("[SVSPregnancy] Patched Game.Save()");
            }
            else
                Log.LogWarning("[SVSPregnancy] Game.Save() not found, save hook skipped");

            var autoSave = AccessTools.Method(typeof(Manager.Game), "AutoSave", new Type[] { });
            if (autoSave != null)
            {
                _hi.Patch(autoSave, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.ManagerGameAutoSaveHook)));
                Log.LogInfo("[SVSPregnancy] Patched Game.AutoSave()");
            }
            else
                Log.LogWarning("[SVSPregnancy] Game.AutoSave() not found, autosave hook skipped");
#endif // SAVE_HOOK

#if SAVE_HOOK
            // ── WorldData.Save hooks (confirmed working via spy run) ──────────────
            // WorldData.Save(string fileName)  — single-arg overload
            var wdSave1 = AccessTools.Method(typeof(SaveData.WorldData), "Save", new Type[] { typeof(string) });
            if (wdSave1 != null)
            {
                _hi.Patch(wdSave1, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.WorldDataSave1Hook)));
                Log.LogInfo("[SVSPregnancy] Patched WorldData.Save(string)");
            }
            else Log.LogWarning("[SVSPregnancy] WorldData.Save(string) not found");

            // WorldData.Save(string path, string fileName)  — two-arg overload
            var wdSave2 = AccessTools.Method(typeof(SaveData.WorldData), "Save", new Type[] { typeof(string), typeof(string) });
            if (wdSave2 != null)
            {
                _hi.Patch(wdSave2, postfix: new HarmonyMethod(typeof(Hooks), nameof(Hooks.WorldDataSave2Hook)));
                Log.LogInfo("[SVSPregnancy] Patched WorldData.Save(string, string)");
            }
            else Log.LogWarning("[SVSPregnancy] WorldData.Save(string,string) not found");
#endif // SAVE_HOOK
        }


      





        





    }

    
    public static class Hooks
    {


        


        readonly internal static MethodInfo PostfixInitialize = AccessTools.Method(typeof(Hooks), "InitializeHook");
         [HarmonyPostfix, HarmonyPatch(typeof(HScene), "Initialize", new System.Type[] { typeof(Parameter), typeof(Il2CppReferenceArray<Actor>) })]
        public static void HSceneInitializeHook()
        {
            var hscene = Singleton<HScene>.Instance;
            if (hscene != null && hscene.Actors.Count > 0)
            {
                if (PregnancyPlugin._worldController != null && PregnancyPlugin._worldController._inited)
                {
                    PregnancyPlugin._worldController.UpdateCharas();
                }

                // ── Clear stale lo-poly SMR cache ───────────────────────────
                // When entering H-scene the lo-poly character is deactivated (not
                // destroyed), so the cached SMR pointer is still non-null and
                // FindBodySMR is never re-triggered.  ForgetAll() wipes the cache
                // so the next Apply() rescans the hi-poly human's gameObject.
                BellyVertexMorph.ForgetAll();

                PregnancyPlugin._gameController = hscene.transform.gameObject.AddComponent(Il2CppType.Of<PregnancyGameController>()).Cast<PregnancyGameController>();
                PregnancyPlugin._gameController.Init();

                // ── Ensure PHC is initialised on every H-scene actor ─────────
                // LoadHair may have run before HScene.Initialize (lo-poly timing),
                // so we force a re-init here with the definitive hi-poly Human ptr.
                int actorCount = hscene.Actors.Count;
                for (int ai = 0; ai < actorCount; ai++)
                {
                    try
                    {
                        var human = hscene.Actors[ai]?.Human;
                        if (human == null) continue;
                        var go = human.gameObject;
                        if (go == null) continue;

                        UnityEngine.Component tempphc = null;
                        go.TryGetComponent(Il2CppType.Of<PregnancyHumanController>(), out tempphc);
                        PregnancyHumanController phc;
                        if (tempphc == null)
                            phc = new PregnancyHumanController(
                                go.AddComponent(Il2CppType.Of<PregnancyHumanController>()).Pointer)
                                .Cast<PregnancyHumanController>();
                        else
                            phc = tempphc.Cast<PregnancyHumanController>();

                        // Re-init with hi-poly Human so _charactrl lookup uses the correct Human
                        phc.Init(human.ToPtr());
                    }
                    catch (Exception phcEx)
                    {
                        PregnancyPlugin._instance?.Log.LogWarning(
                            $"[HSceneHook] PHC re-init for actor[{ai}] failed: {phcEx.Message}");
                    }
                }

                if (PregnancyAssetController.TextureLoader.FertilizeCutInTexturesCount==0)
                {
                    PregnancyAssetController.TextureLoader.RefreshTextures(false);
                }
                PregnancyAssetController.TextureLoader.PreloadAllTextures();

                if (PregnancyAssetController.CutInFertilize_Sound == null)
                {
                    PregnancyPlugin._assetController.LoadAudioClip(PregnancyPlugin.CutInFertilize_SoundPath);
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(WorldData), "Load",new Type[] { typeof(string)})]
        public static void WorldDataLoadHook1(string __0, WorldData __result)
        {
            GameObject gameObject = new GameObject("pregWorldCtrl");
            if (PregnancyPlugin._worldController != null)
            {
                PregnancyPlugin._worldController.DestroyMe();
            }
            PregnancyPlugin._worldController = gameObject.AddComponent(Il2CppType.Of<PregnancyWorldController>()).Cast<PregnancyWorldController>();
            PregnancyPlugin._worldController.Init(__result.ToPtr());
            gameObject.transform.SetParent(Manager.Game.Instance.gameObject.transform, false);
            PregnancyPersistenceManager.OnWorldLoad(__0);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(WorldData), "Load", new Type[] { typeof(string), typeof(string) })]
        public static void WorldDataLoadHook2(string __0, WorldData __result)
        {
            GameObject gameObject = new GameObject("pregWorldCtrl");
            if (PregnancyPlugin._worldController != null)
            {
                PregnancyPlugin._worldController.DestroyMe();
            }
            PregnancyPlugin._worldController = gameObject.AddComponent(Il2CppType.Of<PregnancyWorldController>()).Cast<PregnancyWorldController>();
            PregnancyPlugin._worldController.Init(__result.ToPtr());
            gameObject.transform.SetParent(Manager.Game.Instance.gameObject.transform, false);
            PregnancyPersistenceManager.OnWorldLoad(__0);
        }

        // Patched manually from PregnancyPlugin.TryPatchSaveHooks() — NOT via PatchAll
        public static bool SimulationScene_Start_Hook(SimulationScene __instance)
        {

            if (PregnancyPlugin._worldController == null)
            {
                GameObject gameObject = new GameObject("pregWorldCtrl");
                PregnancyPlugin._worldController = gameObject.AddComponent(Il2CppType.Of<PregnancyWorldController>()).Cast<PregnancyWorldController>();
                PregnancyPlugin._worldController.Init(Manager.Game.saveData.ToPtr());
                gameObject.transform.SetParent(Manager.Game.Instance.gameObject.transform, false);
            }

            if (PregnancyPlugin._assetController == null)
            {

                GameObject agameObject = new GameObject("pregAssetCtrl");
                agameObject.transform.SetParent(Manager.Game.Instance.gameObject.transform, false);
                PregnancyPlugin._assetController = agameObject.AddComponent(Il2CppType.Of<PregnancyAssetController>()).Cast<PregnancyAssetController>();
                PregnancyPlugin._assetController.Init();

            }

            if (PregnancyPlugin._worldController != null && PregnancyPlugin._worldController._inited)
            {
                PregnancyPlugin._worldController.UpdateCharas();
                // Load pregnancy data now that all PregnancyCharaControllers are ready
                PregnancyPersistenceManager.LoadCurrent();
            }

            // Create the debug UI singleton once and keep it alive across scene loads
            if (UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PregnancyDebugUI>()).Count == 0)
            {
                var debugUIGo = new GameObject("pregDebugUI");
                UnityEngine.Object.DontDestroyOnLoad(debugUIGo);
                debugUIGo.AddComponent(Il2CppType.Of<PregnancyDebugUI>());
            }

            return true;

        }

        [HarmonyPrefix, HarmonyPatch(typeof(SimulationButtonAction), "ShiftFromSimNightToRoomMorning")]
        public static bool ShiftFromSimNightToRoomMorningHook(SimulationButtonAction __instance)
        {
            if (PregnancyPlugin._worldController != null && PregnancyPlugin._worldController._inited)
            {
                PregnancyPlugin._worldController.EndTheDay();
                // Save BEFORE the game reloads WorldData for the new day,
                // otherwise in-memory pregnancy state is lost when the WorldController is recreated.
                PregnancyPersistenceManager.SaveBeforeDayEnd();
            }
            return true;
        }
        //[HarmonyPostfix, HarmonyPatch(typeof(HumanCloth), "SetClothesState", new System.Type[] { typeof(ChaFileDefine.ClothesKind), typeof(byte), typeof(bool) })]
        private static void ChaControl_SetClothesState(HumanCloth __instance, ref ChaFileDefine.ClothesKind clothesKind, ref byte state, ref bool next)
        {


        }

     //   [HarmonyPostfix, HarmonyPatch(typeof(HumanCloth), "SetClothesStateAll", new System.Type[] { typeof(ChaFileDefine.ClothesState) })]
        private static void ChaControl_SetClothesStateAll(HumanCloth __instance, ref ChaFileDefine.ClothesState state)
        {
            GameObject humanobj = __instance.human.gameObject;
            PregnancyHumanController humctrl;
            UnityEngine.Component? temphumctrl;
            humanobj.TryGetComponent(Il2CppType.Of<PregnancyHumanController>(), out temphumctrl);// x.GetComponent(Il2CppType.Of<KISActorController>()).Cast<KISActorController>();
            if (temphumctrl == null)
            {
                humctrl = humanobj.AddComponent(Il2CppType.Of<PregnancyHumanController>()).Cast<PregnancyHumanController>();
                humctrl.Init(__instance.human.ToPtr());
            }
            else
            {
                humctrl = temphumctrl.Cast<PregnancyHumanController>();
            }

            
            
            

        }
        [HarmonyPostfix, HarmonyPatch(typeof(Human), "LoadHair")]
        private static void LoadHair(Human __instance)
        {
            GameObject humanobj = __instance.gameObject;
            PregnancyHumanController humctrl;
            UnityEngine.Component? temphumctrl;
            humanobj.TryGetComponent(Il2CppType.Of<PregnancyHumanController>(), out temphumctrl);// x.GetComponent(Il2CppType.Of<KISActorController>()).Cast<KISActorController>();
            if (temphumctrl == null)
            {
                humctrl = humanobj.AddComponent(Il2CppType.Of<PregnancyHumanController>()).Cast<PregnancyHumanController>();
                humctrl.Init(__instance.ToPtr());
            }
            else
            {
                humctrl = temphumctrl.Cast<PregnancyHumanController>();
            }



        }

        //[HarmonyPostfix, HarmonyPatch(typeof(TalkTaskBase), "SetClothesStateAll", new System.Type[] { typeof(ChaFileDefine.ClothesState) ,typeof(bool)})]
        private static void ChaControl_SetClothesStateAll(TalkTaskBase __instance, ref ChaFileDefine.ClothesState state,ref bool isFemaleJudge)
        {

            "TalkTaskBase".ToLog();
            /* string dataid = __instance.human.data.About.dataID;
             GameObject humanobj = __instance.human.gameObject;



             PregnancyHumanController humctrl;
             UnityEngine.Component? temphumctrl;
             humanobj.TryGetComponent(Il2CppType.Of<PregnancyHumanController>(), out temphumctrl);// x.GetComponent(Il2CppType.Of<KISActorController>()).Cast<KISActorController>();
             if (temphumctrl == null)
             {
                 humctrl = new PregnancyHumanController(humanobj.AddComponent(Il2CppType.Of<PregnancyHumanController>()).Pointer).Cast<PregnancyHumanController>();
                 humctrl.Init(__instance.human.ToPtr());
             }
             else
             {
                 humctrl = temphumctrl.Cast<PregnancyHumanController>();
             }*/





        }

        //  [HarmonyPrefix, HarmonyPatch(typeof(SimulationButtonAction), "ShiftFromSimNightToRoomMorningADV")]
        public static void TestHook3(SimulationButtonAction __instance,  UniTask __result)
        {
            "ShiftFromSimNightToRoomMorningADV".ToLog();
        }

#if SAVE_HOOK
        // WorldData.Save(string fileName)
        public static void WorldDataSave1Hook(string __0)
        {
            try { PregnancyPersistenceManager.OnWorldSave(__0); }
            catch (Exception e) { PregnancyActorController.ShowText("[SVSPregnancy] WorldData.Save(1) hook error: " + e.Message); }
        }

        // WorldData.Save(string path, string fileName)
        public static void WorldDataSave2Hook(string __0, string __1)
        {
            // Combine path + fileName to get the full save path, matching WorldData.Load(string,string) signature
            try
            {
                string combined = string.IsNullOrEmpty(__0) ? __1 : System.IO.Path.Combine(__0, __1);
                PregnancyPersistenceManager.OnWorldSave(combined);
            }
            catch (Exception e) { PregnancyActorController.ShowText("[SVSPregnancy] WorldData.Save(2) hook error: " + e.Message); }
        }

        // Called manually from PregnancyPlugin.TryPatchSaveHooks() — NOT via PatchAll
        public static void ManagerGameSaveWithPathHook(string __0)
        {
            try { PregnancyPersistenceManager.OnWorldSave(__0); }
            catch (Exception e) { PregnancyActorController.ShowText("[SVSPregnancy] Save(string) hook error: " + e.Message); }
        }

        // Called manually from PregnancyPlugin.TryPatchSaveHooks() — NOT via PatchAll
        public static void ManagerGameSaveHook()
        {
            try { PregnancyPersistenceManager.SaveCurrent(); }
            catch (Exception e) { PregnancyActorController.ShowText("[SVSPregnancy] Save() hook error: " + e.Message); }
        }

        // Called manually from PregnancyPlugin.TryPatchSaveHooks() — NOT via PatchAll
        public static void ManagerGameAutoSaveHook()
        {
            try { PregnancyPersistenceManager.SaveCurrent(); }
            catch (Exception e) { PregnancyActorController.ShowText("[SVSPregnancy] AutoSave() hook error: " + e.Message); }
        }
#endif // SAVE_HOOK
    }

   
}

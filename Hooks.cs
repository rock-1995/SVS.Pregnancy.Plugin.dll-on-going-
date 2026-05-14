using System;
using System.Reflection;

using ADV;
using HarmonyLib;

using Manager;
using UnityEngine;
using RootMotion;
using System.Threading;

using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.IO;
using System.Runtime.InteropServices;
#if KKS
using SaveData;
#elif KK
using static SaveData;
#endif
namespace SVSPregnancy
{
    // Token: 0x02000002 RID: 2
    internal static class Hooks
    {
        // Token: 0x06000001 RID: 1 RVA: 0x00002050 File Offset: 0x00000250
        public static void InstallHooks()
        {
            try
            {
                if (!StudioAPI.InsideStudio)
                {
                    LewdCrest.Log("PatchAll\u3000Hooks EX5", false);

                    Harmony harmony = Harmony.CreateAndPatchAll(typeof(Hooks), null);
                  //  PatchNPCLoadAll(harmony);
                    Type type = AccessTools.FirstInner(typeof(TalkScene), (Type x) => x.FullName.Contains("TalkScene+<TalkEnd>"));
                    if (type == null)
                    {
                        LewdCrest.Log("�p�b�`��K�p����TalkEnd�C�e���[�^��������܂���ł����B", true);
                    }
                    else
                    {
                        MethodInfo methodInfo = AccessTools.Method(type, "MoveNext", null, null);
                        HarmonyMethod harmonyMethod = new HarmonyMethod(typeof(Hooks), "PreTalkSceneIteratorEndHook", null);
#if KK
                        harmony.Patch(methodInfo, harmonyMethod, null, null, null, null);
#elif KKS
                        harmony.PatchMoveNext(AccessTools.Method(typeof(TalkScene), "TalkEnd"), null, null, new HarmonyMethod(typeof(Hooks), "TranspilerMCP"), null);
#endif



                    }
#if KKS
                    type = AccessTools.FirstInner(typeof(ActionMap), (Type x) => (x.FullName.Contains("ActionMap+<ChangeAsync>")&&x.DeclaringType== typeof(ActionMap)));
                    if (type == null)
                    { LewdCrest.Log("�p�b�`��K�p����ChangeAsync�C�e���[�^��������܂���ł����B", true); }
                    else
                    {
                        MethodInfo methodInfo = AccessTools.Method(typeof(ActionMap), "ChangeAsync", new Type[] { typeof(int), typeof(FadeCanvas.Fade), typeof(bool) }, null);
                        HarmonyMethod harmonyMethod = new HarmonyMethod(typeof(Hooks), "MapChangePostfix", null);
                        // harmony.Patch(methodInfo, null, harmonyMethod, null, null, null);

                        harmony.PatchMoveNext(methodInfo, null, harmonyMethod, null, null);

                    }

                    type = AccessTools.FirstInner(typeof(Scene), (Type x) => x.FullName.Contains("Scene+<UnloadAsync>"));
                    if (type == null)
                    { LewdCrest.Log("�p�b�`��K�p����UnloadAsync�C�e���[�^��������܂���ł����B", true); }
                    else
                    {
                        MethodInfo methodInfo = AccessTools.Method(type, "MoveNext", null, null);
                        HarmonyMethod harmonyMethod = new HarmonyMethod(typeof(Hooks), "PostSceneUnloadHook", null);
                        // harmony.Patch(methodInfo, null, harmonyMethod, null, null, null);
                        harmony.PatchMoveNext(AccessTools.Method(typeof(Scene), "UnloadAsync"), null, harmonyMethod, null, null);
                    }
#endif


                }
            }
            catch (Exception ex)
            {
                LewdCrest.Log("InstallHooks=" + ex.ToString(), true);
            }
        }


        public static void PrintMethod(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions.ToList())
            {
                SaveLog(instruction.ToString());
            }
        }

        public static void SaveLog(String s)
        {
            String logpath = $"{System.IO.Path.GetTempPath()}{"KK_LC\\"}" + "debuglog.txt";
            var tmp = s;
            StreamWriter logfile;
            String path = System.IO.Path.GetDirectoryName(logpath);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }



            logfile = new(logpath, append: true);



            for (int i = 0; i < 3; ++i)
                try
                {
#if KKS
                    logfile.WriteLineAsync(tmp);
#elif KK
                    logfile.WriteLine(tmp);
#endif
                    logfile.Close();
                    logfile.Dispose();
                    break;
                }
                catch (IOException)
                {
#if KKS
                    System.Threading.Tasks.Task.Delay(500);
#endif

                }


        }

    

        private static void PatchNPCLoadAll(Harmony instance)
        {
            var transpiler = new HarmonyMethod(typeof(Hooks), nameof(NPCLoadAllTpl));
            //foreach (var target in typeof(ActionScene).GetMethods(AccessTools.all).Where(x => x.Name == nameof(ActionScene.NPCLoadAll)))
            {
                var target = typeof(ActionScene).GetMethods(AccessTools.all).Where(x => x.Name == nameof(ActionScene.NPCLoadAll)).First();
              //  instance.PatchMoveNext(target, null, null, transpiler);
            }
        }

        private static IEnumerable<CodeInstruction> NPCLoadAllTpl(IEnumerable<CodeInstruction> instructions)
        {
            PrintMethod(instructions);
            return instructions;
            
        }







#if KK
        [HarmonyPostfix]
		[HarmonyPatch(typeof(ActionMap), "Change", new Type[]
		{
			typeof(int),
			typeof(Scene.Data.FadeType)
		})]
        public static void MapChangePostfix(ActionMap __instance, int no, Scene.Data.FadeType fadeType)
		{
#elif KKS

        public static void MapChangePostfix()
        {
#endif
            if (StudioAPI.InsideStudio)
            {
                return;
            }
            if (MakerAPI.InsideMaker)
            {
                return;
            }
            Game instance = Singleton<Game>.Instance;
            ActionGame.Chara.Player player;
            if (instance == null)
            {
                player = null;
            }
            else
            {
                ActionScene actScene;
#if KK
                actScene = instance.actScene;
                player = ((actScene != null) ? actScene.Player : null);
#elif KKS
                actScene = SingletonInitializer<ActionScene>.instance;
                player = ((actScene != null) ? actScene.Player : null);

#endif

            }
            ActionGame.Chara.Player player2 = player;
            if (player2 == null)
            {
                return;
            }
            Hooks._mapNo = player2.mapNo;
            LewdCrest.Log("ActionMap.Change " + player2.mapNo, false);
        } 

        // Token: 0x06000003 RID: 3 RVA: 0x00002190 File Offset: 0x00000390
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HFlag), "FemaleGaugeUp")]
        public static void FemaleGaugeUp(HFlag __instance)
        {
            if (!LewdCrest.EnableLewdCrestH.Value)
            {
                return;
            }
            try
            {
                foreach (SaveData.Heroine heroine in __instance.lstHeroine)
                {
                    LewdCrestController controller = Utils.GetController(heroine);
                    if (!(controller == null))
                    {
                        controller.OnFemaleGaugeUp(heroine, __instance);
                    }
                }
            }
            catch (Exception ex)
            {
                LewdCrest.Log("FemaleGaugeUp=" + ex.ToString(), true);
            }
        }

        // Token: 0x06000004 RID: 4 RVA: 0x0000222C File Offset: 0x0000042C
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HFlag), "MaleGaugeUp")]
        public static void MaleGaugeUp(HFlag __instance)
        {
            if (!LewdCrest.EnableLewdCrestH.Value)
            {
                return;
            }
            try
            {
                SaveData.Player player = (__instance != null) ? __instance.player : null;
                if (player != null)
                {
                    LewdCrestController controller = Utils.GetController(player);
                    if (!(controller == null))
                    {
                        controller.OnMaleGaugeUp(player, __instance);
                    }
                }
            }
            catch (Exception ex)
            {
                LewdCrest.Log("MaleGaugeUp=" + ex.ToString(), true);
            }
        }

        // Token: 0x06000005 RID: 5 RVA: 0x000022A0 File Offset: 0x000004A0
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HFlag), "AddSonyuKokanPlay")]
        public static void AddSonyuKokanPlay(HFlag __instance)
        {
            LewdCrest.Log("AddSonyuKokanPlay", false);
            SaveData.Heroine heroine = (__instance != null) ? __instance.GetLeadHeroine() : null;
            if (heroine == null)
            {
                return;
            }
            LewdCrestController controller = Utils.GetController(heroine);
            if (controller == null)
            {
                return;
            }
            controller.OnInsert(heroine, __instance, false);
        }

        // Token: 0x06000006 RID: 6 RVA: 0x000022E4 File Offset: 0x000004E4
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HFlag), "AddSonyuAnalPlay")]
        public static void AddSonyuAnalPlay(HFlag __instance)
        {
            LewdCrest.Log("AddSonyuAnalPlay", false);
            SaveData.Heroine heroine = (__instance != null) ? __instance.GetLeadHeroine() : null;
            if (heroine == null)
            {
                return;
            }
            LewdCrestController controller = Utils.GetController(heroine);
            if (controller == null)
            {
                return;
            }
            controller.OnInsert(heroine, __instance, true);
        }

        // Token: 0x06000007 RID: 7 RVA: 0x00002327 File Offset: 0x00000527
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HFlag), "AddKuwaeFinish")]
        public static void AddKuwaeFinish(HFlag __instance)
        {
            LewdCrest.Log("AddKuwaeFinish", false);
        }

        // Token: 0x06000008 RID: 8 RVA: 0x00002334 File Offset: 0x00000534
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HFlag), "AddSonyuInside")]
        public static void AddSonyuInside(HFlag __instance)
        {
            LewdCrest.Log("AddSonyuInside", false);
            SaveData.Heroine heroine = (__instance != null) ? __instance.GetLeadHeroine() : null;
            if (heroine == null)
            {
                return;
            }
            LewdCrestController controller = Utils.GetController(heroine);
            if (controller == null)
            {
                return;
            }
            controller.OnFinishRawInside(heroine, __instance, false);
        }

        // Token: 0x06000009 RID: 9 RVA: 0x00002378 File Offset: 0x00000578
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HFlag), "AddSonyuAnalInside")]
        public static void AddSonyuAnalInside(HFlag __instance)
        {
            LewdCrest.Log("AddSonyuAnalInside", false);
            SaveData.Heroine heroine = (__instance != null) ? __instance.GetLeadHeroine() : null;
            if (heroine == null)
            {
                return;
            }
            LewdCrestController controller = Utils.GetController(heroine);
            if (controller == null)
            {
                return;
            }
            controller.OnFinishRawInside(heroine, __instance, true);
        }

        // Token: 0x0600000A RID: 10 RVA: 0x000023BC File Offset: 0x000005BC
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HFlag), "AddSonyuTare")]
        public static void AddSonyuTare(HFlag __instance)
        {
            LewdCrest.Log("AddSonyuTare", false);
            SaveData.Heroine heroine = (__instance != null) ? __instance.GetLeadHeroine() : null;
            if (heroine == null)
            {
                return;
            }
            LewdCrestController controller = Utils.GetController(heroine);
            if (controller == null)
            {
                return;
            }
            controller.OnPullOut(heroine, __instance, false);
        }

        // Token: 0x0600000B RID: 11 RVA: 0x00002400 File Offset: 0x00000600
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HFlag), "AddSonyuAnalTare")]
        public static void AddSonyuAnalTare(HFlag __instance)
        {
            LewdCrest.Log("AddSonyuAnalTare", false);
            SaveData.Heroine heroine = (__instance != null) ? __instance.GetLeadHeroine() : null;
            if (heroine == null)
            {
                return;
            }
            LewdCrestController controller = Utils.GetController(heroine);
            if (controller == null)
            {
                return;
            }
            controller.OnPullOut(heroine, __instance, true);
        }

#if KKS
        private static IEnumerable<CodeInstruction> TranspilerMCP(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            Type tempType = AccessTools.FirstInner(typeof(TalkScene), (Type x) => x.FullName.Contains("TalkScene+<TalkEnd>"));
            MethodInfo tempMethodInfo = AccessTools.Method(tempType, "MoveNext", null, null);
            var lv = tempMethodInfo.GetMethodBody().LocalVariables;
            var l = lv.First(x => x.LocalType == typeof(TalkScene));


            instructions = new CodeMatcher(instructions).SearchForward(
                x=>true)
                .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldloc, l.LocalIndex),
                new CodeInstruction(OpCodes.Call, infoMCP)
                )
                .InstructionEnumeration();
            return instructions;

        }
        
#endif


        internal static MethodInfo infoMCP = AccessTools.Method(typeof(Hooks), "PreTalkSceneIteratorEndHook");
        // Token: 0x0600000C RID: 12 RVA: 0x00002444 File Offset: 0x00000644
#if KK
        public static void PreTalkSceneIteratorEndHook(TalkScene __instance)
#elif KKS
        public static void PreTalkSceneIteratorEndHook(TalkScene __instance)
        
#endif
        {
            LewdCrest.Log("PreTalkSceneIteratorEndHook", false);
#if KK
            Traverse traverse = Traverse.Create(__instance);
#elif KKS
            Traverse traverse = Traverse.Create(__instance);
#endif
            int? num;
            if (traverse == null)
            {
                num = null;
            }
            else
            {
                Traverse traverse2 = traverse.Field("$PC");
                num = ((traverse2 != null) ? new int?(traverse2.GetValue<int>()) : null);
            }
            int? num2 = num;
            int num3 = 2;
            if (num2.GetValueOrDefault() == num3 & num2 != null)
            {
                SaveData.Heroine currentVisibleGirl = Utils.GetCurrentVisibleGirl();
                LewdCrestController controller = Utils.GetController(currentVisibleGirl);
                if (controller != null && currentVisibleGirl != null)
                {
                    LewdCrestGameController.SavePersistData(currentVisibleGirl, controller);
                }
            }
        }

        // Token: 0x0600000D RID: 13 RVA: 0x000024C8 File Offset: 0x000006C8
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TextScenario), "Release")]
        public static void PreTextScenarioReleaseHook()
        {
            SaveData.Heroine currentVisibleGirl = Utils.GetCurrentVisibleGirl();
            LewdCrestController controller = Utils.GetController(currentVisibleGirl);
            if (controller != null&& currentVisibleGirl!=null)
            {
                LewdCrestGameController.SavePersistData(currentVisibleGirl, controller);
            }
        }
#if KK
        // Token: 0x0600000E RID: 14 RVA: 0x000024F4 File Offset: 0x000006F4
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Scene), "UnLoad", new Type[]
        {

        })]
        public static void PostSceneUnloadHook()
        {
#elif KKS
        // Token: 0x0600000E RID: 14 RVA: 0x000024F4 File Offset: 0x000006F4
        public static void PostSceneUnloadHook()
        {
#endif
            try
            {
                LewdCrest.Log("PostSceneUnloadHook", false);
                SaveData.Heroine currentVisibleGirl = Utils.GetCurrentVisibleGirl();
                if (currentVisibleGirl != null)
                {
                    LewdCrestController controller = Utils.GetController(currentVisibleGirl);
                    if (controller != null)
                    {
                        LewdCrestGameController gameController = Utils.GetGameController();
                        if (gameController != null)
                        {
                            gameController.OnSceneUnload(currentVisibleGirl, controller);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LewdCrest.Log("PostSceneUnloadHook=" + ex.ToString(), true);
            }
        }

        // Token: 0x0600000F RID: 15 RVA: 0x00002564 File Offset: 0x00000764
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneProc), "ShortCut")]
        private static void OnShortCut()
        {
            if (LewdCrest.ForceLewdCrest.Value.IsDown())
            {
                LewdCrest.Log("ForceLewdCrest", false);
                HFlag hflag = UnityEngine.Object.FindObjectOfType<HFlag>();
                if (hflag != null)
                {
                    foreach (SaveData.Heroine heroine in hflag.lstHeroine)
                    {
                        LewdCrestController controller = Utils.GetController(heroine);
                        if (!(controller == null))
                        {
                            controller.ForceInmon = true;
                            controller.SetInmonLevel(heroine, 0, false);
                        }
                    }
                    Illusion.Game.Utils.Sound.Play(SystemSE.result_gauge);
                    return;
                }
            }
            else if (LewdCrest.RestBelly.Value.IsDown())
            {
                LewdCrest.Log("RestBelly", false);
                HFlag hflag2 = UnityEngine.Object.FindObjectOfType<HFlag>();
                if (hflag2 != null)
                {
                    foreach (SaveData.Heroine heroine2 in hflag2.lstHeroine)
                    {
                        LewdCrestController controller2 = Utils.GetController(heroine2);
                        if (controller2 != null)
                        {
                            controller2.ResetSemenVolume(true);
                        }
                    }
                    Illusion.Game.Utils.Sound.Play(SystemSE.ok_l);
                    return;
                }
#if KK
                foreach (SaveData.Heroine heroine3 in Singleton<Game>.Instance.HeroineList)
#elif KKS
                foreach (SaveData.Heroine heroine3 in Game.saveData.heroineList)
#endif
                {
                    LewdCrestController controller3 = Utils.GetController(heroine3);
                    if (controller3 != null)
                    {
                        controller3.ResetSemenVolume(true);
                    }
                }
                Illusion.Game.Utils.Sound.Play(SystemSE.ok_l);
            }
        }

        // Token: 0x06000010 RID: 16 RVA: 0x000026F8 File Offset: 0x000008F8
        private static void RemoveAllTextures(HFlag hFlag)
        {
            Utils.GetController(hFlag.player).RemoveAllTextures();
            foreach (SaveData.Heroine heroine in hFlag.lstHeroine)
            {
                Utils.GetController(heroine).RemoveAllTextures();
            }
        }

        // Token: 0x06000011 RID: 17 RVA: 0x00002760 File Offset: 0x00000960
        private static void UpdateAllTextures(HFlag hFlag)
        {
            Utils.GetController(hFlag.player).SetInmonLevelMale(hFlag.player, 0);
            foreach (SaveData.Heroine heroine in hFlag.lstHeroine)
            {
                Utils.GetController(heroine).SetInmonLevel(heroine, 0, false);
            }
        } 

        // Token: 0x04000001 RID: 1
        private static int _mapNo;
    }
}

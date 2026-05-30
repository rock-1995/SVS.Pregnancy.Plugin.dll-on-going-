using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ILLGames.Unity.Component;
using Manager;
using SV.H;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using CharacterCreation.UI.View.Body.HandNail;
using SV.H.UI;
using ADV.Commands.Object;

namespace SVSPregnancy
{
    internal class CustomGameController : MonoBehaviour
    {
        public  CustomGameController _instance { get; protected set; }

        public List<Player> _playerList { get; set; } = new List<Player>();
        public  HScene? _hScene =>Singleton<HScene>.Instance;

        public bool _inited = false;

        public int _count = 0;
        public CustomGameController(IntPtr ptr) : base(ptr)
        {
            _instance = this;
           // _hScene = hscene;



        }
       
        public void Test()
        {
            
            
            //Util.SaveLog($"Activing");

        }

       
        private static readonly BepInEx.Logging.ManualLogSource _cgcLog =
            BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.CGC");

        public virtual void Init()
        {
            _inited = false;
            _cgcLog.LogInfo("[CGC] Init() entered");
            int num = _hScene?.Actors?.Count ?? 0;
            _cgcLog.LogInfo($"[CGC] actor count = {num}");
            this._playerList = new List<Player>();
            for (int i = 0; i < num; i++)
            {
                try
                {
                    var actor = _hScene.Actors[i];
                    var x    = actor?.Human?.gameObject;
                    if (actor == null || x == null)
                    {
                        _cgcLog.LogWarning($"[CGC] actor[{i}] or Human.gameObject is null, skipping");
                        continue;
                    }

                    PregnancyActorController ctrlx;
                    UnityEngine.Component? tempctrlx;
                    x.TryGetComponent(Il2CppType.Of<PregnancyActorController>(), out tempctrlx);
                    if (tempctrlx == null)
                    {
                        ctrlx = new PregnancyActorController(
                            x.AddComponent(Il2CppType.Of<PregnancyActorController>()).Pointer)
                            .Cast<PregnancyActorController>();
                        _cgcLog.LogInfo($"[CGC] actor[{i}]: added fresh PAC");
                    }
                    else
                    {
                        ctrlx = tempctrlx.Cast<PregnancyActorController>();
                        _cgcLog.LogInfo($"[CGC] actor[{i}]: reused existing PAC");
                    }

                    // Get the native actor pointer — WordPlayer.Actor may be null for some actors
                    IntPtr actorPtr = IntPtr.Zero;
                    try
                    {
                        var wp = actor.WordPlayer;
                        if (wp?.Actor != null)
                            actorPtr = wp.Actor.ToPtr();
                        else
                            _cgcLog.LogWarning($"[CGC] actor[{i}]: WordPlayer or Actor is null, using IntPtr.Zero");
                    }
                    catch (Exception wpEx)
                    {
                        _cgcLog.LogWarning($"[CGC] actor[{i}]: WordPlayer.Actor.ToPtr() threw: {wpEx.Message}");
                    }

                    _playerList.Add(new Player(i, actorPtr, ctrlx));
                    _count++;
                    _cgcLog.LogInfo($"[CGC] actor[{i}]: Player created OK (actorPtr=0x{actorPtr:X})");
                }
                catch (Exception ex)
                {
                    _cgcLog.LogError($"[CGC] actor[{i}]: EXCEPTION: {ex}");
                }
            }
            if (_playerList.Count != num)
            {
                _cgcLog.LogWarning($"[CGC] _playerList.Count={_playerList.Count} != num={num}");
                Util.SaveLog($"Something was wrong in creating _playerList");
            }
            _inited = true;
            _cgcLog.LogInfo("[CGC] Init() finished OK");
        }


      
        protected virtual void Update()
        {

            
        }
    }

}
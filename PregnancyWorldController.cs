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

namespace SVSPregnancy
{

    public  class PregnancyWorldController : MonoBehaviour
    {

        public static new PregnancyWorldController _instance { get; protected set; }
        public  List<IntPtr> _PregnancyCharaControllers = new List<IntPtr>();
        public IntPtr _worldPtr = IntPtr.Zero;
        public bool _inited = false;
        public WorldData _world=> _worldPtr.ToObject<WorldData>();
        public static Il2CppSystem.Collections.Generic.Dictionary<int, Actor> _Charas => Manager.Game.Charas;
        public PregnancyWorldController(IntPtr ptr) : base(ptr)
        {
            _instance = this;

        }

        public void Init(IntPtr ptrWorld)
        {
            _worldPtr = ptrWorld;

            UpdateCharas();

            _inited = true;

            
        }

        public IntPtr FindPregnancyCharaControllerPtr(int charaId)
        {
            // Search by charaId (Actor.charasGameParam.Index) directly — avoids
            // pointer comparison against Manager.Game.Charas which may lag behind
            // the freshly-loaded WorldData at hook time.
            foreach (var ptr in _PregnancyCharaControllers)
            {
                var ctrl = ptr.ToObject<PregnancyCharaController>();
                if (ctrl != null && ctrl._charaId == charaId)
                    return ptr;
            }
            return IntPtr.Zero;
        }

        public void EndTheDay()
        {
            UpdateCharas();
            foreach (var chara in _world.Charas.Values)
            {
                if (this._PregnancyCharaControllers.Exists(x => x.ToObject<PregnancyCharaController>()._charaPtr == chara.ToPtr()))
                {

                    PregnancyCharaController actrl = this._PregnancyCharaControllers.First(x => x.ToObject<PregnancyCharaController>()._charaPtr == chara.ToPtr()).ToObject<PregnancyCharaController>();
                    actrl.DayPlus();
                }
            }
        }

        public void UpdateCharas()
        {
            List<IntPtr> _PregnancyCharaControllersToRemove = new List<IntPtr>();
            foreach (var acptr in _PregnancyCharaControllers)
            {
                PregnancyCharaController ac= acptr.ToObject<PregnancyCharaController>();
                if (ac == null || ac._chara == null || !_world.Charas.ToDict().Values.ToList().Exists(x => x.ToPtr() == ac._charaPtr))
                {
                    _PregnancyCharaControllersToRemove.Add(acptr);
                    
                }
            }
            foreach (var acptr in _PregnancyCharaControllersToRemove)
            {
                _PregnancyCharaControllers.Remove(acptr);
                PregnancyCharaController ac = acptr.ToObject<PregnancyCharaController>();
                ac.DestroyMe();
            }
            foreach (var chara in _world.Charas.Values)
            {
                if (!this._PregnancyCharaControllers.Exists(x => x.ToObject<PregnancyCharaController>()._charaPtr == chara.ToPtr()))
                {
                    GameObject gameObject = new GameObject("pregctrl" + " " + chara.charasGameParam.Index);
                    
                    var newctr = new PregnancyCharaController(gameObject.AddComponent(Il2CppType.Of<PregnancyCharaController>()).Pointer).Cast<PregnancyCharaController>();
                    newctr.Init(chara.ToPtr());
                    this._PregnancyCharaControllers.Add(newctr.ToPtr());
                    gameObject.transform.SetParent(this.gameObject.transform, false);
                  //  gameObject.tag = "pregActorctrl";
                }
            }

        }
        void Update()
        {
#if SAVE_FSW
            PregnancyPersistenceManager.FlushPendingSave();
#endif
        }

        public void DestroyMe()
        {
            _inited = false;
            foreach (var controllerPtr in _PregnancyCharaControllers)
            {
                var controller = controllerPtr.ToObject<PregnancyCharaController>();
                controller.DestroyMe();
            }
            UnityEngine.Object.Destroy(this.gameObject);
        }

    }


}

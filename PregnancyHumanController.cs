using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ILLGames.Unity.Component;
using Manager;
using SV.H;
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using UnityEngine;
using Il2CppInterop.Common.Attributes;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
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
using static ChaFileDefine;
using Il2CppSystem.Security.Cryptography;
using ADV.Commands.Base;
using System.ComponentModel;
using ILLGames.Unity.Animations;
using SV;
using static ADV.Commands.Chara.KaraokePlay;
using static SV.H.HSECtrl;
using static Network.NetworkAPIInfo.MetaInfo;
using Il2CppSystem.ComponentModel;
using static Character.Human;
using static SaveData.SensitivityParameter;
using MessagePack.Formatters.SaveData;
using static SaveData.StateParameter;
using static SaveData.Extension.ActorExtensionH;
using static Il2CppSystem.Linq.Expressions.Interpreter.CastInstruction.CastInstructionNoT;
using static Il2CppSystem.Globalization.HebrewNumber;
using Character;

namespace SVSPregnancy
{
    public partial class PregnancyHumanController : MonoBehaviour
    {
        public bool _inited = false;
        public new PregnancyHumanController _instance { get; protected set; }
        public int _charaId = -1;
        public static Il2CppSystem.Collections.Generic.Dictionary<int, Actor> _Charas => Manager.Game.Charas;
        public IntPtr _humanPtr = IntPtr.Zero;
        public Character.Human _human => _humanPtr.ToObject<Character.Human>();

        public IntPtr _charactrlPtr = IntPtr.Zero;

        public PregnancyCharaController _charactrl => _charactrlPtr.ToObject<PregnancyCharaController>();
        protected readonly object syncLock = new object();

        protected volatile bool created = false;

        protected bool Enable { get; set; }

        public PregnancyHumanController(IntPtr ptr) : base(ptr)
        {
            _instance = this;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        protected void Awake()
        {
            this.created = true;
        }

        protected string GetCharFilerDataId()
        {
            return _human.data.About.dataID;
        }

        protected int GetCharaId()
        {
            var charas = _Charas.ToDict();

            if (charas.Any(pair => pair.Value.charFile.About.dataID == this.GetCharFilerDataId()))
            {
                return charas.FirstOrDefault(pair => pair.Value.charFile.About.dataID == this.GetCharFilerDataId()).Key;
            }
            else
            {
                return -1;
            }
        }

        public void Init(IntPtr humanPtr)
        {
            _humanPtr = humanPtr;
            _charaId = this.GetCharaId();
            if (_charaId < 0)
            {
                $"Failed retrive Character{_charaId}'s charaId.".ToLog();
            }
            var worldctrl = PregnancyPlugin._worldController;
            if (worldctrl != null)
            {
                this._charactrlPtr = worldctrl.FindPregnancyCharaControllerPtr(_charaId);
            }
            _inited = true;
        }

        protected void Start()
        {
            Il2CppSystem.Threading.Thread.Sleep(100);
            lock (syncLock)
            {
                this.Enable = false;
                Il2CppSystem.Threading.Thread.MemoryBarrier();
            }
            this.Enable = true;
        }

        /// <summary>Undo any vertex deformation and restore the mesh to its rest pose.</summary>
        public void ResetBones()
        {
            BellyVertexMorph.Reset(_charaId);
        }

        private static BepInEx.Logging.ManualLogSource _modLog =
            BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.PHC");
        private float _lastLoggedRate = float.NaN;
        private bool  _loggedNullCtrl = false;

        public void ModifyBelly()
        {
            if (_charactrl == null)
            {
                if (!_loggedNullCtrl)
                {
                    _modLog.LogWarning($"[PHC] ModifyBelly id={_charaId}: _charactrl is null");
                    _loggedNullCtrl = true;
                }
                BellyVertexMorph.Reset(_charaId);
                return;
            }
            _loggedNullCtrl = false;

            if (!_charactrl.IsPregnant())
            {
                BellyVertexMorph.Reset(_charaId);
                return;
            }

            int   day      = _charactrl._pregnancyInfo._day;
            int   startDay = BellyDeformSettings.StartDay;
            int   maxDays  = _charactrl._pregnancyInfo._currentMaximalPregnantDays;

            // day <= startDay means belly has not yet begun — undo any deformation and exit.
            // This avoids calling Apply(rate=0) which would rewrite the mesh + RecalculateNormals
            // even though no vertex moved, causing a visible shading "snap" at startDay.
            if (maxDays <= startDay || day <= startDay)
            {
                BellyVertexMorph.Reset(_charaId);
                return;
            }

            float t    = Mathf.Clamp01((float)(day - startDay) / (maxDays - startDay));
            float rate = t * t * (3f - 2f * t);   // SmoothStep: slope=0 at both ends, no sudden jump at startDay

            if (!Mathf.Approximately(rate, _lastLoggedRate))
            {
                _modLog.LogInfo($"[PHC] ModifyBelly id={_charaId}: day={day}/{maxDays} startDay={startDay} t={t:F3} rate={rate:F3}");
                _lastLoggedRate = rate;
            }

            BellyVertexMorph.Apply(_human, _charaId, rate);
        }

        public int GetSex()
        {
            return _human.sex;
        }

        protected void LateUpdate()
        {
            if (!created || !Enable || !PregnancyPlugin.ConfigEnable.Value || !_inited)
                return;
            if (BellyVertexMorph.Paused) return;

            if (this.GetSex() == 1)
            {
                // In H-scene, the hi-poly participant is on its own GO (e.g. chaF_05).
                // The lo-poly map clone (e.g. chaF_00_AI) is deactivated at the body level
                // but its root GO (and thus this PHC) is still active.
                // If we let the lo-poly PHC run it wins the BellyVertexMorph cache and the
                // hi-poly body never gets deformed.
                // Solution: skip lo-poly humans when an H-scene is active.
                try
                {
                    if (!_human.hiPoly && Singleton<HScene>.Instance != null)
                        return; // lo-poly clone during H-scene — let the hi-poly PHC handle it
                }
                catch { /* _human may be temporarily invalid; just run normally */ }

                ModifyBelly();
            }
        }
    }
}

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
using System.Diagnostics;
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
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils;
using BepInEx.Unity.IL2CPP.Utils.Collections;

//using UnityEngine.Animations.Rigging;
namespace SVSPregnancy
{

    public partial class PregnancyActorController : CustomActorController
    {
        public new PregnancyActorController _instance { get; protected set; }
        public IntPtr _humanPtr => _hActor.Human.ToPtr();
        public Character.Human _human => _humanPtr.ToObject<Character.Human>();

        public IntPtr _charactrlPtr= IntPtr.Zero;

        public Vector3 _waist01_localscale0 = Vector3.one;
        public Vector3 _spine01_localscale0 = Vector3.one;
        public Vector3 _spine02_localscale0 = Vector3.one;

        public IntPtr _audioObjectPtr = IntPtr.Zero;
        public GameObject _audioObject;

        public IntPtr _CutInFertilize_PlayerPtr { get; internal set; }
        public AudioSource _CutInFertilize_Player => _CutInFertilize_PlayerPtr.ToObject<AudioSource>();

        public PregnancyCharaController _charactrl=> _charactrlPtr.ToObject<PregnancyCharaController>();
        protected readonly object syncLock = new object();

        protected volatile bool created = false;

        protected bool InsideHScene => this._hActor.HasAnimator && this._hActor.Animator.runtimeAnimatorController != null && this._hActor.Human.hiPoly;// { get;  set; }

        protected bool Enable { get; set; }

        protected int _pastAnimState = 0;
        protected int _currentAnimState = 0;
        public bool _animStateChanged => _currentAnimState != _pastAnimState;

        protected bool needtoupdate = false;
        protected bool issynchronic = false;

        private List<bool> initVirginity = new bool[4].ToList();
        private List<bool> Vlost = new bool[4].ToList();
      //  public static List<Texture2D> _fertilizeCutInTextures = new List<Texture2D>();
        private PregnancyAssetController.Wipe.cutIn _fertilized;
        public PregnancyActorController(IntPtr ptr) : base(ptr)
        {
            _instance = this;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        protected void Awake()
        {

            
                this.created = true;
                
            
            
        }

        protected void Start()
        {
            Il2CppSystem.Threading.Thread.Sleep(100);
            lock (syncLock)
            {
                this.Enable = false;
                Il2CppSystem.Threading.Thread.MemoryBarrier();
                if (!this._hActor.Human.hiPoly)
                    return;
                for (int i = 0; i < 4; i++)
                {
                    this.initVirginity[i] = _hActor.hStatus._virginData[i].IsInitVirgin;//init
                    if (!this.initVirginity[i])
                    {

                        this.Vlost[i] = true;//init vlost
                    }
                }

                



            }
            this.Enable = true;



        }

        public override void Init(int index, IntPtr ptrActor)
        {
            // ── Spy: confirm Init is reached ─────────────────────────────
            _spyLog.LogInfo($"[Spy] PAC.Init ENTER index={index} ptrActor=0x{ptrActor:X}");

            _index = index;
            _hActorPtr = ptrActor;

            int sex = -1;
            try { sex = GetSex(); } catch (Exception ex) { _spyLog.LogInfo($"[Spy] GetSex threw: {ex.Message}"); }
            _spyLog.LogInfo($"[Spy] PAC.Init sex={sex}");

            _charaId = this.GetCharaId();
            _spyLog.LogInfo($"[Spy] PAC.Init charaId={_charaId}");

            if (_charaId < 0)
            {
                $"Failed retrive Player{_index}'s charaId.".ToLog();
            }
            var worldctrl = PregnancyPlugin._worldController;
            if (worldctrl != null)
            {
                this._charactrlPtr = worldctrl.FindPregnancyCharaControllerPtr(_charaId);
            }
            // Clear stale lo-poly SMR/frame cache before H-scene uses hi-poly body.
            BellyVertexMorph.Forget(_charaId);
            SpyDumpHSceneMeshes(); // no sex filter — dump for all actors

            GameObject humanobj = _hActor.Human.gameObject;
            PregnancyHumanController humctrl;
            UnityEngine.Component? temphumctrl;
            humanobj.TryGetComponent(Il2CppType.Of<PregnancyHumanController>(), out temphumctrl);
            if (temphumctrl == null)
            {
                humctrl = new PregnancyHumanController(humanobj.AddComponent(Il2CppType.Of<PregnancyHumanController>()).Pointer).Cast<PregnancyHumanController>();
            }
            else
            {
                humctrl = temphumctrl.Cast<PregnancyHumanController>();
            }
            // Always re-init so _humanPtr and _charactrlPtr reflect the H-scene Human.
            humctrl.Init(_hActor.Human.ToPtr());
            if (GetSex() == 1)
            {
                GameObject kokan = FindBone("cf_j_kokan").ToObject<GameObject>();
                var audioObject = new GameObject("AudioObject");
                GameObject.DontDestroyOnLoad(audioObject);
                _audioObjectPtr = audioObject.ToPtr();
                _audioObject = audioObject;
                _CutInFertilize_PlayerPtr = audioObject.GetOrAddComponent<AudioSource>().Cast<AudioSource>().ToPtr();
                audioObject.transform.SetParent(kokan.transform, false);
                audioObject.transform.localPosition = new Vector3(0, 0.13f, 0f);
                if (PregnancyAssetController.CutInFertilize_Sound == null)
                {
                    PregnancyPlugin._assetController.LoadAudioClip(PregnancyPlugin.CutInFertilize_SoundPath);
                }
                this._CutInFertilize_Player.clip = PregnancyAssetController.CutInFertilize_Sound;
                this._CutInFertilize_Player.playOnAwake = false;
                this._CutInFertilize_Player.rolloffMode = AudioRolloffMode.Logarithmic;
                this._CutInFertilize_Player.minDistance = 3f;
                this._CutInFertilize_Player.maxDistance = 20f;
                this._CutInFertilize_Player.dopplerLevel = 1.0f;
                this._CutInFertilize_Player.spatialBlend = 1.0f;
                this._CutInFertilize_Player.volume = 1.0f;
                this._CutInFertilize_Player.loop = false;
                this._CutInFertilize_Player.Stop();

            }

            _inited = true;
        }

        protected override void LateUpdate()
        {
            if (!created || !InsideHScene||!Enable||!PregnancyPlugin.ConfigEnable.Value||!_inited)
            { return; }
            
            //lock (syncLock)
            {

                _pastAnimState = _currentAnimState;
                _currentAnimState = _currAnimStateInfo.nameHash;
                if (this.GetSex() != 0&&!PregnancyPlugin.FutanariCanInseminate.Value)
                {
                    if (GetSex() == 1 && !BellyVertexMorph.Paused) TryApplyBellyDeformH();
                    return;
                }
                else if (_animStateChanged)
                {
                    

                    if (this.IsNowStartOrgasm())
                    {
                        needtoupdate = true;
                        List<Player> listOpponents = PregnancyPlugin._gameController._playerList.Where(p => p.IsAnOpponent(this._thisplayer)).ToList();
                        if (this.AnimIsOrgasmS() || listOpponents.Exists(p => p._actorController.IsNowStartOrgasm()))
                        {
                            issynchronic = true;
                        }
                        else
                        {
                            issynchronic = false;
                        }
                    }
                    

                }

                if (!needtoupdate)
                {
                    return;
                }

                if (_animStateChanged && this.AnimIsOrgasm_A())
                {
                    

                    if (this.AnimIsOrgasm_IN_A())
                    {
                        TrySow();

                    }
                    needtoupdate = false;
                    
                }

                
            }
        }
        private System.Collections.IEnumerator PlayFTMsg(Player opponent,bool issynchronic)
        {
            var charaId = _charaId;
            var oppoId = opponent._charaId;
            var oppoctrl = opponent._actorController;
            var individualityop = opponent._hActor.Human.fileGameParam.individuality.answer.ToList();
            var chastityLvop = opponent._actorController.ChastityLv;
            bool ichizuflagop = !(individualityop.Contains((int)Individuality.一途) && PartnerCount(oppoId) > 0) || HasPartnership(oppoId, charaId);
            bool flagexception = (
                (individualityop.Contains((int)Individuality.一途) && HasPartnership(oppoId, charaId)) ||
                (oppoctrl.IsDarknessOfMind() && (HasPartnership(oppoId, charaId) || HasMaximalFeelingOn(oppoId, charaId, Sensitivity.LOVE)))
                );
            int palatableop = oppoctrl.IsPalatableSex(_thisplayer);
            bool acceptop = !oppoctrl.IsReallyDislike() || flagexception;
            /*
            if (opponent._actorController._CutInFertilize_Player != null)
            {
                if (opponent._actorController._CutInFertilize_Player.clip == null)
                {
                    PregnancyPlugin._assetController.LoadAudioClipWav(PregnancyPlugin.CutInFertilize_SoundPath);
                    opponent._actorController._CutInFertilize_Player.clip = PregnancyAssetController._CutInFertilize_Sound;
                }

                // 12.ToLog();
                //Hooks.SaveLog($"{LewdCrest.CutInFertilize_Sound.samples}");

                opponent._actorController._CutInFertilize_Player.PlayOneShot(opponent._actorController._CutInFertilize_Player.clip);
            }
            Func<bool> fseisplaying = () => opponent._actorController._CutInFertilize_Player.isPlaying;
            yield return new WaitWhile(fseisplaying);*/
            string fathername = this._hActor.Parameter.lastname + this._hActor.Parameter.firstname;
            string mothername = opponent._hActor.Parameter.lastname + opponent._hActor.Parameter.firstname;
            ShowText($"{mothername}は{fathername}に種付けされて孕まされちゃった");
            List<StateKind> newemotionop = new List<StateKind>();
            Favorability addfavoop = new Favorability();
            if (issynchronic)
            {

                newemotionop.AddRange(new List<StateKind>
                                            {
                                            StateKind.RUT,
                                            StateKind.RUT,
                                            StateKind.RUT,
                                            StateKind.SHYNESS,
                                            StateKind.SHYNESS,
                                            StateKind.SHYNESS
                                             });
                addfavoop.AddFavo(90, Sensitivity.LOVE);

            }
            if (!acceptop || !ichizuflagop)
            {
                newemotionop.Add(StateKind.TENSION);
                newemotionop.Add(StateKind.TENSION);
                newemotionop.Add(StateKind.TENSION);
                if (!acceptop)
                {
                    addfavoop.AddFavo(90, Sensitivity.DISLIKE);

                    if (individualityop.Contains((int)Individuality.真面目))
                    {
                        newemotionop.Add(StateKind.TENSION);
                    }
                    if (!ichizuflagop && !HasMaximalFeelingOn(oppoId, charaId, Sensitivity.LOVE))
                    {
                        newemotionop.Add(StateKind.DISAPPOINTMENT);
                        newemotionop.Add(StateKind.DISAPPOINTMENT);
                        newemotionop.Add(StateKind.DISAPPOINTMENT);
                        addfavoop.AddFavo(-90, Sensitivity.LOVE);
                        addfavoop.AddFavo(90, Sensitivity.DISLIKE);
                    }
                    if (oppoctrl.IsBlackHearted() && !HasPartnership(oppoId, charaId) && !HasMaximalFeelingOn(oppoId, charaId, Sensitivity.LOVE))
                    {
                        newemotionop.Add(StateKind.DISAPPOINTMENT);
                        newemotionop.Add(StateKind.DISAPPOINTMENT);
                        newemotionop.Add(StateKind.DISAPPOINTMENT);
                        addfavoop.AddFavo(-60, Sensitivity.LOVE);
                        addfavoop.AddFavo(60, Sensitivity.DISLIKE);
                    }
                    if (oppoctrl.IsDarknessOfMind())
                    {
                        if (chastityLvop >= (int)PropertyLevel.MiddleHigh || individualityop.Contains((int)Individuality.一途))
                        {
                            if (!HasPartnership(oppoId, charaId) && !HasMaximalFeelingOn(oppoId, charaId, Sensitivity.LOVE))
                            {
                                newemotionop.Add(StateKind.DISAPPOINTMENT);
                                newemotionop.Add(StateKind.DISAPPOINTMENT);
                                newemotionop.Add(StateKind.DISAPPOINTMENT);
                                addfavoop.AddFavo(-90, Sensitivity.LOVE);
                                addfavoop.AddFavo(90, Sensitivity.DISLIKE);
                            }
                        }
                    }
                    if (individualityop.Contains((int)Individuality.チャーム) && palatableop > 0)
                    {
                        addfavoop.AddFavo(-30, Sensitivity.DISLIKE);
                        if (individualityop.Contains((int)Individuality.ミーハー))
                        {
                            addfavoop.AddFavo(-60, Sensitivity.DISLIKE);
                        }

                    }




                }

            }
            else
            {
                newemotionop.AddRange(new List<StateKind>
                                            {
                                            StateKind.UPLIFT,
                                            StateKind.UPLIFT,
                                            StateKind.UPLIFT
                                             });
                if (individualityop.Contains((int)Individuality.一途) && HasPartnership(oppoId, charaId))
                {
                    newemotionop.Add(StateKind.UPLIFT);
                    addfavoop.AddFavo(30, Sensitivity.LOVE);
                }
                if (oppoctrl.IsDarknessOfMind() && (HasPartnership(oppoId, charaId) || HasMaximalFeelingOn(oppoId, charaId, Sensitivity.LOVE)) && ichizuflagop)
                {
                    newemotionop.Add(StateKind.UPLIFT);
                    addfavoop.AddFavo(30, Sensitivity.LOVE);

                }
                if (individualityop.Contains((int)Individuality.恋愛脳) && HasPartnership(oppoId, charaId))
                {
                    newemotionop.Add(StateKind.UPLIFT);
                    addfavoop.AddFavo(30, Sensitivity.LOVE);
                }
                if (individualityop.Contains((int)Individuality.チャーム) && palatableop > 0 && ichizuflagop)
                {
                    newemotionop.Add(StateKind.UPLIFT);
                    addfavoop.AddFavo(30, Sensitivity.LOVE);
                    if (individualityop.Contains((int)Individuality.ミーハー))
                    {
                        addfavoop.AddFavo(60, Sensitivity.LOVE);
                    }

                }

            }


            AddFavorability(oppoId, charaId, addfavoop);
            AddEmotion(oppoId, newemotionop);
            if (addfavoop.feelings[(int)Sensitivity.LOVE].ToDecimal() > 0)
            {
                ShowText($"{mothername}は{fathername}のことがもっと好きになった");
            }
            if (addfavoop.feelings[(int)Sensitivity.DISLIKE].ToDecimal() > 0)
            {
                ShowText($"{mothername}は{fathername}のことがもっと嫌いになった");
            }
            yield break;
        }
        protected bool TrySow()
        {
            if (!this.IsInSonyu())
                return false;
            List<Player> listOpponents = PregnancyPlugin._gameController._playerList.Where(p => p.IsAnOpponent(this._thisplayer)).ToList();
            List<Player> listAffectedOopponents = listOpponents.Where(p => p._actorController.IsInvaded()&&p._actorController.VirginaInserted()&&p._actorController.GetSex()==1).ToList();
            var charaId = _charaId;
            //listAffectedOopponents.Count.ToLog();
            foreach (var opponent in listAffectedOopponents)
            {
                var oppoId = opponent._charaId;
                var chastityLvop = opponent._actorController.ChastityLv;
               // Util.SaveLog($"{this.IsInvading()} {this.VirginaInserted()} {this.CalcKaikanCondom()}");

                if (this.IsInvading() && this.VirginaInserted() && this.CalcKaikanCondom() >= 1)
                {
                    {
                        var oppoctrl = opponent._actorController;
                         var individualityop = opponent._hActor.Human.fileGameParam.individuality.answer.ToList();
                        bool ichizuflagop = !(individualityop.Contains((int)Individuality.一途) && PartnerCount(oppoId) > 0) || HasPartnership(oppoId, charaId);
                        bool flagexception= (             
                            (individualityop.Contains((int)Individuality.一途) && HasPartnership(oppoId, charaId)) ||
                            (oppoctrl.IsDarknessOfMind() && (HasPartnership(oppoId, charaId) || HasMaximalFeelingOn(oppoId, charaId, Sensitivity.LOVE)))
                            );
                        int palatableop = oppoctrl.IsPalatableSex(_thisplayer);
                        bool acceptop = !oppoctrl.IsReallyDislike()|| flagexception;
                        var worldctrl = PregnancyPlugin._worldController;
                        if (worldctrl != null)
                        {
                            var oppopregcharactrl = opponent._actorController._charactrl;
                            if (oppopregcharactrl != null)
                            {
                                if (!oppopregcharactrl.IsPregnant())
                                {
                                    Il2CppSystem.Random randomizer = new Il2CppSystem.Random();
                                    int prob = CalcPregnantRate(opponent);
                                    if (issynchronic && (opponent._actorController.IsWeakness() || opponent._actorController.IsLewdness()))
                                    {
                                        prob *= 2;
                                    }
                                    prob = System.Math.Clamp(prob, 0, 100);
                                    if (!(randomizer.Next(100) < prob))
                                    {
                                        continue;
                                    }
             
                                    if (oppopregcharactrl.Conceive(this._hActor.Parameter.firstname, this._hActor.Parameter.lastname, acceptop))
                                    {
                                        //assetrelease
                                        
                                       // PregnancyAssetController.PreloadAllTextures();

                                        /*
                                        opponent._actorController._fertilized = PregnancyAssetController.Wipe.DisplayCutIn(PregnancyPlugin.CutInFertilize, PregnancyAssetController._fertilizeCutInTextures, opponent, PregnancyPlugin.CutInFertilize_Wait.Value, PregnancyPlugin.CutInFertilize_Loop.Value, PregnancyPlugin.CutInFertilize_X.Value, PregnancyPlugin.CutInFertilize_Y.Value, PregnancyPlugin.CutInFertilize_Z.Value, PregnancyPlugin.CutInFertilize_Ratio.Value);
                                            opponent._actorController._fertilized.frameIndex = 0;
                                            opponent._actorController._fertilized.framesPerSecond = PregnancyPlugin.CutInFertilize_FPS.Value;
                                        */
                                        
                                        this.StartCoroutine(PlayFTMsg(opponent,this.issynchronic).WrapToIl2Cpp());

                                        //opponent._actorController._CutInFertilize_Player.PlayOneShot(opponent._actorController._CutInFertilize_Player.clip);
                                       
                                       
                                    }
                                }
                                else if(issynchronic&&opponent._actorController.IsStrongOrWeakness())
                                {
                                    if (oppopregcharactrl.CheckBabySize() >= 1)
                                    {
                                        oppopregcharactrl._pregnancyInfo._broken = true;
                                    }
                                }
                            }
                        }
                    }

                }

            }


            return true;
        }
        private void TryApplyBellyDeformH()
        {
            if (_charactrl == null || !_charactrl.IsPregnant()) return;
            int   day      = _charactrl._pregnancyInfo._day;
            int   startDay = BellyDeformSettings.StartDay;
            int   maxDays  = _charactrl._pregnancyInfo._currentMaximalPregnantDays;

            // Mirror ModifyBelly: no deformation before startDay, smooth reset.
            if (maxDays <= startDay || day <= startDay)
            {
                BellyVertexMorph.Reset(_charaId);
                return;
            }

            float t    = Mathf.Clamp01((float)(day - startDay) / (maxDays - startDay));
            float rate = t * t * (3f - 2f * t);   // SmoothStep: slope=0 at both ends
            BellyVertexMorph.Apply(_human, _charaId, rate);
        }

        protected void OnDestroy()
        {
            // Clear hi-poly SMR cache so the map Human gets a fresh lookup.
            if (_charaId >= 0)
                BellyVertexMorph.Forget(_charaId);
        }

        private static readonly BepInEx.Logging.ManualLogSource _spyLog =
            BepInEx.Logging.Logger.CreateLogSource("SVSPregnancy.Spy");

        private void SpyDumpHSceneMeshes()
        {
            try
            {
                var human = _human;
                _spyLog.LogInfo($"[Spy] ===== H-scene Init charaId={_charaId} hiPoly={_hActor.Human.hiPoly} =====");

                // human.gameObject path
                try { _spyLog.LogInfo($"[Spy] human.gameObject path: {GetTransformPath(_hActor.Human.gameObject.transform)}"); } catch { }

                // bone root path
                try
                {
                    var boneRoot = human?.body?.trfBodyBone;
                    if (boneRoot != null)
                        _spyLog.LogInfo($"[Spy] trfBodyBone path: {GetTransformPath(boneRoot)}");
                    else
                        _spyLog.LogInfo("[Spy] trfBodyBone: null");
                }
                catch { }

                // Scene-wide SMR scan (includes inactive objects)
                _spyLog.LogInfo("[Spy] --- Scene-wide SkinnedMeshRenderer scan ---");
                try
                {
                    var allSmrs = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>(true);
                    if (allSmrs == null) { _spyLog.LogInfo("[Spy] FindObjectsOfType returned null"); }
                    else
                    {
                        _spyLog.LogInfo($"[Spy] Total SMRs in scene: {allSmrs.Length}");
                        foreach (var s in allSmrs)
                        {
                            if (s == null) continue;
                            try
                            {
                                string meshName  = s.sharedMesh != null ? s.sharedMesh.name : "(null)";
                                int    verts     = s.sharedMesh != null ? s.sharedMesh.vertexCount : -1;
                                bool   readable  = s.sharedMesh != null && s.sharedMesh.isReadable;
                                bool   active    = s.gameObject.activeInHierarchy;
                                string path      = GetTransformPath(s.transform);
                                _spyLog.LogInfo(
                                    $"[Spy] SMR \"{s.name}\" | mesh=\"{meshName}\" | verts={verts}" +
                                    $" | readable={readable} | active={active}" +
                                    $" | path={path}");
                            }
                            catch (Exception ex) { _spyLog.LogInfo($"[Spy] SMR error: {ex.Message}"); }
                        }
                    }
                }
                catch (Exception ex) { _spyLog.LogInfo($"[Spy] Scene scan error: {ex.Message}"); }

                _spyLog.LogInfo($"[Spy] ===== end =====");
            }
            catch (Exception ex) { _spyLog.LogInfo($"[Spy] SpyDump error: {ex.Message}"); }
        }

        private static string GetTransformPath(Transform t)
        {
            if (t == null) return "(null)";
            try
            {
                var parts = new System.Collections.Generic.List<string>();
                var cur = t;
                for (int i = 0; i < 20 && cur != null; i++)
                {
                    parts.Add(cur.name);
                    cur = cur.parent;
                }
                parts.Reverse();
                return string.Join("/", parts);
            }
            catch { return t.name; }
        }

        public bool SpermsVSOvum(Player defender)
        {
            var chastityLvop = defender._actorController.ChastityLv;
            int atk = this.PhysicalLv + 1;
            int def = chastityLvop + 1;
            if (this.IsStrongOrWeakness())
            {
                atk += 2;

            }
            if (defender._actorController.IsDislike() || defender._actorController.IsLetMeHoldReceive() || defender._actorController.IsLetMeHold())
            {
                def += 3;
            }
            if (defender._actorController.IsWeakness())
            {
                def = def / 2;
            }
            $"{atk} vs {def}".ToLog();
            if (atk > def)
            {
                return true;

            }
            return false;
        }
        public static void ShowText(String text)
        {
            if (PregnancyPlugin.ConfigLog.Value)
                PregnancyPlugin._instance.Log.LogMessage(text);
        }
        int rateSafe => PregnancyPlugin.OvulationRateSafe.Value;
        int rateNormal => PregnancyPlugin.OvulationRateNormal.Value;
        int rateDangerous => PregnancyPlugin.OvulationRateDanger.Value;
        protected int CalcPregnantRate(Player opponent)
        {
            if (opponent._actorController.GetSex() ==0)
            {
                return 0;
            }
            int risk = 0;
            int day = Manager.Game.saveData.Day % 14;
            var oppoId = opponent._charaId;
            int riskday=_Charas[oppoId].charasGameParam.menstruations[day];
            switch (riskday)
            {
                case (int)Menstruation.Safe:
                    risk = rateSafe;
                    break;
                case (int)Menstruation.Normal:
                    risk = rateNormal;
                    break;
                case (int)Menstruation.Danger:
                    risk= rateDangerous;
                    break;
                default: 
                    break;
            }

            

            return risk;
        }




        #region findbone
        public IntPtr FindBone(string name)
        {
            if (this._humanPtr == IntPtr.Zero)
                return IntPtr.Zero;
            
            if (_human == null || !_human.isActiveAndEnabled || _human.name == null)
                return IntPtr.Zero;
            return _human.body.trfBodyBone.gameObject != null ? FindBone(name, _human.body.trfBodyBone.gameObject.ToPtr()) : IntPtr.Zero;
            //trfBodyBone: Transform

        }


        protected IntPtr FindBone(string name, IntPtr rootPtr, bool noRetry = false)//rootPtr:GameObject*;__result:GameObject*
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (rootPtr == IntPtr.Zero) throw new ArgumentNullException(nameof(rootPtr));

            var recreated = false;
            if (!_lookup.TryGetValue(rootPtr, out var boneDic))
            {
                PurgeDestroyed();
                boneDic = CreateBoneDic(rootPtr);
                recreated = true;
                _lookup[rootPtr] = boneDic;
            }

            boneDic.TryGetValue(name, out var boneObj);
            if (boneObj == IntPtr.Zero && !recreated && !noRetry)
            {
                PurgeDestroyed();
                boneDic = CreateBoneDic(rootPtr);
                _lookup[rootPtr] = boneDic;
                boneDic.TryGetValue(name, out boneObj);
            }

            return boneObj;
        }

        protected void PurgeDestroyed()
        {
            foreach (var nullGo in _lookup.Keys.Where(x => x == null).ToList()) _lookup.Remove(nullGo);
        }
        public Dictionary<string, IntPtr> CreateBoneDic(IntPtr rootObjectPtr)// CreateBoneDic:GameObject*=>Dictionary<string, GameObject*>
        {

            var d = new Dictionary<string, IntPtr>();
    
            FindAll(rootObjectPtr.ToObject<GameObject>().transform.ToPtr(), d, _human.acs.Accessories.Where(x => x != null && x.objAccessory != null).Select(x => x.objAccessory.transform.ToPtr()).ToList());
            return d;
        }

        protected static void FindAll(IntPtr roottransformPtr, Dictionary<string, IntPtr> dictObjName, List<IntPtr> excludeTransforms)//transformPtr:Transform*; dictObjName:Dictionary<string, GameObject*>;excludeTransforms:List<Transform*>
        {
            // Util.SaveLog($"{transformPtr}");
            if (!dictObjName.ContainsKey(roottransformPtr.ToObject<Transform>().gameObject.name))
                dictObjName[roottransformPtr.ToObject<Transform>().gameObject.name] = roottransformPtr.ToObject<Transform>().gameObject.ToPtr();
            // Util.SaveLog($"{transformPtr.ToObject<Transform>().gameObject.name},{transformPtr.ToObject<Transform>().childCount}");
            for (var i = 0; i < roottransformPtr.ToObject<Transform>().childCount; i++)
            {
                var childTransform = roottransformPtr.ToObject<Transform>().GetChild(i);
                var trName = childTransform.name;

                // Exclude parented characters (in Studio) and accessories/other
                if (!trName.StartsWith("chaF_") && !trName.StartsWith("chaM_") && !excludeTransforms.Contains(childTransform.ToPtr()))
                    FindAll(childTransform.ToPtr(), dictObjName, excludeTransforms);
            }
        }
        private Dictionary<IntPtr, Dictionary<string, IntPtr>> _lookup = new Dictionary<IntPtr, Dictionary<string, IntPtr>>();
        #endregion


    }
}

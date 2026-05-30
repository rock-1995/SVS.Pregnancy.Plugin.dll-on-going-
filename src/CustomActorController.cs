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
using static ADV.Commands.Chara.KaraokePlay;
using static SVSPregnancy.Player;
using ILLGames.Extensions;
using UnityEngine.PlayerLoop;
using static ILLGames.Unity.Utils;
using MessagePack.Formatters.SaveData;
using static SaveData.SensitivityParameter;
using UnityEngine.Animations;
using static SaveData.StateParameter;
using static SaveData.Extension.ActorExtensionH;
using static SVSPregnancy.PregnancyPlugin;
namespace SVSPregnancy
{
    public class CustomActorController : MonoBehaviour
    {
        public CustomActorController _instance { get; protected set; }

        public HScene _hscene => Singleton<HScene>.Instance;
        public Player _thisplayer { get; set; }
        public int _index = -1;
        public bool _inited = false;
        public IntPtr _hActorPtr = IntPtr.Zero;
        public HActor _hActor => _hActorPtr.ToObject<HActor>();
        public int _charaId = -1;
        public static Il2CppSystem.Collections.Generic.Dictionary<int, Actor> _Charas => Manager.Game.Charas;
        public Il2CppSystem.Collections.Generic.Dictionary<int, SensitivityParameter.FavorabiliryInfo> _Favorabilitys => _Charas[_charaId].charasGameParam.sensitivity.tableFavorabiliry;
        public CustomActorController(IntPtr ptr) : base(ptr)
        {
            _instance = this;
        }

        public virtual void Init(int index, IntPtr ptrActor)
        {
            _index = index;
            _hActorPtr = ptrActor;

            _charaId = this.GetCharaId();
            if (_charaId < 0)
            {
                $"Failed to retrieve Player{_index}'s charaId.".ToLog();
            }
            _inited = true;
        }



        #region Anim
        public AnimatorStateInfo _currAnimStateInfo => _hActor.GetCurrentAnimatorStateInfo(0);
        public List<string> _currAnimName => _animator.runtimeAnimatorController.name.Split('_').ToList();
        private List<string> origins = new List<string>() { "sv", "hc", "3p" };
        private bool unknowOrigin => !origins.Contains(_currAnimName[0]);
        public string _animOrigin => unknowOrigin ? "hc" : _currAnimName[0];
        public string _animForm => unknowOrigin ? _currAnimName[0] : _currAnimName[1];
        public string _animPos => unknowOrigin ? _currAnimName[1] : _currAnimName[2];
        public string _animNum => unknowOrigin ? _currAnimName[2] : _currAnimName[3];
        public UnityEngine.Animator _animator => _hActor.Animator;


        public static Dictionary<(string, string), List<(int, string)>> dict_Active = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (03, "m"), (04, "m"), (05, "m"), (06, "m"), (07, "m"), (08, "m"), (09, "m"), (10, "m"), (11, "m"), (12, "m"), (13, "m"), (14, "m"), (15, "m"), (16, "m"), (17, "m"), (18, "m"), (19, "m"), (20, "m"), (21, "m"), (22, "m"), (23, "m"), (24, "m"), (25, "m"), (26, "m"), (27, "m"), (28, "m"), (29, "m"), (30, "m"), (31, "m"), (32, "m"), (33, "m"), (34, "m"), (35, "m"), (36, "m"), (37, "m"), (38, "m"), (39, "m"), (40, "m"), (41, "m") } },
            { ("sv","hou"), new List<(int, string)> { (00, "f"), (01, "f"), (02, "f"), (03, "m"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), (08, "f"), (09, "f"), (10, "f"), (11, "f"), (12, "f"), (13, "m"), (14, "f"), (15, "f"), (16, "f"), (17, "f"), (18, "f"), (19, "f"), (20, "f"), (21, "f"), (22, "f"), (23, "m"), (24, "m") } },
            { ("sv","som"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (03, "m"), (04, "m"), (05, "m"), (06, "m"), (07, "m"), (08, "m"), (09, "m"), (10, "m"), (11, "m"), (12, "m"), (13, "m"), (14, "m"), (15, "m"), (16, "m"), (17, "m"), (18, "m"), (19, "m"), (20, "m"), (21, "m"), (22, "m"), (23, "m"), (24, "m"), (25, "m"), (26, "m"), (27, "m"), (28, "m"), (29, "m"), (30, "m"), (31, "m"), (32, "m")} },
            { ("sv","sof"), new List<(int, string)> { (00, "f"), (01, "f"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), (08, "f"), (09, "f"), (10, "f"), (11, "f"), (12, "f"), (13, "f"), (14, "f"), (15, "m"), (16, "m"), (17, "m"), (18, "m"), (19, "m"), (20, "m"), (21, "m"), (22, "m") } } ,
            { ("sv","rez"), new List<(int, string)> { (00, "f1"),(00, "f2"),(01,"f1"), (01, "f2")} } ,
            { ("sv","mff"), new List<(int, string)> { (00, "f1"), (00, "f2"), (01, "f1"), (01, "f2"), (02, "m"), (03, "f1"), (03, "f2"), (04, "m"), (05, "m"), (06, "m"), (07, "m"), } },
            { ("sv","mmf"), new List<(int, string)> { (00, "m1"), (00, "m2"), (01, "f"), (02, "m1"), (02, "m2"), (03, "m1"), (03, "m2"), (04, "m1"), (04, "m2"), (05, "m1"), (05, "m2"), (06, "m1"), (06, "m2"), (07, "m1"), (07, "m2"), (08, "m1"), (08, "m2"), (09, "m1"), (09, "m2"), (10, "m1"), (10, "m2") } },
            { ("hc","aib"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (03, "m"), (04, "m"), (05, "m"), (06, "m"), (07, "m"), (08, "m"), (09, "m"), (10, "m"), (11, "m"), (12, "m"), (13, "m"), (14, "m"), (15, "m"), (16, "m"), (17, "m"), (18, "m"), (19, "m"), (20, "m"), (21, "m"), (22, "m"), (23, "m"), (24, "m"), (25, "m"), (26, "m"), (27, "m"), (28, "m"), (29, "m"), (30, "m"), (31, "m"), (32, "m")} },
            { ("hc","hou"), new List<(int, string)> { (00, "f"), (01, "f"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), (08, "f"), (09, "m"), (10, "m"), (11, "f"), (12, "f"), (13, "f"), (14, "f"), (15, "f"), (16, "f"), (17, "m"), (18, "f"), (19, "m"), (20, "f"), (21, "f"), (22, "f"), (23, "f"), (24, "f"), (25, "f"), (26, "f"), (27, "f"), (28, "f"), (29, "f"), (30, "f"), (31, "f"), (32, "m"), (33, "m"), (34, "f"), (35, "f"), (36, "f"), (37, "m"), (39, "f"), } },
            { ("hc","tok"), new List<(int, string)> { (00, "f"), (01, "f"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "m"), (07, "f"), (08, "m"), (08, "f"), (09, "m"), (09, "f"), (10, "m"), (11, "m"), (12, "m"), (13, "m"), (14, "m"), (15, "m"), (16, "m"), (22, "m"), (22, "f"), (29, "m"), (29, "f"), (30, "m"), (30, "f"), } },
            { ("hc","sou"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (03, "m"), (04, "m"), (05, "m"), (06, "m"), (07, "m"), (08, "m"), (09, "m"), (10, "m"), (11, "m"), (12, "m"), (13, "f"), (14, "f"), (15, "f"), (16, "f"), (17, "f"), (18, "m"), (19, "m"), (20, "m"), (21, "m"), (22, "m"), (23, "m"), (24, "m"), (25, "m"), (26, "m"), (27, "m"), (28, "m"), (29, "m"), (30, "m"), (31, "f"), (32, "m"), (33, "m"), (34, "m"), (35, "m"), (36, "m"), (37, "m"), (38, "m"), (39, "m"), (40, "m"), (41, "f"), (42, "f"), (43, "f"), (44, "f"), (45, "m"), (46, "m"), (47, "m"), (48, "f"), (49, "m"), (50, "m"), (51, "m"), (52, "m"), (53, "f"), (54, "m"), (55, "f"), (56, "m"), (57, "f"), (58, "m"), (59, "m"), (60, "m"), (61, "m"), (62, "m"), (63, "m"), (64, "m"), (65, "m"), (66, "m"), (68, "m"), } },
            { ("hc","les"), new List<(int, string)> { (00, "f1"), (00, "f2"), (01, "f2"), (02, "f2"), (03, "f2"), (04, "f2"), } },
            { ("3p","mf2"), new List<(int, string)> { (00, "f1"), (00, "f2"), (01, "f1"), (01, "f2"), (02, "m"), (03, "m"), (04, "f1"), (04, "f2"), (05, "m"), } },
            { ("3p","m2f"), new List<(int, string)> { (00, "m1"), (00, "m2"), (01, "f"), (02, "m1"), (02, "m2"), (03, "m1"), (03, "m2"), (04, "m1"), (04, "m2"), (05, "m1"), (05, "m2"), (06, "m1"), (06, "m2"), (07, "m1"), (07, "m2"), } },

        };
        public static Dictionary<(string, string), List<(int, string)>> dict_Passive = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> { (00, "f"), (01, "f"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), (08, "f"), (09, "f"), (10, "f"), (11, "f"), (12, "f"), (13, "f"), (14, "f"), (15, "f"), (16, "f"), (17, "f"), (18, "f"), (19, "f"), (20, "f"), (21, "f"), (22, "f"), (23, "f"), (24, "f"), (25, "f"), (26, "f"), (27, "f"), (28, "f"), (29, "f"), (30, "f"), (31, "f"), (32, "f"), (33, "f"), (34, "f"), (35, "f"), (36, "f"), (37, "f"), (38, "f"), (39, "f"), (40, "f"), (41, "f") } },
            { ("sv","hou"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (03, "f"), (04, "m"), (05, "m"), (06, "m"), (07, "m"), (08, "m"), (09, "m"), (10, "m"), (11, "m"), (12, "m"), (13, "f"), (14, "m"), (15, "m"), (16, "m"), (17, "m"), (18, "m"), (19, "m"), (20, "m"), (21, "m"), (22, "m"), (23, "f"), (24, "f") } },
            { ("sv","som"), new List<(int, string)> { (00, "f"), (01, "f"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), (08, "f"), (09, "f"), (10, "f"), (11, "f"), (12, "f"), (13, "f"), (14, "f"), (15, "f"), (16, "f"), (17, "f"), (18, "f"), (19, "f"), (20, "f"), (21, "f"), (22, "f"), (23, "f"), (24, "f"), (25, "f"), (26, "f"), (27, "f"), (28, "f"), (29, "f"), (30, "f"), (31, "f"), (32, "f") } },
            { ("sv","sof"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (03, "m"), (04, "m"), (05, "m"), (06, "m"), (07, "m"), (08, "m"), (09, "m"), (10, "m"), (11, "m"), (12, "m"), (13, "m"), (14, "m"), (15, "f"), (16, "f"), (17, "f"), (18, "f"), (19, "f"), (20, "f"), (21, "f"), (22, "f") } } ,
            { ("sv","rez"), new List<(int, string)> {  } },
            { ("sv","mff"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "f1"), (02, "f2"), (03, "m"), (04, "f1"), (04, "f2"), (05, "f1"), (05, "f2"), (06, "f1"), (06, "f2"), (07, "f1"), (07, "f2")} },
            { ("sv","mmf"), new List<(int, string)> { (00, "f"), (01, "m1"), (01, "m2"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), (08, "f"), (09, "f"), (10, "f") } },
            { ("hc","aib"), new List<(int, string)> { (00, "f"), (01, "f"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), (08, "f"), (09, "f"), (10, "f"), (11, "f"), (12, "f"), (13, "f"), (14, "f"), (15, "f"), (16, "f"), (17, "f"), (18, "f"), (19, "f"), (20, "f"), (21, "f"), (22, "f"), (23, "f"), (24, "f"), (25, "f"), (26, "f"), (27, "f"), (28, "f"), (29, "f"), (30, "f"), (31, "f"), (32, "f")} },
            { ("hc","hou"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (03, "m"), (04, "m"), (05, "m"), (06, "m"), (07, "m"), (08, "m"), (09, "f"), (10, "f"), (11, "m"), (12, "m"), (13, "m"), (14, "m"), (15, "m"), (16, "m"), (17, "f"), (18, "m"), (19, "f"), (20, "m"), (21, "m"), (22, "m"), (23, "m"), (24, "m"), (25, "m"), (26, "m"), (27, "m"), (28, "m"), (29, "m"), (30, "m"), (31, "m"), (32, "f"), (33, "f"), (34, "m"), (35, "m"), (36, "m"), (37, "f"), (39, "m"), } },
            { ("hc","tok"), new List<(int, string)> { (10, "f"), (11, "f"), (12, "f"), (13, "f"), (14, "f"), (15, "f"), (16, "f"), } },
            { ("hc","sou"), new List<(int, string)> { (00, "f"), (01, "f"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), (08, "f"), (09, "f"), (10, "f"), (11, "f"), (12, "f"), (13, "m"), (14, "m"), (15, "m"), (16, "m"), (17, "m"), (18, "f"), (19, "f"), (20, "f"), (21, "f"), (22, "f"), (23, "f"), (24, "f"), (25, "f"), (26, "f"), (27, "f"), (28, "f"), (29, "f"), (30, "f"), (31, "m"), (32, "f"), (33, "f"), (34, "f"), (35, "f"), (36, "f"), (37, "f"), (38, "f"), (39, "f"), (40, "f"), (41, "m"), (42, "m"), (43, "m"), (44, "m"), (45, "f"), (46, "f"), (47, "f"), (48, "m"), (49, "f"), (50, "f"), (51, "f"), (52, "f"), (53, "m"), (54, "f"), (55, "m"), (56, "f"), (57, "m"), (58, "f"), (59, "f"), (60, "f"), (60, "f"), (61, "f"), (62, "f"), (63, "f"), (64, "f"), (65, "f"), (66, "f"), (68, "f"), } },
            { ("hc","les"), new List<(int, string)> { (01, "f1"), (02, "f1"), (03, "f1"), (04, "f1"), } },
            { ("3p","mf2"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "f1"), (02, "f2"), (03, "f1"), (03, "f2"), (04, "m"), (05, "f1"), (05, "f2"), } },
            { ("3p","m2f"), new List<(int, string)> { (00, "f"), (01, "m1"), (01, "m2"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), } },
        };

        public static Dictionary<(string, string), List<(int, string)>> dict_FacetoFace = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> {  } },
            { ("sv","hou"), new List<(int, string)> {  } },
            { ("sv","som"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (05, "m"), (07, "m"), (11, "m"), (13, "m"), (14, "m"), (19, "m"), (21, "m"), (23, "m"), (26, "m"), (28, "m"), (00, "f"), (01, "f"), (02, "f"), (05, "f"), (07, "f"), (11, "f"), (13, "f"), (14, "f"), (19, "f"), (21, "f"), (23, "f"), (26, "f"), (28, "f") } },
            { ("sv","sof"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (05, "m"), (07, "m"), (09, "m"), (11, "m"), (12, "m"), (15, "m"), (17, "m"), (19, "m"), (21, "m"), (00, "f"), (01, "f"), (02, "f"), (05, "f"), (07, "f"), (09, "f"), (11, "f"), (12, "f"),(15, "f"), (17, "f"), (19, "f"), (21, "f") } } ,
            { ("sv","rez"), new List<(int, string)> { (00,"f1"),  (00, "f2")} } ,
            { ("sv","mff"), new List<(int, string)> { (00, "m"), (00, "f1"), (05, "m"), (05, "f1") } },
            { ("sv","mmf"), new List<(int, string)> { (00, "m1"), (00, "f"), (02, "m1"), (02, "f"), (03, "m1"), (03, "f"), (06, "m1"), (06, "f"), (07, "m1"), (07, "f"), (08, "m1"), (08, "f"), (09, "m1"), (09, "f"), (10, "m1"), (10, "f") } },
            { ("hc","aib"), new List<(int, string)> { } },
            { ("hc","hou"), new List<(int, string)> { } },
            { ("hc","tok"), new List<(int, string)> { } },
            { ("hc","sou"), new List<(int, string)> { (00, "m"), (00, "f"), (01, "m"), (01, "f"), (05, "m"), (05, "f"), (06, "m"), (06, "f"), (08, "m"), (08, "f"), (09, "m"), (09, "f"), (11, "m"), (11, "f"), (13, "m"), (13, "f"), (15, "m"), (15, "f"), (16, "m"), (16, "f"), (17, "m"), (17, "f"), (18, "m"), (18, "f"), (19, "m"), (19, "f"), (20, "m"), (20, "f"), (23, "m"), (23, "f"), (32, "m"), (32, "f"), (37, "m"), (37, "f"), (39, "m"), (39, "f"), (40, "m"), (40, "f"), (42, "m"), (42, "f"), (44, "m"), (44, "f"), (47, "m"), (47, "f"), (49, "m"), (49, "f"), (51, "m"), (51, "f"), (52, "m"), (52, "f"), (53, "m"), (53, "f"), (54, "m"), (54, "f"), (55, "m"), (55, "f"), (57, "m"), (57, "f"), (58, "m"), (58, "f"), (59, "m"), (59, "f"), (60, "m"), (60, "f"), (63, "m"), (63, "f"), (64, "m"), (64, "f"), (68, "m"), (68, "f"), } },
            { ("hc","les"), new List<(int, string)> { (00, "f1"), (00, "f2") } },
            { ("3p","mf2"), new List<(int, string)> { (00, "m"), (00, "f1"), } },
            { ("3p","m2f"), new List<(int, string)> { (00, "m1"), (00, "f"), (02, "m1"), (02, "f"), (03, "m2"), (03, "f"), } },
        };

        public static Dictionary<(string, string), List<(int, string)>> dict_FacetoBack = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> {  } },
            { ("sv","hou"), new List<(int, string)> {  } },
            { ("sv","som"), new List<(int, string)> { (03, "m"), (04, "m"), (06, "m"), (08, "m"), (10, "m"), (12, "m"), (15, "m"), (16, "m"), (17, "m"), (18, "m"), (20, "m"), (22, "m"), (24, "m"), (25, "m"), (27, "m"), (29, "m"), (30, "m"), (31, "m"), (32, "m"),(03, "f"), (04, "f"), (06, "f"), (08, "f"), (10, "f"), (12, "f"), (15, "f"), (16, "f"), (17, "f"), (18, "f") , (20, "f"), (22, "f"), (24, "f"), (25, "f"), (27, "f"), (29, "f"), (30, "f"), (31, "f"), (32, "f")} },
            { ("sv","sof"), new List<(int, string)> { (03, "m"), (04, "m"), (06, "m"), (08, "m"), (10, "m"), (13, "m"), (14, "m"), (16, "m"), (18, "m"), (20, "m"), (22, "m"), (03, "f"), (04, "f"), (06, "f"), (08, "f"), (10, "f"), (13, "f"), (14, "f"), (16, "f"), (18, "f"), (20, "f"), (22, "f") } },
            { ("sv","rez"), new List<(int, string)> {  } },
            { ("sv","mff"), new List<(int, string)> { (02, "m"), (02, "f1"), (04, "m"), (04, "f1"), (06, "m"), (06, "f1"), (07, "m"), (07, "f1") } },
            { ("sv","mmf"), new List<(int, string)> { (00, "m2"), (00, "f"), (02, "m2"), (02, "f"), (03, "m2"), (03, "f"), (04, "m1"), (04, "f"), (05, "m1"), (05, "f"), (06, "m2"), (06, "f"), (07, "m2"), (07, "f"), (08, "m2"), (08, "f")} },
            { ("hc","aib"), new List<(int, string)> { } },
            { ("hc","hou"), new List<(int, string)> { } },
            { ("hc","tok"), new List<(int, string)> { } },
            { ("hc","sou"), new List<(int, string)> { (02, "m"), (02, "f"), (03, "m"), (03, "f"), (04, "m"), (04, "f"), (07, "m"), (07, "f"), (10, "m"), (10, "f"), (12, "m"), (12, "f"), (14, "m"), (14, "f"), (21, "m"), (21, "f"), (22, "m"), (22, "f"),  (24, "m"), (24, "f"), (25, "m"), (25, "f"), (26, "m"), (26, "f"), (27, "m"), (27, "f"), (28, "m"), (28, "f"), (29, "m"), (29, "f"), (30, "m"), (30, "f"), (31, "m"), (31, "f"), (33, "m"), (33, "f"), (34, "m"), (34, "f"), (35, "m"), (35, "f"), (36, "m"), (36, "f"), (41, "m"), (41, "f"), (43, "m"), (43, "f"), (45, "m"), (45, "f"), (46, "m"), (46, "f"), (48, "m"), (48, "f"), (50, "m"), (50, "f"), (56, "m"), (56, "f"), (61, "m"), (61, "f"), (62, "m"), (62, "f"), (65, "m"), (65, "f"), (66, "m"), (66, "f"), } },
            { ("hc","les"), new List<(int, string)> { } },
            { ("3p","mf2"), new List<(int, string)> { (03, "m"), (03, "f1"), (05, "m"), (05, "f1"), } },
            { ("3p","m2f"), new List<(int, string)> { (00, "m2"), (00, "f"), (02, "m2"), (02, "f"), (03, "m1"), (03, "f"), (04, "m1"), (04, "f"), (05, "m1"), (05, "f"), (06, "m1"), (06, "f"), (07, "m1"), (07, "f"), } },
        };


        public static Dictionary<(string, string), List<(int, string)>> dict_VirginaInserted = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> {  } },
            { ("sv","hou"), new List<(int, string)> {  } },
            { ("sv","som"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (03, "m"), (04, "m"), (05, "m"), (06, "m"), (07, "m"), (08, "m"), (09, "m"), (10, "m"), (11, "m"), (12, "m"), (19, "m"), (20, "m"), (21, "m"), (22, "m"), (23, "m"), (24, "m"), (25, "m"), (26, "m"), (27, "m"), (00, "f"), (01, "f"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), (08, "f"), (09, "f"), (10, "f"), (11, "f"), (12, "f"), (19, "f"), (20, "f"), (21, "f"), (22, "f"), (23, "f"), (24, "f"), (25, "f"), (26, "f"), (27, "f")} },
            { ("sv","sof"), new List<(int, string)> { (00, "m"), (01, "m"), (02, "m"), (03, "m"), (04, "m"), (05, "m"), (06, "m"), (07, "m"), (08, "m"), (09, "m"), (10, "m"), (15, "m"), (16, "m"), (17, "m"), (18, "m"), (19, "m"), (20, "m"), (00, "f"), (01, "f"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), (08, "f"), (09, "f"), (10, "f"), (15, "f"), (16, "f"), (17, "f"), (18, "f"), (19, "f"), (20, "f") } } ,
            { ("sv","rez"), new List<(int, string)> {  } },
            { ("sv","mff"), new List<(int, string)> { (00, "m"), (00, "f1"), (02, "m"), (02, "f1"), (04, "m"), (04, "f1"), (05, "m"), (05, "f1"), (06, "m"), (06, "f1"), (07, "m"), (07, "f1") } },
            { ("sv","mmf"), new List<(int, string)> { (00, "m1"), (00, "f"), (02, "m1"), (02, "f"), (03, "m1"), (03, "f"), (04, "m1"), (04, "f"), (05, "m1"), (05, "f"), (06, "m1"), (06, "f"), (07, "m1"), (07, "f"), (08, "m1"), (08, "f"), (09, "m1"), (09, "f"), (10, "m1"), (10, "f") } },
            { ("hc","aib"), new List<(int, string)> { } },
            { ("hc","hou"), new List<(int, string)> { } },
            { ("hc","tok"), new List<(int, string)> { } },
            { ("hc","sou"), new List<(int, string)> { (00, "m"), (00, "f"), (01, "m"), (01, "f"), (02, "m"), (02, "f"), (03, "m"), (03, "f"), (04, "m"), (04, "f"), (05, "m"), (05, "f"), (06, "m"), (06, "f"), (07, "m"), (07, "f"), (08, "m"), (08, "f"), (09, "m"), (09, "f"), (10, "m"), (10, "f"), (13, "m"), (13, "f"), (14, "m"), (14, "f"), (15, "m"), (15, "f"), (18, "m"), (18, "f"), (19, "m"), (19, "f"), (20, "m"), (20, "f"), (21, "m"), (21, "f"), (22, "m"), (22, "f"), (23, "m"), (23, "f"), (24, "m"), (24, "f"), (26, "m"), (26, "f"), (27, "m"), (27, "f"), (29, "m"), (29, "f"), (32, "m"), (32, "f"), (33, "m"), (33, "f"), (34, "m"), (34, "f"), (35, "m"), (35, "f"), (36, "m"), (36, "f"), (37, "m"), (37, "f"), (38, "m"), (38, "f"), (39, "m"), (39, "f"), (40, "m"), (40, "f"), (41, "m"), (41, "f"), (42, "m"), (42, "f"), (43, "m"), (43, "f"), (44, "m"), (44, "f"), (45, "m"), (45, "f"), (46, "m"), (46, "f"), (49, "m"), (49, "f"), (50, "m"), (50, "f"), (51, "m"), (51, "f"), (52, "m"), (52, "f"), (53, "m"), (53, "f"), (54, "m"), (54, "f"), (55, "m"), (55, "f"), (56, "m"), (56, "f"), (57, "m"), (57, "f"), (58, "m"), (58, "f"), (59, "m"), (59, "f"), (60, "m"), (60, "f"), (62, "m"), (62, "f"), (63, "m"), (63, "f"), (64, "m"), (64, "f"), (65, "m"), (65, "f"), (66, "m"), (66, "f"), (68, "m"), (68, "f"), } },
            { ("hc","les"), new List<(int, string)> { } },
            { ("3p","mf2"), new List<(int, string)> { (00, "m"), (00, "f1"), (03, "m"), (03, "f1"), (05, "m"), (05, "f1"), } },
            { ("3p","m2f"), new List<(int, string)> {  (00, "m1"), (00, "f"), (02, "m1"), (02, "f"), (03, "m2"), (03, "f"), (04, "m1"), (04, "f"), (05, "m1"), (05, "f"), (06, "m1"), (06, "f"), (07, "m1"), (07, "f"), } },
        };
        public static Dictionary<(string, string), List<(int, string)>> dict_AnalInserted = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> {  } },
            { ("sv","hou"), new List<(int, string)> {  } },
            { ("sv","som"), new List<(int, string)> { (13, "m"), (14, "m"), (15, "m"), (16, "m"), (17, "m"), (18, "m"), (28, "m"), (29, "m"), (30, "m"), (31, "m"), (32, "m"), (13, "f"), (14, "f"), (15, "f"), (16, "f"), (17, "f"), (18, "f"), (28, "f"), (29, "f"), (30, "f"), (31, "f"), (32, "f") } },
            { ("sv","sof"), new List<(int, string)> { (11, "m"), (12, "m"), (13, "m"), (14, "m"), (21, "m"), (22, "m"), (11, "f"), (12, "f"), (13, "f"), (14, "f"), (21, "f"), (22, "f") } } ,
            { ("sv","rez"), new List<(int, string)> {  } },
            { ("sv","mff"), new List<(int, string)> {  } },
            { ("sv","mmf"), new List<(int, string)> { (00, "m2"), (00, "f"), (02, "m2"), (02, "f"), (03, "m2"), (03, "f"), (06, "m2"), (06, "f"), (07, "m2"), (07, "f"), (08, "m2"), (08, "f") } },
            { ("hc","aib"), new List<(int, string)> { } },
            { ("hc","hou"), new List<(int, string)> { } },
            { ("hc","tok"), new List<(int, string)> { } },
            { ("hc","sou"), new List<(int, string)> { (11, "m"), (11, "f"), (12, "m"), (12, "f"), (28, "m"), (28, "f"), (30, "m"), (30, "f"), (47, "m"), (47, "f"), (48, "m"), (48, "f"), } },
            { ("hc","les"), new List<(int, string)> { } },
            { ("3p","mf2"), new List<(int, string)> {  } },
            { ("3p","m2f"), new List<(int, string)> { (00, "m2"), (00, "f"), (02, "m2"), (02, "f"), (03, "m1"), (03, "f"), } },
        };

        public static Dictionary<(string, string), List<(int, string)>> dict_kuchi = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> { (00, "m"), (00, "f"), (05, "m"), (07, "m"), (07, "f"), (10, "m"), (13, "m"), (17, "m"), (19, "m"), (19, "f"), (24, "m"), (26, "m"), (26, "f"), (29, "m"), (32, "m"), (36, "m"), (39, "m"), (41, "m")} },
            { ("sv","hou"), new List<(int, string)> {  (01, "f"), (02, "f"), (03, "f"), (05, "f"), (06, "f"), (08, "f"), (09, "f"), (11, "f"), (12, "f"), (13, "f"), (15, "f"), (16, "f"), (18, "f"), (19, "f"), (21, "f"), (22, "f"), (23, "f"), (24, "f") } },
            { ("sv","som"), new List<(int, string)> { (01, "m"), (01, "f") } },
            { ("sv","sof"), new List<(int, string)> {  } },
            { ("sv","rez"), new List<(int, string)> { (01,"f1"),  (01, "f2")} },
            { ("sv","mff"), new List<(int, string)> { (00, "m"), (01, "f1"), (01, "f2"), (03, "f1"), (04, "m"), (05, "m"), (07, "m")} },
            { ("sv","mmf"), new List<(int, string)> { (01, "f"), (04, "f"), (05, "f"), (09, "f"), (10, "f") } },
            { ("hc","aib"), new List<(int, string)> { (00, "m"), (00, "f"), (01, "m"), (01, "f"), (04, "m"), (06, "m"), (07, "m"), (07, "f"), (08, "m"), (10, "m"), (11, "m"), (11, "f"), (12, "m"), (12, "f"), (13, "m"), (18, "m"), (19, "m"), (21, "m"), (22, "m"), (24, "m"), (25, "m"), (25, "f"), (29, "m"), (30, "m"), (31, "m"), } },
            { ("hc","hou"), new List<(int, string)> { (00, "f"), (04, "f"), (05, "f"), (07, "f"), (10, "f"), (12, "f"), (13, "f"), (14, "f"), (16, "f"), (17, "f"), (19, "f"), (21, "f"), (23, "f"), (25, "f"), (26, "f"), (32, "f"), (33, "f"), (34, "f"), (37, "f"), (39, "f"), } },
            { ("hc","tok"), new List<(int, string)> { (07, "m"), (07, "f"), (08, "m"), (08, "f"), (09, "m"), (22, "m"), (22, "f"), (30, "f"), } },
            { ("hc","sou"), new List<(int, string)> { } },
            { ("hc","les"), new List<(int, string)> { (02, "f2"), (03, "f2"),  (04, "f2"), } },
            { ("3p","mf2"), new List<(int, string)> {  (00, "m"), (01, "f1"), (01, "f2"), (04, "f1"), (05, "m"),  } },
            { ("3p","m2f"), new List<(int, string)> { (01, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "f"), } },

        };


        public static Dictionary<(string, string), List<(int, string)>> dict_kuchied = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> { (00, "m"), (00, "f"), (05, "f"), (07, "m"), (07, "f"), (10, "f"), (13, "f"), (17, "f"), (19, "m"), (19, "f"), (24, "f"), (26, "m"), (26, "f"), (29, "f"), (32, "f"), (36, "f"), (39, "f"), (41, "f")} },
            { ("sv","hou"), new List<(int, string)> {  (01, "m"), (02, "m"), (03, "m"), (05, "m"), (06, "m"), (08, "m"), (09, "m"), (11, "m"), (12, "m"), (13, "m"), (15, "m"), (16, "m"), (18, "m"), (19, "m"), (21, "m"), (22, "m"), (23, "m"), (24, "m") } },
            { ("sv","som"), new List<(int, string)> { (01, "m"), (01, "f") } },
            { ("sv","sof"), new List<(int, string)> {  } },
            { ("sv","rez"), new List<(int, string)> { (01,"f1"),  (01, "f2")} },
            { ("sv","mff"), new List<(int, string)> { (00, "f2"), (01, "m"), (03, "m"), (04, "f2"), (05, "f2"), (07, "f2")} },
            { ("sv","mmf"), new List<(int, string)> { (01, "m1"), (04, "m2"), (05, "m2"), (09, "m2"), (10, "m2") } },
            { ("hc","aib"), new List<(int, string)> { (00, "m"), (00, "f"), (01, "m"), (01, "f"), (04, "f"), (06, "f"), (07, "m"), (07, "f"), (08, "f"), (10, "f"), (11, "m"), (11, "f"), (12, "m"), (12, "f"), (13, "f"), (18, "f"), (19, "f"), (21, "f"), (22, "f"), (24, "f"), (25, "m"), (25, "f"), (29, "f"), (30, "f"), (31, "f"), } },
            { ("hc","hou"), new List<(int, string)> { } },
            { ("hc","tok"), new List<(int, string)> { (07, "m"), (07, "f"), (08, "m"), (08, "f"), (09, "m"), (22, "m"), (22, "f"), (30, "m"), } },
            { ("hc","sou"), new List<(int, string)> { } },
            { ("hc","les"), new List<(int, string)> { (02, "f1"), (03, "f1"), (04, "f1"), } },
            { ("3p","mf2"), new List<(int, string)> { (00, "f2"), (01, "m"), (04, "m"), (05, "f2"), } },
            { ("3p","m2f"), new List<(int, string)> { (01, "m1"), (04, "m2"), (05, "m2"), (06, "m2"), (07, "m2"), } },

        };

        public static Dictionary<(string, string), List<(int, string)>> dict_anal = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> { (04, "f"), (05, "f"), (06, "f"), (12, "f"), (13, "f"), (14, "f") ,(23, "f"), (24, "f"), (25, "f"), (31, "f"), (32, "f"), (33, "f")} },
            { ("sv","hou"), new List<(int, string)> {  } },
            { ("sv","som"), new List<(int, string)> { (13, "f"), (14, "f"), (15, "f"), (16, "f"), (17, "f"), (18, "f"), (28, "f"), (29, "f"), (30, "f"), (31, "f"), (32, "f") } },
            { ("sv","sof"), new List<(int, string)> { (11, "f"), (12, "f"), (13, "f"), (14, "f"), (21, "f"), (22, "f") } } ,
            { ("sv","rez"), new List<(int, string)> { } },
            { ("sv","mff"), new List<(int, string)> { } },
            { ("sv","mmf"), new List<(int, string)> { (00, "f"), (02, "f"), (03, "f"),  (06, "f"), (07, "f"), (08, "f"), } },
            { ("hc","aib"), new List<(int, string)> { (08, "f"), (09, "f"), (16, "f"), } },
            { ("hc","hou"), new List<(int, string)> { (07, "m"), } },
            { ("hc","tok"), new List<(int, string)> { } },
            { ("hc","sou"), new List<(int, string)> { (11, "f"),  (12, "f"),  (28, "f"),  (30, "f"),  (47, "f"),  (48, "f"), } },
            { ("hc","les"), new List<(int, string)> { } },
            { ("3p","mf2"), new List<(int, string)> {  } },
            { ("3p","m2f"), new List<(int, string)> {  (00, "f"), (02, "f"), (03, "f"), } },
         };

        public static Dictionary<(string, string), List<(int, string)>> dict_aibu3P = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> {  } },
            { ("sv","hou"), new List<(int, string)> {  } },
            { ("sv","som"), new List<(int, string)> { (01, "m"), (01, "f") } },
            { ("sv","sof"), new List<(int, string)> {  } },
            { ("sv","rez"), new List<(int, string)> { (00, "f1"), (00, "f2"), (01,"f1"),  (01, "f2")  } },
            { ("sv","mff"), new List<(int, string)> { (00, "m"), (00, "f2"), (02, "m"), (02, "f2"), (04, "m"), (04, "f2"), (05, "m"), (05, "f2"), (06, "m"), (06, "f2"), (07, "m"), (07, "f2")} },
            { ("sv","mmf"), new List<(int, string)> { } },
            { ("hc","aib"), new List<(int, string)> { } },
            { ("hc","hou"), new List<(int, string)> { } },
            { ("hc","tok"), new List<(int, string)> { (00, "f"), (01, "f"), (02, "f"), (03, "f"), (04, "f"), (05, "f"), (06, "f"), (07, "m"), (07, "f"), (08, "m"), (08, "f"), (09, "m"), (09, "f"), (10, "m"), (10, "f"), (11, "m"), (11, "f"), (12, "m"), (12, "f"), (13, "m"), (13, "f"), (14, "m"), (14, "f"), (15, "m"), (15, "f"), (16, "m"), (16, "f"), (22, "m"), (22, "f"), (29, "m"), (29, "f"), (30, "f"), } },
            { ("hc","sou"), new List<(int, string)> { (01, "m"), (01, "f"), (06, "m"), (06, "f"), (16, "m"), (16, "f"), (17, "m"), (17, "f"), (25, "m"), (25, "f"), (31, "m"), (31, "f"), (61, "m"), (61, "f"), } },
            { ("hc","les"), new List<(int, string)> { (00, "f1"), (00, "f2"), (01,"f1"),  (01, "f2"), (02, "f1"), (02, "f2"), (03, "f1"), (03, "f2"), (04, "f1"), (04, "f2"), } },
            { ("3p","mf2"), new List<(int, string)> {  (00, "m"), (00, "f2"), (02, "m"), (02, "f1"), (02, "f2"), (03, "m"), (03, "f2"), (05, "m"), (05, "f2"), } },
            { ("3p","m2f"), new List<(int, string)> {  } },
        };
        public static Dictionary<(string, string), List<(int, string)>> dict_hoshi3P = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> {  } },
            { ("sv","hou"), new List<(int, string)> {  } },
            { ("sv","som"), new List<(int, string)> {  } },
            { ("sv","sof"), new List<(int, string)> {  } },
            { ("sv","rez"), new List<(int, string)> {  } },
            { ("sv","mff"), new List<(int, string)> { (01, "m"), (01, "f1"), (01, "f2"), (03, "m"), (03, "f1"), (03, "f2") } },
            { ("sv","mmf"), new List<(int, string)> { (01, "m1"), (01, "m2"), (01, "f"), (04, "m2"), (04, "f"), (05, "m2"), (05, "f"), (09, "m2"), (09, "f"), (10, "m2"), (10, "f") } },
            { ("hc","aib"), new List<(int, string)> { } },
            { ("hc","hou"), new List<(int, string)> { } },
            { ("hc","tok"), new List<(int, string)> { (07, "m"), (07, "f"), (08, "m"), (08, "f"), (09, "m"), (09, "f"), (22, "m"), (22, "f"), (29, "m"), (29, "f"), (30, "m"), (30, "f"), } },
            { ("hc","sou"), new List<(int, string)> { (17, "m"), (17, "f"), } },
            { ("hc","les"), new List<(int, string)> { } },
            { ("3p","mf2"), new List<(int, string)> { (01, "m"), (01, "f1"), (01, "f2"), (04, "m"), (04, "f1"), (04, "f2") } },
            { ("3p","m2f"), new List<(int, string)> { (01, "m1"), (01, "m2"), (01, "f"), (04, "m2"), (04, "f"), (05, "m2"), (05, "f"), (06, "m2"), (06, "f"), (07, "m2"), (07, "f"), } },
        };

        public static Dictionary<(string, string), List<(int, string)>> dict_SM = new Dictionary<(string, string), List<(int, string)>>()
        {
            { ("sv","adv"), new List<(int, string)> {  } },
            { ("sv","aib"), new List<(int, string)> {  } },
            { ("sv","hou"), new List<(int, string)> {  } },
            { ("sv","som"), new List<(int, string)> {  } },
            { ("sv","sof"), new List<(int, string)> {  } },
            { ("sv","rez"), new List<(int, string)> {  } },
            { ("sv","mff"), new List<(int, string)> {  } },
            { ("sv","mmf"), new List<(int, string)> {  } },
            { ("hc","aib"), new List<(int, string)> {  } },
            { ("hc","hou"), new List<(int, string)> {  } },
            { ("hc","tok"), new List<(int, string)> { (10, "m"), (10, "f"), (11, "m"), (11, "f"), (12, "m"), (12, "f"), (13, "m"), (13, "f"), (14, "m"), (14, "f"), (15, "m"), (15, "f"), (16, "m"), (16, "f"), } },
            { ("hc","sou"), new List<(int, string)> {  } },
            { ("hc","les"), new List<(int, string)> {  } },
            { ("3p","mf2"), new List<(int, string)> {  } },
            { ("3p","m2f"), new List<(int, string)> {  } },
        };

        public bool IsAnOpponentPos(string pos1)
        {
            string pos0 = _animPos;
            if (pos0.Equals(pos1))
            {
                return false;
            }
            else
            {
                if (_animForm.Equals("rez") || _animForm.Equals("les"))
                {
                    return true;
                }
                else
                {
                    return IsAMasculinePos(pos0) && IsAFemininePos(pos1) || IsAFemininePos(pos0) && IsAMasculinePos(pos1);
                }
            }
        }

        public static bool IsAMainPos(string position)
        {
            return position.Equals("m") || position.Equals("m1") || position.Equals("f") || position.Equals("f1");
        }

        public static bool IsAMasculinePos(string position)
        {

            return position.Equals("m") || position.Equals("m1") || position.Equals("m2");
        }
        public static bool IsAFemininePos(string position)
        {

            return position.Equals("f") || position.Equals("f1") || position.Equals("f2");
        }

        public bool IsInMainPos()
        {

            return _animForm.Equals("rez") || _animForm.Equals("les") || IsAMainPos(_animPos);
        }
        public bool IsInMasculinePos()
        {

            return IsAMasculinePos(_animPos);
        }
        public bool IsInFemininePos()
        {

            return IsAFemininePos(_animPos);
        }
        public bool IsInHoshi()
        {
            int num = int.Parse(_animNum);
            return _animForm.Equals("hou") || (CustomActorController.dict_hoshi3P[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos)));
        }

        public bool IsInAibu()
        {
            int num = int.Parse(_animNum);
            return _animForm.Equals("aib") || (CustomActorController.dict_aibu3P[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos)));
        }

        public bool IsInSM()
        {
            int num = int.Parse(_animNum);
            return (CustomActorController.dict_SM[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos)));
        }

        public bool IsInSonyu()
        {

            return VirginaInserted() || AnalInserted();
        }

        public bool IsInvading()
        {

            return IsInSonyu() && IsInMasculinePos();
        }
        public bool IsInvaded()
        {
            return IsInSonyu() && IsInFemininePos();
        }

        public bool IsActive()
        {
            int num = int.Parse(_animNum);
            return CustomActorController.dict_Active[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos));

        }
        public bool IsPassive()
        {
            int num = int.Parse(_animNum);
            return CustomActorController.dict_Passive[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos));
        }

        public bool VirginaInserted()
        {
            int num = int.Parse(_animNum);
            return CustomActorController.dict_VirginaInserted[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos));
        }
        public bool AnalInserted()
        {
            int num = int.Parse(_animNum);
            return CustomActorController.dict_AnalInserted[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos));
        }

        public bool IsFacetoFace()
        {
            int num = int.Parse(_animNum);

            return CustomActorController.dict_FacetoFace[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos));
        }

        public bool IsBacktoFace()
        {

            int num = int.Parse(_animNum);
            return CustomActorController.dict_FacetoBack[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos));
        }
        public bool IsKuchi()
        {
            int num = int.Parse(_animNum);

            return CustomActorController.dict_kuchi[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos));
        }
        public bool IsBeKuchi()
        {
            int num = int.Parse(_animNum);

            return CustomActorController.dict_kuchied[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos));
        }
        public bool IsAnal()
        {
            int num = int.Parse(_animNum);

            return CustomActorController.dict_anal[(_animOrigin, _animForm)].Exists(x => x == (num, _animPos));
        }
        public bool IsLick()
        {

            return IsKuchi() && (_hActor.WordPlayer.General._isLick || _hActor.WordPlayer.General._lesbianPostureType == SV.H.Words.FlagManager.LesbianPostureType.Sixnine || _hActor.WordPlayer.General._serviceType == SV.H.Words.FlagManager.ServiceType.Lick || _hActor.WordPlayer.General._caressType == SV.H.Words.FlagManager.CaressType.VaginaLick || _hActor.WordPlayer.General._caressType == SV.H.Words.FlagManager.CaressType.AnalLick || _hActor.WordPlayer.General._threesomeType == SV.H.Words.FlagManager.ThreesomeType.LickAndInsert);
        }
        public bool IsSuckOrThroat()
        {

            return IsKuchi() && (_hActor.WordPlayer.General._isSuck || _hActor.WordPlayer.General._serviceType == SV.H.Words.FlagManager.ServiceType.Suck || _hActor.WordPlayer.General._serviceType == SV.H.Words.FlagManager.ServiceType.Throat || _hActor.WordPlayer.General._threesomeType == SV.H.Words.FlagManager.ThreesomeType.SuckAndInsert);
        }
        #endregion


        #region AnimState
        public static List<string> WeakAnim = new List<string>() { "WLoop", "WSPLoop", "OrgasmF", "OrgasmF_ST", "OrgasmM_IN", "OrgasmM_IN_ST", "OrgasmM_OUT", "OrgasmM_OUT_ST", "OrgasmS_IN", "OrgasmS_ST", "Orgasm_IN_A", "OrgasmM_OUT_A", "Orgasm_A", "Pull", "Drop" };
        public static List<string> StrongAnim = new List<string>() { "SLoop", "SSPLoop", "BOrgasmF", "BOrgasmF_ST", "BOrgasmM_IN", "BOrgasmM_IN_ST", "BOrgasmM_OUT", "BOrgasmM_OUT_ST", "BOrgasmS_IN", "BOrgasmS_ST", "BOrgasm_IN_A", "BOrgasmM_OUT_A", "BPull", "BDrop" };
        public static List<string> OLoopAnim = new List<string>() { "SSPLoop", "WSPLoop" };
        public static List<string> OrgasmMAnim = new List<string>() { "OrgasmM_IN", "OrgasmM_IN_ST", "OrgasmM_OUT", "OrgasmM_OUT_ST", "OrgasmS_IN", "OrgasmS_ST", "BOrgasmM_IN", "BOrgasmM_IN_ST", "BOrgasmM_OUT", "BOrgasmM_OUT_ST", "BOrgasmS_IN", "BOrgasmS_ST" };
        public static List<string> OrgasmFAnim = new List<string>() { "OrgasmF", "OrgasmF_ST", "OrgasmS_IN", "OrgasmS_ST", "BOrgasmF", "BOrgasmF_ST", "BOrgasmS_IN", "BOrgasmS_ST" };
        public static List<string> OrgasmSAnim = new List<string>() { "OrgasmS_IN", "OrgasmS_ST", "BOrgasmS_IN", "BOrgasmS_ST" };
        public static List<string> StartOrgasmAnim = new List<string>() { "OrgasmM_IN_ST", "OrgasmM_OUT_ST", "OrgasmF_ST", "OrgasmS_ST", "BOrgasmM_IN_ST", "BOrgasmM_OUT_ST", "BOrgasmF_ST", "BOrgasmS_ST" };
        public static List<string> CumINAnim = new List<string>() { "OrgasmM_IN", "OrgasmM_IN_ST", "OrgasmS_IN", "OrgasmS_ST", "BOrgasmM_IN", "BOrgasmM_IN_ST", "BOrgasmS_IN", "BOrgasmS_ST" };
        public static List<string> Orgasm_IN_AAnim = new List<string>() { "Orgasm_IN_A", "BOrgasm_IN_A" };
        public static List<string> Orgasm_AAnim = new List<string>() { "Orgasm_IN_A", "BOrgasm_IN_A", "OrgasmM_OUT_A", "BOrgasmM_OUT_A", "Orgasm_A", };
        public static List<string> PullAnim = new List<string>() { "Pull", "BPull" };
        public static List<string> DropAnim = new List<string>() { "Drop", "BDrop" };
        public static List<string> IdleAnim = new List<string>() { "Idle", "D_Idle", "InsertIdle" };
        public static List<string> InsertAnim = new List<string>() { "Insert" };
        public static List<string> NOTInsideAnim = new List<string>() { "Idle", "D_Idle", "Drop", "BDrop", "OrgasmM_OUT", "OrgasmM_OUT_ST", "OrgasmM_OUT_A", "BOrgasmM_OUT", "BOrgasmM_OUT_ST", "BOrgasmM_OUT_A", "Orgasm_A" };
        public bool AnimIsWeak()
        {

            bool result = false;
            result = WeakAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool AnimIsStrong()
        {

            bool result = false;
            result = StrongAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool AnimIsOLoop()
        {

            bool result = false;
            result = OLoopAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool AnimIsOrgasmM()
        {

            bool result = false;
            result = OrgasmMAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool AnimIsOrgasmF()
        {

            bool result = false;
            result = OrgasmFAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }

        public bool AnimIsOrgasmS()
        {

            bool result = false;
            result = OrgasmSAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }

        public bool AnimIsStartOrgasm()
        {

            bool result = false;
            result = StartOrgasmAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool AnimIsCumIN()
        {

            bool result = false;
            result = CumINAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool AnimIsOrgasm_IN_A()
        {

            bool result = false;
            result = Orgasm_IN_AAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }

        public bool AnimIsOrgasm_A()
        {

            bool result = false;
            result = Orgasm_AAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool AnimIsPull()
        {

            bool result = false;
            result = PullAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool AnimIsDrop()
        {

            bool result = false;
            result = DropAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool AnimIsIdle()
        {

            bool result = false;
            result = IdleAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool AnimIsInsert()
        {

            bool result = false;
            result = InsertAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }

        public bool IsNOTInside()
        {
            bool result = false;
            result = NOTInsideAnim.Exists(x => _currAnimStateInfo.IsName(x));
            return result;
        }
        public bool IsStrongOrWeakness()
        {
            return (AnimIsStrong() || this.IsWeakness());
        }
        public bool IsWeakAndNotWeakness()
        {
            return (AnimIsWeak() && !this.IsWeakness());
        }

        public bool IsWeakness()
        {
            return _hActor.hStatus.IsWeakness;
        }

        public bool IsNowOrgasm()
        {
            bool result = false;
            result = this.AnimIsOrgasmM() && this.IsInMasculinePos() || this.AnimIsOrgasmF() && this.IsInFemininePos();
            return result;
        }
        public bool IsNowStartOrgasm()
        {
            bool result = false;
            result = this.IsNowOrgasm() && this.AnimIsStartOrgasm();
            return result;
        }
        public bool IsNowOrgasming()
        {
            bool result = false;
            result = this.IsNowOrgasm() && !this.AnimIsStartOrgasm();
            return result;
        }
        public bool IsSpeaking()
        {

            return _hActor.WordPlayer.IsPlaying(1);
        }
        public bool IsInVLPain()
        {
            bool result = false;
            if (this.VirginaInserted() && this.IsInvaded() && !this.IsNOTInside())
            {

                if (_hActor.hStatus._virginData[0].IsLost)
                    result = true;
            }
            if (this.AnalInserted() && this.IsInvaded() && !this.IsNOTInside())
            {
                if (_hActor.hStatus._virginData[1].IsLost)
                    result = true;

            }
            if (this.NumRudes() > 0 && this.IsInvaded())
            {
                result = true;

            }
            return result;
        }
        #endregion
        public void Test()
        {
            if (_animator.runtimeAnimatorController != null)
            {
                Util.SaveLog($"isSonyu:{IsInSonyu()}");
                Util.SaveLog($"isAibu:{IsInAibu()}");
                Util.SaveLog($"isHoshi:{IsInHoshi()}");
                Util.SaveLog($"isinMasculinePos:{IsInMasculinePos()}");
                Util.SaveLog($"IsInFemininePos:{IsInFemininePos()}");
                Util.SaveLog($"IsInvading:{IsInvading()}");
                Util.SaveLog($"IsInvaded:{IsInvaded()}");
                Util.SaveLog($"IsActive:{IsActive()}");
                Util.SaveLog($"IsPassive:{IsPassive()}");
                Util.SaveLog($"VirginaInserted:{VirginaInserted()}");
                Util.SaveLog($"AnalInserted:{AnalInserted()}");
                Util.SaveLog($"IsFacetoFace:{IsFacetoFace()}");
                Util.SaveLog($"IsBacktoFace:{IsBacktoFace()}");
                Util.SaveLog($"IsKuchi:{IsKuchi()}");
                Util.SaveLog($"IsAnal:{IsAnal()}");
            }
        }



        public int NumRudes()
        {
            int num = 0;

            if (this.IsInvaded() && !this.IsNOTInside())
            {
                var playerlist = _gameController._playerList;

                num = playerlist.Where(player1 => (player1._actorController.IsInvading() && player1._actorController.IsAnOpponentPos(this._animPos)))
                    .Where(player1 => (player1._actorController.PhysicalLv == (int)PropertyLevel.HIGH || GetInitialAffinityScore(player1) < 0) && GetAffinityLv(this._charaId, player1._charaId) < (int)AffinityLevel.良好)
                    .Count();

            }

            return num;
        }


        public bool IsRudeV(Player player1)
        {

            if (this.IsInvaded() && this.VirginaInserted() && !this.IsNOTInside())
            {
                if (player1._actorController.IsInvading() && player1._actorController.VirginaInserted() && player1._actorController.IsAnOpponentPos(this._animPos))
                {
                    if ((player1._actorController.PhysicalLv == (int)PropertyLevel.HIGH || GetInitialAffinityScore(player1) < 0) && GetAffinityLv(this._charaId, player1._charaId) < (int)AffinityLevel.良好)
                        return true;
                }

            }

            return false;
        }
        public bool IsRudeA(Player player1)
        {

            if (this.IsInvaded() && this.AnalInserted() && !this.IsNOTInside())
            {
                if (player1._actorController.IsInvading() && player1._actorController.AnalInserted() && player1._actorController.IsAnOpponentPos(this._animPos))
                {
                    if ((player1._actorController.PhysicalLv == (int)PropertyLevel.HIGH || GetInitialAffinityScore(player1) < 0) && GetAffinityLv(this._charaId, player1._charaId) < (int)AffinityLevel.良好)
                        return true;
                }

            }

            return false;
        }
        public int CalcMaximumInvaderPower()
        {
            int power = 0;

            if (this.IsInvaded() && !this.IsNOTInside())
            {
                var playerlist = _gameController._playerList;
                power = playerlist.Where(player1 => (player1._actorController.IsInvading() && player1._actorController.IsAnOpponentPos(this._animPos))).Select(player1 => player1._actorController.PhysicalLv).Max();

            }

            return power;
        }

        public int CalcMaximalInvaderAffinity()
        {
            int power = 0;

            if (this.IsInvaded() && !this.IsNOTInside())
            {
                var playerlist = _gameController._playerList;
                power = playerlist.Where(player1 => (player1._actorController.IsInvading() && player1._actorController.IsAnOpponentPos(this._animPos))).Select(player1 => GetAffinityLv(this._charaId, player1._charaId)).Max();

            }

            return power;
        }
        public int ChastityLv => _hActor.Data.GameParameter.LvChastity;
        public int PhysicalLv => _hActor.Data.GameParameter.LvPhysical;//0~4

        public int StaminaLv => _Charas[_charaId].charasGameParam.baseParameter.StaminaLV;//1~10
        public int StudyLv => _Charas[_charaId].charasGameParam.baseParameter.StudyLV;//1~10
        public int LivingLv => _Charas[_charaId].charasGameParam.baseParameter.LivingLV;//1~10

        public int ConversationLv => _Charas[_charaId].charasGameParam.baseParameter.ConversationLV;//1~10

        public int GetHeightKind()
        {
            return _Charas[_charaId].charFile.Custom.GetHeightKind();
        }



        #region DPS


        public float CalcDPSRate1()
        {
            // HScene hScene = _gameController._hScene;
            var playerlist = _gameController._playerList;
            var mainopponent = playerlist.First(p => this._thisplayer.IsMainOpponent(p));
            float rate0 = _hActor._gauge._getRate.Invoke();
            float ratewheel = this.CalcKaikanRateWL();
            float rateaffinity = (this.IsInvaded() && mainopponent._actorController.IsInvading()) ? Mathf.Clamp(GetFinalAffinityScore(this._charaId, mainopponent._charaId) * 0.5f, 0f, 2f) : 1f;
            float ratefavo = CalcKaikanRateFavro();
            float rateori = this.CalcKaikanRateOri();
            float ratehp = this.CalcKaikanRatePH();
            float ratews = this.CalcKaikanRateWS();
            float ratelewdness = this.IsLewdness() ? _LewdnessFactor : 1f;
            float ratecondom = this.CalcKaikanCondom();
            float ratedangerous = 1f;
            if (this.IsInDangerousDay() && this.VirginaInserted() && this.IsInvaded())
            {
                var vinvader = playerlist.First(p => this._thisplayer.IsAnOpponent(p) && p._actorController.VirginaInserted() && p._actorController.IsInvading());
                if (vinvader != null && !vinvader._hActor.IsCondom() && (vinvader._actorController.PhysicalLv >= (int)PropertyLevel.MiddleHigh || GetAffinityLv(this._charaId, vinvader._charaId) >= (int)AffinityLevel.最高))
                {
                    ratedangerous = _DangerousMultiplier;
                }


            }
            float rate = ratewheel * rateaffinity * ratefavo * rateori * ratehp * ratews * ratelewdness * ratecondom * ratedangerous;
            //Util.SaveLog($"{_index}:{rate};{rate0},{ratewheel},{rateaffinity},{ratehp},{ratews},{ratecondom}");
            return rate;
        }

        public enum Individuality : int
        {
            Null = -1,
            チョロイ = 0,
            熱血友情,
            男性苦手,
            女性苦手,
            チャーム,
            侠気,
            ミーハー,
            素直,
            前向き,
            照れ屋,
            ヤキモチ,
            豆腐精神,
            スケベ,
            真面目,
            平常心,
            神経質,
            直情的,
            ぽややん,
            短気,
            肉食系,
            草食系,
            世話焼き,
            まとめ役,
            筋肉愛,
            お喋り,
            ハラペコ,
            勤勉,
            恋愛脳,
            一方的,
            一途,
            優柔不断,
            腹黒,
            世渡り上手,
            勤労,
            奔放,
            M気質,
            心の闇,
            鈍感,
            節穴,
            強運
        }

        public enum PreferenceH2Int : int
        {
            Unknow = -100,
            Null = -1,
            DefenceSuki = 0,
            OffenceSuki = 1,
            AibuSuki = 2,
            HoshiSuki = 3,
            SkilledMouth = 4,
            AnalSuki = 5,
            FacetoFaceSuki = 6,
            FacetoBackSuki = 7,
            CuminSuki = 8,
            CumoutSuki = 9,
            CuminmouthSuki = 10
        }
        public enum SexualOrientation : int
        {
            HETRO = 0,
            HetroLean = 1,
            BI = 2,
            HomoLean = 3,
            HOMO = 4
        }

        public enum PropertyLevel : int
        {
            LOW = 0,
            MiddleLow = 1,
            MEDIUM = 2,
            MiddleHigh = 3,
            HIGH = 4
        }

        public enum AffinityLevel : int
        {

            不具合 = 0,
            普通 = 1,
            良好 = 2,
            最高 = 3,
            完璧 = 4
        }

        public enum HeightKind : int
        {
            LOW = 0,
            MEDIUM = 1,
            HIGH = 2,
        }
        /*
        public enum Sensitivity : int
        {

            LOVE,

            FRIEND,

            INDIFFERENT,

            DISLIKE,

            MAX
        }
        
        public enum StateKind
        {
            // Token: 0x040020DF RID: 8415
            UPLIFT,
            // Token: 0x040020E0 RID: 8416
            SHYNESS,
            // Token: 0x040020E1 RID: 8417
            JEALOUSY,
            // Token: 0x040020E2 RID: 8418
            ANGER,
            // Token: 0x040020E3 RID: 8419
            DISAPPOINTMENT,
            // Token: 0x040020E4 RID: 8420
            PEACEOFMIND,
            // Token: 0x040020E5 RID: 8421
            RUT,
            // Token: 0x040020E6 RID: 8422
            EARNESTNESS,
            // Token: 0x040020E7 RID: 8423
            TENSION,
            // Token: 0x040020E8 RID: 8424
            NORMAL,
            // Token: 0x040020E9 RID: 8425
            TOTAL
        }
        public enum Rank : int
        {

            NON = -1,

            LOW,

            MIDDLE,

            HIGH,

            MAX
        }*/
        public float CalcKaikanRateWL()
        {
            HScene hScene = _gameController._hScene;
            return Mathf.Clamp(hScene.animeSpeeder.Rate, 0.1f, 1f);
        }

        protected static float _WeakFactorMasculine = 1f;
        protected static float _StrongFactorMasculine = 1.2f;
        protected static float _WeakFactorFeminine = 1f;
        protected static float _StrongFactorFeminine = 1.6f;
        protected static float _PreferenceHFactor = 0.2f;
        protected static float _LewdnessFactor = 1.5f;
        protected static float _CondomMultiplier = 0.7f;
        protected static float _DangerousMultiplier = 1.2f;
        protected static bool _VirginLostAffectsGauges = true;
        public float CalcKaikanRatePH()
        {
            float rateph = 0f;
            if (_hActor != null && _hActor.HasAnimator && _hActor.Animator.runtimeAnimatorController != null)
            {
                var playerlist = _gameController._playerList;
                foreach (var player1 in playerlist)
                {
                    if (player1._actorController != null && this._index != player1._index && player1._actorController.IsAnOpponentPos(this._animPos))
                    {
                        if (player1._hActor.IsPreferenceH((int)PreferenceH2Int.DefenceSuki))
                        {
                            if (player1._actorController.IsInvaded() && this.IsInvading())
                            {
                                rateph += _PreferenceHFactor;
                            }
                        }
                        if (player1._hActor.IsPreferenceH((int)PreferenceH2Int.OffenceSuki))
                        {
                            if (player1._actorController.IsInvading() && this.IsInvaded())
                            {
                                rateph += _PreferenceHFactor;
                            }
                        }
                        if (player1._hActor.IsPreferenceH((int)PreferenceH2Int.SkilledMouth) && !player1._hActor.IsDislike)
                        {
                            if (player1._actorController.IsKuchi())
                            {
                                rateph += _PreferenceHFactor;
                            }
                        }
                    }
                }
                if (_hActor.IsPreferenceH((int)PreferenceH2Int.DefenceSuki) && this.IsPassive())
                {

                    rateph += _PreferenceHFactor;
                }
                if (_hActor.IsPreferenceH((int)PreferenceH2Int.OffenceSuki) && this.IsActive())
                {

                    rateph += _PreferenceHFactor;
                }


                if (_hActor.IsPreferenceH((int)PreferenceH2Int.AibuSuki) && this.IsInAibu())
                {
                    rateph += _PreferenceHFactor;
                }
                if (_hActor.IsPreferenceH((int)PreferenceH2Int.HoshiSuki) && this.IsInHoshi())
                {
                    rateph += _PreferenceHFactor;
                }
                if (_hActor.IsPreferenceH((int)PreferenceH2Int.AnalSuki) && this.IsAnal())
                {

                    rateph += _PreferenceHFactor;
                }
                if (_hActor.IsPreferenceH((int)PreferenceH2Int.FacetoFaceSuki) && this.IsFacetoFace())
                {

                    rateph += _PreferenceHFactor;
                }
                if (_hActor.IsPreferenceH((int)PreferenceH2Int.FacetoBackSuki) && this.IsBacktoFace())
                {

                    rateph += _PreferenceHFactor;
                }
                if (_hActor.IsPreferenceH((int)PreferenceH2Int.CuminSuki) && this.IsInSonyu() && (this.IsInvading() && !_hActor.IsCondom() || this.IsInvaded() && playerlist.Exists(x => (x._actorController.IsInvading() && x._actorController.IsInSonyu() && !x._hActor.IsCondom() && x._actorController.IsAnOpponentPos(this._animPos)))))
                {

                    rateph += _PreferenceHFactor;
                }
                if (_hActor.IsPreferenceH((int)PreferenceH2Int.CumoutSuki) && (this.IsInSonyu() || this.IsInHoshi()))
                {

                    rateph += _PreferenceHFactor;
                }
                if (_hActor.IsPreferenceH((int)PreferenceH2Int.CuminmouthSuki) && this.IsInHoshi() && (this.IsSuckOrThroat() || this.IsBeKuchi() && playerlist.Exists(x => (x._actorController.IsInHoshi() && x._actorController.IsSuckOrThroat() && x._actorController.IsAnOpponentPos(this._animPos)))))
                {

                    rateph += _PreferenceHFactor;
                }

                float ratewheel = CalcKaikanRateWL();
                float wetness = _hActor.Human.fileStatus.wetRate;
                if (_VirginLostAffectsGauges)
                {
                    int numrudev = playerlist.Where(player1 => this.IsRudeV(player1)).Count();
                    int numrudea = playerlist.Where(player1 => this.IsRudeA(player1)).Count();
                    int numrudes = NumRudes();
                    if (this.VirginaInserted() && this.IsInvaded())
                    {
                        if (_hActor.hStatus._virginData[0].IsLost)
                            rateph -= 1f * ratewheel * (1 - wetness) * (numrudev > 0 ? 2 : 1);
                    }
                    if (this.AnalInserted() && this.IsInvaded())
                    {
                        if (_hActor.hStatus._virginData[1].IsLost)
                            rateph -= 0.5f * ratewheel * (1 - wetness) * (numrudea > 0 ? 2 : 1);

                    }
                    if (numrudes > 0 && this.IsInvaded())
                    {
                        rateph -= 0.5f * numrudes * ratewheel * (1 - wetness);

                    }
                }
                var individuality = this._hActor.Human.fileGameParam.individuality.answer.ToList();
                if (IsInSM())
                {
                    if (IsPassive())
                    {
                        if (individuality.Contains((int)Individuality.M気質))
                        {
                            rateph += _PreferenceHFactor;
                        }
                        else
                        {
                            rateph -= 1f * ratewheel * (1 - wetness);
                        }
                    }
                    else if (IsActive())
                    {
                        if (individuality.Contains((int)Individuality.腹黒))
                        {
                            rateph += _PreferenceHFactor;
                        }
                    }
                }
            }
            rateph += 1f;
            rateph = Mathf.Clamp(rateph, 0f, 2f);
            return rateph;
        }

        public float CalcKaikanRateWS()
        {

            float ratews = 1f;

            if (this.AnimIsWeak() && !this.IsWeakness() && this.IsInMasculinePos())
            {
                ratews = _WeakFactorMasculine;
            }
            if ((this.AnimIsStrong() || this.IsWeakness()) && this.IsInMasculinePos())
            {
                ratews = _StrongFactorMasculine;
            }
            if (this.AnimIsWeak() && !this.IsWeakness() && this.IsInFemininePos())
            {
                ratews = _WeakFactorFeminine;
            }
            if ((this.AnimIsStrong() || this.IsWeakness()) && this.IsInFemininePos())
            {
                ratews = _StrongFactorFeminine;
            }


            return ratews;
        }
        public float CalcKaikanRateFavro()
        {
            float ratefavo = 100;
            var playerlist = _gameController._playerList;
            var mainopponent = playerlist.First(p => this._thisplayer.IsMainOpponent(p));
            var oppoId = mainopponent._charaId;
            var charaId = _charaId;
            var individuality = this._hActor.Human.fileGameParam.individuality.answer.ToList();
            // int love01 = GetFavorability(charaId, oppoId).feelings[(int)Sensitivity.LOVE].ToDecimal();
            // int indifferent01 = GetFavorability(charaId, oppoId).feelings[(int)Sensitivity.INDIFFERENT].ToDecimal();
            //  int dislike01 = GetFavorability(charaId, oppoId).feelings[(int)Sensitivity.DISLIKE].ToDecimal();
            var loveRank01 = (int)GetFavorabilityRank(charaId, oppoId, Sensitivity.LOVE);
            var friendRank01 = (int)GetFavorabilityRank(charaId, oppoId, Sensitivity.FRIEND);
            var indifferentRank01 = (int)GetFavorabilityRank(charaId, oppoId, Sensitivity.INDIFFERENT);
            var dislikeRank01 = (int)GetFavorabilityRank(charaId, oppoId, Sensitivity.DISLIKE);

            if (loveRank01 >= (int)Rank.MIDDLE)
            {
                ratefavo += 10;
                if (loveRank01 >= (int)Rank.HIGH)
                {
                    ratefavo += 20;
                    if (HasMaximalFeelingOn(charaId, oppoId, Sensitivity.LOVE))
                    {
                        ratefavo += 20;
                    }
                }
            }
            if (friendRank01 >= (int)Rank.HIGH)
            {
                ratefavo += 10;
            }
            if (indifferentRank01 >= (int)Rank.HIGH)
            {
                ratefavo -= 20;
            }
            if (dislikeRank01 >= (int)Rank.MIDDLE)
            {
                if (this.IsRaper() || this.IsRapee())
                {
                    ratefavo += 10;
                    if (dislikeRank01 >= (int)Rank.HIGH)
                    {
                        ratefavo += 20;
                    }
                }
                else
                {
                    ratefavo -= 10;
                    if (dislikeRank01 >= (int)Rank.HIGH)
                    {
                        ratefavo -= 20;
                    }
                }
            }

            ratefavo = ratefavo / 100;
            return ratefavo;
        }

        public float CalcKaikanRateOri()
        {

            float rateorient = 1f;
            var playerlist = _gameController._playerList;
            var mainopponent = playerlist.First(p => this._thisplayer.IsMainOpponent(p));
            var oppoId = mainopponent._charaId;
            var charaId = _charaId;
            int palatable = IsPalatableSex(mainopponent);
            if (palatable == 0)
            {
                rateorient -= 0.5f;
            }

            return rateorient;
        }
        public float CalcKaikanCondom()
        {

            float ratecondom = 1f;

            var playerlist = _gameController._playerList;
            if (this.IsInSonyu() && (this.IsInvading() && _hActor.IsCondom() || this.IsInvaded() && playerlist.Where(x => (x._actorController.IsInvading() && x._actorController.IsInSonyu() && x._actorController.IsAnOpponentPos(this._animPos))).All(x => x._hActor.IsCondom())))
            {
                ratecondom = _CondomMultiplier;
            }



            return ratecondom;
        }

        protected float CalcWetness()
        {
            float wetness;
            var kaikan = _hActor.GaugeValue;
            wetness = Mathf.Clamp(kaikan - 20, 0, 100) / 100;
            if (IsNowOrgasm() || (kaikan == 0 && AnimIsOrgasm_IN_A()))
            {
                wetness = IsStrongOrWeakness() ? 100 : 80;
                wetness = wetness / 100;
            }



            return wetness;
        }


        public bool IsLewdness()
        {
            return _hActor.IsLewdness;
        }
        public bool IsMasochism()
        {
            return _hActor.IsMasochism();
        }



        public bool IsBlackHearted()
        {
            return _hActor.IsBlackHearted();
        }

        public bool IsDarknessOfMind()
        {
            return _hActor.IsDarknessOfMind();
        }

        public bool IsLetMeHold()
        {
            return _hActor.IsLetMeHold;
        }

        public bool IsLetMeHoldReceive()
        {
            return _hActor.IsLetMeHoldReceive;
        }

        public bool IsDislike()
        {
            return _hActor.IsDislike;
        }
        public bool IsReallyDislike()
        {
            return (IsDislike() || IsLetMeHoldReceive()) && !IsLewdness();
        }

        public bool IsRaper()
        {
            return _Charas[_charaId].charasGameParam.isHActiveRape;
        }

        public bool IsRapee()
        {
            return _Charas[_charaId].charasGameParam.isHPassiveRape;
        }
        public int GetSex()
        {
            return _hActor.Human.sex;
        }
        #endregion

        internal void TestSkin()
        {
            float wetrate = 1f;
            byte tearlv = 3;
            float nipstand = 1f;
            if (_hActor.Status.tearsLv < tearlv)
            {
                _hActor.Status.tearsLv = tearlv;
            }
            if (_hActor.Status.hohoAkaRate < wetrate)
            {
                _hActor.Status.hohoAkaRate = wetrate;

            }
            if (_hActor.Status.wetRate < wetrate)
            {
                _hActor.Status.wetRate = wetrate;
                _hActor.Human.ChangeWet(wetrate);
            }
            if (_hActor.Status.sweatRate < wetrate)
            {
                _hActor.Status.sweatRate = wetrate;
                _hActor.Human.ChangeSweat(wetrate);
            }

            if (_hActor.Status.nipStandRate < nipstand)
            {
                _hActor.Human.body.ChangeNipRate(nipstand);
                _hActor.Status.nipStandRate = nipstand;


            }
            SetMatFloat(_hActor.Human.face.customMatFace, "_HohoakaIntensity", wetrate);
            //  SetMatFloat(_hActor.Human.face.customMatFace, "_NureIntensity", wetrate);
            // SetMatFloat(_hActor.Human.face.customMatFace, "_AseIntensity", wetrate);
            SetMatFloat(_hActor.Human.face.customMatFace, "_sppower", wetrate);

            SetMatFloat(_hActor.Human.body.customMatBody, "_Skin_red_power", wetrate);
            SetMatFloat(_hActor.Human.body.customMatBody, "_High_light_power", wetrate);
            SetMatFloat(_hActor.Human.body.customMatBody, "_Highlight_power", wetrate);
            //  SetMatFloat(_hActor.Human.body.customMatBody, "_NureIntensity", wetrate);
            //  SetMatFloat(_hActor.Human.body.customMatBody, "_AseIntensity", wetrate);           

        }
        protected void SetMatFloat(Material mat, string propertyname, float value, bool updateonlyifgreater = true)
        {
            if (!mat.HasFloat(propertyname))
            {
                return;
            }
            if (updateonlyifgreater && value <= mat.GetFloat(propertyname))
            {
                return;
            }
            mat.SetFloat(propertyname, value);

        }
        #region SaveDatas

        public class Trigesimal : nbaseNumber
        {
            public Trigesimal()
            {
                this.n = 30;

                this.digits = new List<int>() { 0 };
            }

            public Trigesimal(int decimalNumber)
            { this.n = 30; this.FromDecimal(decimalNumber); }
            public Trigesimal(List<int> digits)
            { this.n = 30; this.digits = digits.ToList(); }


        }
        public class nbaseNumber
        {

            public int n = 10;

            public List<int> digits = new List<int>() { 0 };

            public nbaseNumber()
            { }
            public nbaseNumber(int n)
            { this.n = n; }

            public List<int> FromDecimal(int decimalNumber)
            {
                digits = DecimalTobasenNumber(decimalNumber, n);
                return digits;
            }

            public int ToDecimal()
            {
                int decimalNumber = basenNumberToDecimal(digits, n);
                return decimalNumber;
            }
            public static List<int> DecimalTobasenNumber(int decimalNumber, int n = 10)
            {

                List<int> basenDigits = new List<int>();

                while (decimalNumber > 0)
                {
                    basenDigits.Add(decimalNumber % n);
                    decimalNumber /= n;
                }


                return basenDigits.Count > 0 ? basenDigits : new List<int> { 0 };
            }

            public static int basenNumberToDecimal(List<int> basenDigits, int n = 10)
            {
                int decimalNumber = 0;

                for (int i = 0; i < basenDigits.Count; i++)
                {
                    decimalNumber += basenDigits[i] * (int)System.Math.Pow(n, i);
                }

                return decimalNumber;
            }

            public override bool Equals(object obj)
            {
                return obj is nbaseNumber && this.Equals(obj as nbaseNumber);
            }
            public bool Equals(nbaseNumber obj)
            {
                return this == obj;
            }

            public static bool operator ==(nbaseNumber x, nbaseNumber y)
            {
                return x.ToDecimal() == y.ToDecimal();
            }

            public static bool operator !=(nbaseNumber x, nbaseNumber y)
            {
                return x.ToDecimal() != y.ToDecimal();
            }

            public static bool operator <=(nbaseNumber x, nbaseNumber y)
            {
                return x.ToDecimal() <= y.ToDecimal();
            }

            public static bool operator >=(nbaseNumber x, nbaseNumber y)
            {
                return x.ToDecimal() >= y.ToDecimal();
            }

            public static bool operator <(nbaseNumber x, nbaseNumber y)
            {
                return x.ToDecimal() < y.ToDecimal();
            }

            public static bool operator >(nbaseNumber x, nbaseNumber y)
            {
                return x.ToDecimal() > y.ToDecimal();
            }

            public static nbaseNumber operator +(nbaseNumber x, nbaseNumber y)
            {
                nbaseNumber result = new nbaseNumber(x.n);
                result.FromDecimal(x.ToDecimal() + y.ToDecimal());
                return result;
            }
            public static nbaseNumber operator -(nbaseNumber x, nbaseNumber y)
            {
                nbaseNumber result = new nbaseNumber(x.n);
                result.FromDecimal(x.ToDecimal() - y.ToDecimal());
                return result;
            }
            public static nbaseNumber operator *(nbaseNumber x, nbaseNumber y)
            {
                nbaseNumber result = new nbaseNumber(x.n);
                result.FromDecimal(x.ToDecimal() * y.ToDecimal());
                return result;
            }
            public static nbaseNumber operator /(nbaseNumber x, nbaseNumber y)
            {
                nbaseNumber result = new nbaseNumber(x.n);
                result.FromDecimal(x.ToDecimal() / y.ToDecimal());
                return result;
            }

        }



        public class Favorability
        {
            public List<Trigesimal> feelings;

            public Favorability()
            {
                feelings = new List<Trigesimal>
                {
                    new Trigesimal(),
                    new Trigesimal(),
                    new Trigesimal(),
                    new Trigesimal()
                };
            }

            public Favorability(List<int> shortstock, List<int> longcount)
            {
                feelings = new Trigesimal[4].ToList();
                if (!(longcount != null && shortstock != null && longcount.Count == 4 && shortstock.Count == 4))
                {
                    $"Incorrect input to create Favorability".ToLog();
                    return;

                }
                for (int i = 0; i < 4; i++)
                {
                    List<int> digits = new List<int>() { shortstock[i], longcount[i] };
                    this.feelings[i] = new Trigesimal(digits);
                }

            }
            public void AddFavo(int amount, Sensitivity kind)
            {

                this.feelings[(int)kind].digits[0] += amount;
                this.feelings[(int)kind].FromDecimal(System.Math.Max(this.feelings[(int)kind].ToDecimal(), 0));
            }

        }




        protected IntPtr GetCharacter()
        {
            return _hActor.Actor.ToPtr();
        }

        public string GetCharFileDataId()
        {
            return _hActor.Actor.charFile.About.dataID;
        }
        public int GetCharaId()
        {
            return this._hActor.Actor.charasGameParam.Index;
            var charas = _Charas.ToDict();

            if (charas.Any(pair => pair.Value.charFile.About.dataID == this.GetCharFileDataId()))
            {
                return charas.FirstOrDefault(pair => pair.Value.charFile.About.dataID == this.GetCharFileDataId()).Key;
            }
            else
            {
                return -1;
            }
        }
        public static bool AddEmotion(int charaId0, List<StateKind> newemotion)
        {
            var state = _Charas[charaId0].charasGameParam.state;
            if (state == null)
                return false;
            var shorts = state.shorts;
            if (shorts == null)
                return false;
            var longs = state.longs;
            var trashs = state.trashs;
            List<StateKind> tempshorts = new List<StateKind>();
            lock (shorts)
            {
                lock (state)
                {
                    foreach (var emo in newemotion)
                    {
                        shorts.Add(emo);
                    }
                    int m = shorts.Count - 10;
                    if (m > 0)
                    {
                        tempshorts = shorts.GetRange(0, m).ToList();
                        foreach (var emo in tempshorts)
                        {
                            for (int k = 0; k < 10; k++)
                            {
                                trashs.Add(emo);
                            }
                        }
                        shorts.RemoveRange(0, m);
                    }
                    shorts._size = shorts.Count;
                    //int t = state.trashs.Count - 10;

                    if (trashs.Count >= 100)
                    {
                        state.LongStockCalc();
                    }
                    state.State = state.CalcNowState();

                }
            }

            return true;
        }
        public bool AddFavorability(int charaId0, int charaId1, Favorability additionalfavrobility)
        {
            var sensitivity = _Charas[charaId0].charasGameParam.sensitivity;
            if (sensitivity == null)
                return false;
            var tablef = sensitivity.GetInfoFromID(charaId1);
            if (tablef == null)
                return false;
            var shorts = tablef.shortStocks.ToList();
            if (shorts == null || shorts.Count != 4)
                return false;
            List<int> cnum = new int[4].ToList();
            int n = additionalfavrobility.feelings[0].n;
            lock (tablef)
            {
                lock (sensitivity)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        tablef.shortStocks[i] += additionalfavrobility.feelings[i].ToDecimal();
                        cnum[i] = tablef.shortStocks[i] / n;
                        tablef.shortStocks[i] -= cnum[i] * n;
                        for (int k = 0; k < cnum[i]; k++)
                        {
                            tablef.longStocks.Add(i);
                        }
                    }

                    int m = tablef.longStocks.Count - 30;
                    if (m > 0)
                    {
                        tablef.longStocks.RemoveRange(0, m);
                    }
                    tablef.longStocks._size = tablef.longStocks.Count;
                    for (int i = 0; i < 4; i++)
                    {
                        tablef.longSensitivityCounts[i] = tablef.longStocks.ToList().Count(x => x == i);
                    }



                    // sensitivity.LongStockCalc(tablef);
                    sensitivity.CalcFavorState(tablef);
                    sensitivity.CalcHighvFavorability();
                }
            }


            return true;
        }

        public static bool AddFavorability(int charaId0, int charaId1, int amount, Sensitivity kind)
        {
            var sensitivity = _Charas[charaId0].charasGameParam.sensitivity;
            if (sensitivity == null)
                return false;
            var tablef = sensitivity.GetInfoFromID(charaId1);
            if (tablef == null)
                return false;
            var shorts = tablef.shortStocks.ToList();
            if (shorts == null || shorts.Count != 4)
                return false;
            int n = 30;
            int i = (int)kind;
            int cnumi = 0;
            lock (tablef)
            {
                lock (sensitivity)
                {

                    tablef.shortStocks[i] += amount;
                    cnumi = tablef.shortStocks[i] / n;
                    tablef.shortStocks[i] -= cnumi * n;
                    for (int k = 0; k < cnumi; k++)
                    {
                        tablef.longStocks.Add(i);
                    }


                    int m = tablef.longStocks.Count - 30;
                    if (m > 0)
                    {
                        tablef.longStocks.RemoveRange(0, m);
                    }
                    tablef.longStocks._size = tablef.longStocks.Count;

                    tablef.longSensitivityCounts[i] = tablef.longStocks.ToList().Count(x => x == i);


                    //sensitivity.LongStockCalc(tablef);
                    sensitivity.CalcFavorState(tablef);
                    sensitivity.CalcHighvFavorability();

                }
            }

            return true;
        }
        public static Favorability GetFavorability(int charaId0, int charaId1)
        {
            var tablef = _Charas[charaId0].charasGameParam.sensitivity.GetInfoFromID(charaId1);
            var shorts = tablef.shortStocks.ToList();
            var longs = tablef.longStocks.ToList();
            var longcounts = tablef.longSensitivityCounts.ToList();
            Favorability favrobility = new Favorability(shorts, longcounts);
            return favrobility;
        }

        public static int GetFavorability(int charaId0, int charaId1, Sensitivity kind)
        {
            Favorability favrobility = GetFavorability(charaId0, charaId1);
            return favrobility.feelings[(int)kind].ToDecimal();
        }
        public static SensitivityParameter.Rank GetFavorabilityRank(int charaId0, int charaId1, Sensitivity kind)
        {
            var tablef = _Charas[charaId0].charasGameParam.sensitivity.GetInfoFromID(charaId1);
            var ranks = tablef.ranks.ToList();
            return ranks[(int)kind];
        }

        public static bool HasMaximalFeelingOn(int charaId0, int charaId1, Sensitivity kind)
        {
            var hfk = _Charas[charaId0].charasGameParam.sensitivity.GetHighFavorability((int)kind).ToList();

            return hfk.Contains(charaId1);
        }
        public static bool HasPartnership(int charaId0, int charaId1)
        {
            var lovers = _Charas[charaId0].charasGameParam.memory.lovers.ToList();

            return lovers.Exists(x => (x.id == charaId1));
        }
        public static int PartnerCount(int charaId0)
        {
            var lovers = _Charas[charaId0].charasGameParam.memory.lovers.ToList();

            return lovers.Count;
        }
        public static bool HasH(int charaId0, int charaId1)
        {
            var pairTable = _Charas[charaId0].charasGameParam.memory.pairTable.ToDict();

            return pairTable.Any(x => (x.Key == charaId1 && x.Value.TotalH > 0));
        }
        public static bool HasInsertedH(int charaId0, int charaId1)
        {
            var pairTable = _Charas[charaId0].charasGameParam.memory.pairTable.ToDict();

            return pairTable.Any(x => (x.Key == charaId1 && x.Value.Insertion > 0));
        }

        public static bool HasAnalH(int charaId0, int charaId1)
        {
            var pairTable = _Charas[charaId0].charasGameParam.memory.pairTable.ToDict();

            return pairTable.Any(x => (x.Key == charaId1 && x.Value.Anal > 0));
        }
        public List<int> GetAffinityAffectedCharas(Player player1)
        {
            int charaId = this._charaId;
            int charaId1 = player1._charaId;
            List<int> charasIds = _Charas.ToDict().Select(x => x.Key).ToList();
            List<int> self = new List<int>() { charaId };
            List<Player> listopponent = _gameController._playerList.Where(p => p.IsAnOpponent(this._thisplayer)).ToList();
            List<int> listopponentIds = listopponent.Select(p => p._charaId).ToList();
            List<int> rivals = new List<int>();
            if (!listopponentIds.Contains(charaId1))
            {
                return rivals;
            }
            foreach (var charaId2 in charasIds)
            {
                if (charaId2 == charaId1 || charaId2 == charaId || listopponentIds.Contains(charaId2))
                {
                    continue;
                }
                var ctrl2 = _Charas[charaId2].charFile;

                if (this.IsInvaded())
                {

                    if (this.VirginaInserted() && player1._actorController.VirginaInserted() && player1._actorController.IsInvading())
                    {

                        if (HasInsertedH(charaId2, charaId) && (ctrl2.Parameter.sex == 0 || ctrl2.Parameter.isFutanari))
                        {
                            if (ctrl2.GameParameter.LvPhysical < player1._actorController.PhysicalLv)
                            {
                                rivals.Add(charaId2);
                            }
                        }
                    }
                    if (this.AnalInserted() && player1._actorController.AnalInserted() && player1._actorController.IsInvading())
                    {
                        if (HasAnalH(charaId2, charaId) && (ctrl2.Parameter.sex == 0 || ctrl2.Parameter.isFutanari))
                        {
                            if (ctrl2.GameParameter.LvPhysical < player1._actorController.PhysicalLv)
                            {
                                rivals.Add(charaId2);
                            }
                        }
                    }
                }
            }

            return rivals;

        }
        public List<int> GetFavoAffectedRivals(Player player1)
        {
            int charaId = this._charaId;
            var individuality = this._hActor.Human.fileGameParam.individuality.answer.ToList();
            var orient = this._hActor.Human.fileGameParam.SexualTarget;
            var chastityLv = this.ChastityLv;
            int oppoId = player1._charaId;
            List<int> charasIds = _Charas.ToDict().Select(x => x.Key).ToList();
            List<int> self = new List<int>() { charaId };
            List<Player> listopponent = _gameController._playerList.Where(p => p.IsAnOpponent(this._thisplayer)).ToList();
            List<int> listopponentIds = listopponent.Select(p => p._charaId).ToList();
            ;
            List<int> rivalIds = new List<int>();
            int love01 = GetFavorability(charaId, oppoId).feelings[(int)Sensitivity.LOVE].ToDecimal();
            int afflv01 = GetAffinityLv(charaId, oppoId);
            if (!listopponentIds.Contains(oppoId))
            {
                return rivalIds;
            }
            foreach (var charaId2 in charasIds)
            {
                if (charaId2 == oppoId || charaId2 == charaId || listopponentIds.Contains(charaId2) || !HasH(charaId, charaId2))
                {
                    continue;
                }
                if (_Charas[charaId2].charFile.Parameter.sex != _Charas[oppoId].charFile.Parameter.sex && !_Charas[oppoId].charFile.Parameter.isFutanari && !_Charas[charaId2].charFile.Parameter.isFutanari)
                    continue;
                bool ichizuflag2 = individuality.Contains((int)Individuality.一途) && HasPartnership(charaId, charaId2)
                    || individuality.Contains((int)Individuality.心の闇) && HasMaximalFeelingOn(charaId, charaId2, Sensitivity.LOVE);
                if (ichizuflag2)
                    continue;



                int love02 = GetFavorability(charaId, charaId2).feelings[(int)Sensitivity.LOVE].ToDecimal();
                int afflv02 = GetAffinityLv(charaId, charaId2);
                bool thresholdlove = (chastityLv == (int)PropertyLevel.HIGH && HasMaximalFeelingOn(charaId, oppoId, Sensitivity.LOVE)) ||
                    (chastityLv == (int)PropertyLevel.MiddleHigh && (int)GetFavorabilityRank(charaId, oppoId, Sensitivity.LOVE) >= (int)Rank.HIGH && love01 >= love02) ||
                    (chastityLv == (int)PropertyLevel.MEDIUM && love01 >= love02) ||
                    (chastityLv == (int)PropertyLevel.MiddleLow && true) ||
                    (chastityLv == (int)PropertyLevel.LOW && false)
                    ;
                bool thresholdaffinity = (chastityLv == (int)PropertyLevel.HIGH && afflv01 >= 4 && afflv01 > afflv02) ||
                    (chastityLv == (int)PropertyLevel.MiddleHigh && afflv01 >= 3 && afflv01 > afflv02) ||
                    (chastityLv == (int)PropertyLevel.MEDIUM && afflv01 > afflv02) ||
                    (chastityLv == (int)PropertyLevel.MiddleLow && true) ||
                    (chastityLv == (int)PropertyLevel.LOW && false)
                    ;

                if (thresholdlove || this.IsLewdness() && thresholdaffinity)
                {
                    rivalIds.Add(charaId2);
                }


            }

            return rivalIds;

        }
        public bool IsInDangerousDay()
        {


            return this.GetSex() == 1 && TodayIsDangerousDay(_charaId);
        }
        public static bool TodayIsDangerousDay(int charaId0)
        {
            int day = Manager.Game.saveData.Day % 14;

            return _Charas[charaId0].charasGameParam.menstruations[day] == (int)Menstruation.Danger;
        }
        public static bool AddAffinity(int charaId0, int charaId1, int amount)
        {

            var param = _Charas[charaId0].charasGameParam.baseParameter;
            var currentAffinity = param.GetHAffinity(charaId1);

            int currentaffinity = currentAffinity.Point;

            amount = Mathf.Clamp(amount, -currentaffinity, 100 - currentaffinity);
            param.AddHAffinity(charaId1, amount);
            return true;

        }

        public int GetInitialAffinityScore(Player player1)
        {
            int score = 0;
            // int affinityLv = GetAffinityLv(_charaId, player1._charaId);
            if (this.IsInvaded() && !this.IsNOTInside())
            {
                if (player1._actorController.IsInvading() && player1._actorController.IsAnOpponentPos(this._animPos))
                {
                    score = GetInitialAffinityScore(_charaId, player1._charaId);
                }



            }
            return score;
        }
        public static int GetInitialAffinityScore(int charaId0, int charaId1)
        {
            int score = 0;
            int affinityLv = GetAffinityLv(charaId0, charaId1);


            int heightc = _Charas[charaId0].charFile.Custom.GetHeightKind();
            int sizeLv = _Charas[charaId1].charFile.GameParameter.LvPhysical;
            int tempscore = sizeLv - (heightc + 1);
            if (tempscore == 1 || tempscore == 2)
            {
                score = 4;
            }
            else if (tempscore == 0)
            {
                score = 3;
            }
            else if (tempscore == -1)
            {
                score = 2;
            }
            else if (tempscore == -2)
            {
                score = 1;
            }
            else if (tempscore <= -3)
            {
                score = 0;
            }
            else if (tempscore >= 3)
            {
                score = -1;
            }

            return score;
        }

        public static int GetAffinity(int charaId0, int charaId1)
        {
            var param = _Charas[charaId0].charasGameParam.baseParameter;
            var currentAffinity = param.GetHAffinity(charaId1);
            return currentAffinity.Point;
        }
        public static int GetAffinityLv(int charaId0, int charaId1)
        {

            return _Charas[charaId0].charasGameParam.baseParameter.GetHAffinityLV(charaId1);
        }
        public static int GetFinalAffinityScore(int charaId0, int charaId1)
        {

            int affinityLv = GetAffinityLv(charaId0, charaId1);
            int initialscore = GetInitialAffinityScore(charaId0, charaId1);
            int finalscore = 0;
            if (affinityLv == (int)AffinityLevel.普通)
            {
                finalscore = System.Math.Min(initialscore, affinityLv);
            }
            else
            {
                finalscore = affinityLv;
            }
            finalscore = System.Math.Clamp(finalscore, 0, 4);
            return finalscore;
        }
        public int IsPalatableSex(Player player1)
        {
            var sex = this._hActor.Human.sex;
            var orient = this._hActor.Human.fileGameParam.SexualTarget;
            var sex1 = player1._hActor.Human.sex;
            if ((sex ^ sex1) == 1)//diff
            {
                return 4 - orient;
            }
            else if ((sex ^ sex1) == 0)//same
            {
                return orient;
            }

            return 0;
        }

        #endregion
        protected virtual void Update()
        {
            //_hActor.WordPlayer.General._insertType==SV.H.Words.FlagManager.InsertType.Vagina
            //_hActor.WordPlayer.General.typ
            //this._hActor.Human.data.
            //   this.CalcDPSRate1();

        }
        protected virtual void LateUpdate()
        {

        }
    }

}
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
using ILLGames.Extensions;
using UnityEngine.PlayerLoop;
using static ILLGames.Unity.Utils;
using MessagePack.Formatters.SaveData;
using static SaveData.SensitivityParameter;
using UnityEngine.Animations;
using static SaveData.StateParameter;


namespace SVSPregnancy
{
    public class PregnancyCharaController : MonoBehaviour
    {

        public bool _inited = false;
        public IntPtr _charaPtr = IntPtr.Zero;
        public Actor _chara => _charaPtr.ToObject<Actor>();
        public int _charaId => _chara.charasGameParam.Index;
        public static Il2CppSystem.Collections.Generic.Dictionary<int, Actor> _Charas => Manager.Game.Charas;
        public PregnancyInfo _pregnancyInfo;

        public PregnancyCharaController(IntPtr ptr) : base(ptr)
        {

        }

        public void Init(IntPtr ptrActor)
        {
            _charaPtr= ptrActor;
            

            if (_charaId < 0)
            {
                $"Failed retrive charaId.".ToLog();
                DestroyMe();
            }
            this._pregnancyInfo = new PregnancyInfo(_chara.charFile.Parameter.sex);
           _inited = true;
        }

        public void Update()
        {
           /* if (_inited&& _chara == null)
            {
                
                
                DestroyMe();
            }*/
            
        }

        public void DestroyMe()
        {
            _inited = false;
            UnityEngine.Object.Destroy(this.gameObject);
        }



        public bool Conceive(string fathergivenname, string fathersurnname,bool acceptance)
        {
            if (_pregnancyInfo._sex == 0 || _pregnancyInfo.IsPregnant || _pregnancyInfo.IsCoolingdown)
            {
                return false;
            }
            Il2CppSystem.Random randomizer = new Il2CppSystem.Random();
            _pregnancyInfo._currentMaximalPregnantDays = PregnancyInfo.defaultmaximalPregnantDays + randomizer.Next(-21, 22);            
            _pregnancyInfo._day = 0;
            _pregnancyInfo._father_givenname = fathergivenname;
            _pregnancyInfo._father_surname = fathersurnname;
            _pregnancyInfo._acceptance = acceptance;
            return true;
            //return _pregnancyInfo.Seed(fathergivenname, fathersurnname);


        }

        public bool DayPlus()
        {
            if (_pregnancyInfo.IsPregnant)
            {
                _pregnancyInfo._day+= PregnancyPlugin.PregnancyProgressionSpeed.Value;

            }
            if (_pregnancyInfo.IsCoolingdown)
            {
                _pregnancyInfo._cooldown -= PregnancyPlugin.PregnancyProgressionSpeed.Value;

            }
            if (_pregnancyInfo._cooldown < 0)
            {
                _pregnancyInfo._cooldown = 0;
            }
            if (_pregnancyInfo._day >= _pregnancyInfo._currentMaximalPregnantDays|| _pregnancyInfo._broken)
            {
                GiveBirth();
            }
            
            return true;
            //return _pregnancyInfo.Seed(fathergivenname, fathersurnname);


        }
        public bool GiveBirth()
        {
            int babysize = CheckBabySize();
            if (babysize < 1)
            {
                _pregnancyInfo._broken = false;
                return false;
            }
            string mothername = _chara.parameter.lastname+ _chara.parameter.firstname;
            string fathername = _pregnancyInfo._father_surname + _pregnancyInfo._father_givenname;
            List<StateKind> newemotion = new List<StateKind>();

            var individuality = _chara.charFile.GameParameter.individuality.answer.ToList();

            if (CheckBabySize() >= 4)
            {
                if (_pregnancyInfo._acceptance)
                {
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.UPLIFT);
                    newemotion.Add(StateKind.UPLIFT);
                    newemotion.Add(StateKind.UPLIFT);
                }
                else
                {
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.TENSION);
                    newemotion.Add(StateKind.TENSION);
                    newemotion.Add(StateKind.TENSION);
                }
                PregnancyActorController.ShowText($"{mothername}��{fathername}�Ƃ̌��C�ȐԂ������Y�񂶂����");
                _pregnancyInfo._cooldown = PregnancyInfo.defaultcooldownDays;
            }
            if (CheckBabySize() == 3)
            {
                if (_pregnancyInfo._acceptance)
                {
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.UPLIFT);
                    newemotion.Add(StateKind.UPLIFT);
                    newemotion.Add(StateKind.UPLIFT);
                    newemotion.Add(StateKind.TENSION);
                    newemotion.Add(StateKind.TENSION);
                    newemotion.Add(StateKind.TENSION);
                }
                else
                {
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.PEACEOFMIND);
                    newemotion.Add(StateKind.PEACEOFMIND);
                    if (!individuality.Contains((int)CustomActorController.Individuality.平常心))
                    {
                        newemotion.Add(StateKind.TENSION);
                        newemotion.Add(StateKind.TENSION);
                        newemotion.Add(StateKind.TENSION);
                    }
                    else
                    {
                        newemotion.Add(StateKind.TENSION);
                    }
                        
                }
                PregnancyActorController.ShowText($"{mothername}��{fathername}�Ƃ̐Ԃ������Y�񂶂����");
                _pregnancyInfo._cooldown = PregnancyInfo.defaultcooldownDays;
            }
            else if (CheckBabySize() == 2)
            {
                if (_pregnancyInfo._acceptance)
                {
                    newemotion.Add(StateKind.DISAPPOINTMENT);
                    newemotion.Add(StateKind.DISAPPOINTMENT);
                    newemotion.Add(StateKind.DISAPPOINTMENT);
                }
                else
                {
                    newemotion.Add(StateKind.NORMAL);
                    newemotion.Add(StateKind.NORMAL);
                    newemotion.Add(StateKind.NORMAL);
                }
                PregnancyActorController.ShowText($"{mothername}��{fathername}�Ƃ̐Ԃ�����������������");
                _pregnancyInfo._cooldown = PregnancyInfo.defaultcooldownDays / 6;
            }
            else if (CheckBabySize() ==1)
            {
                if (_pregnancyInfo._acceptance)
                {
                    newemotion.Add(StateKind.DISAPPOINTMENT);
                    newemotion.Add(StateKind.DISAPPOINTMENT);
                    newemotion.Add(StateKind.DISAPPOINTMENT);
                }
                else
                {
                    newemotion.Add(StateKind.NORMAL);
                    newemotion.Add(StateKind.NORMAL);
                    newemotion.Add(StateKind.NORMAL);
                }
                PregnancyActorController.ShowText($"{mothername}��{fathername}�Ƃ̐Ԃ�����������������");
                _pregnancyInfo._cooldown = PregnancyInfo.defaultcooldownDays/18;
            }
            PregnancyActorController.AddEmotion(_charaId, newemotion);
            _pregnancyInfo._day = -1;
            _pregnancyInfo._broken = false;
            _pregnancyInfo._father_givenname = "";
            _pregnancyInfo._father_surname = "";
            return true;

        }
        public bool IsCoolingdown()
        {
            if (_pregnancyInfo != null)
            {
                return _pregnancyInfo.IsCoolingdown;
            }
            else
            {
                return false;
            }
        }
        public bool IsPregnant()
        {
            if (_pregnancyInfo != null)
            {
                return _pregnancyInfo.IsPregnant;
            }
            else
            {
                return false;
            }
        }

        public int CheckBabySize()
        {
            if (!IsPregnant())
                return -1;
            else if (_pregnancyInfo._day >= PregnancyInfo.defaultGrowLineXL)
            {
                return 4;
            }
            else if (_pregnancyInfo._day >= PregnancyInfo.defaultGrowLineL)
            {
                return 3;
            }
            else if (_pregnancyInfo._day >= PregnancyInfo.defaultGrowLineM)
            {
                return 2;
            }
            else if (_pregnancyInfo._day >= PregnancyInfo.defaultGrowLineS)
            {
                return 1;
            }
            else 
            {
                return 0;
            }
        }
        public class PregnancyInfo
        {
            public PregnancyInfo(int sex)
            {
                this._sex = sex;
            }
            public static int defaultmaximalPregnantDays=280;
            public static int defaultcooldownDays = 180;
            public static int defaultGrowLineS = 28;
            public static int defaultGrowLineM = 91;
            public static int defaultGrowLineL = 196;
            public static int defaultGrowLineXL = 259;
            public int _currentMaximalPregnantDays=280;
            public bool _broken = false;
            public bool _acceptance = true;
           // public int _currentcooldownDays = 180;
            public int _sex = 0;
            public int _day=-1;
            public int _cooldown = 0;
            public string _father_givenname = "";
            public string _father_surname = "";
            public bool IsPregnant => _day >= 0;
            public bool IsCoolingdown => _cooldown > 0;

            internal void Reset()
            {
                 _day = -1;
                _cooldown = 0;
                _father_givenname = "";
                _father_surname = "";
            }


        }

    }
}
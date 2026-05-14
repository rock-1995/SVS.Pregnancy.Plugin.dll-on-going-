using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ILLGames.Unity.Component;
using Manager;
using SV.H;
using System;
using System.Collections.Generic;
//using Il2CppSystem.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using System.Threading.Tasks;
using Il2CppInterop.Runtime.InteropTypes.Fields;
using ADV.Commands.Base;
using MessagePack.Formatters.Character;
using SV;
using SV.H.UI;
using CharacterCreation.UI.View;

namespace SVSPregnancy
{
    public class Player
    {
        public int _index = -1;
        public int _charaId => _actorController._charaId;
        public SV.H.HActor _hActor => _actorController._hActor;
        public PregnancyActorController _actorController;
       // internal AnimEnhanceGaugeController? _gaugeController;
 


        public Player(int index, IntPtr ptrActor, PregnancyActorController actorController)//, AnimEnhanceGaugeController gaugeController)
        {
            _index = index;
            _actorController = actorController;

            
          //  _gaugeController = gaugeController;
            if (_actorController != null)
            {
                _actorController._thisplayer = this;
                _actorController.Init(index, ptrActor);
            }
            /*
            if (_gaugeController != null)
            {
                _gaugeController.Init(index);
            }*/

        }

        public bool IsAnOpponent(Player player1)
        {
            if (this._actorController == null || player1._actorController == null || this == player1)
            {
                return false;
            }
            return this._actorController.IsAnOpponentPos(player1._actorController._animPos);
        }

        public bool IsMainOpponent(Player player1)
        {
            bool result = IsAnOpponent(player1) && player1._actorController.IsInMainPos();
            return result;
        }


    }
}

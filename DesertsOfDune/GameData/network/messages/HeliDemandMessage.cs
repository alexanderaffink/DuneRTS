﻿using GameData.network.util.world;
using System;
using System.Collections.Generic;
using System.Text;

namespace GameData.network.messages
{
    /// <summary>
    /// This message class is used to comunicate the demand of a helicopter
    /// </summary>
    public class HeliDemandMessage : TurnMessage
    {

        public Position target { get; }
        public bool crash { get; }

        /// <summary>
        /// This is the Constructor of the class HeliDemandMessage
        /// </summary>
        /// <param name="clientID">the id of the client</param>
        /// <param name="characterID">the id of the character</param>
        /// <param name="target">the target the heli is demanded on </param>
        /// <param name="crash">true, if the helicopter crashes</param>
        public HeliDemandMessage(int clientID, int characterID, Position target, bool crash) : base(characterID, clientID, MessageType.HELI_DEMAND)
        {
            this.target = target;
            this.crash = crash;
        }
    }
}

﻿using System;
using System.Collections.Generic;
using GameData.network.controller;
using GameData.network.messages;
using GameData.network.util.world;
using Server.Clients;
using Server;
using Serilog;
using Server.ClientManagement.Clients;
using GameData.network.util.enums;
using GameData.network.util.world.greatHouse;
using System.Linq;
using Serilog.Sinks.SystemConsole.Themes;
using GameData.network.util.parser;
using Server.Configuration;
using Newtonsoft.Json;

namespace Server
{
    public class ServerMessageController : MessageController
    {
        private Party party;

        private bool firstPlayerGotGreatHousesAndGotRequestAck;

        public ServerMessageController()
        {
            this.firstPlayerGotGreatHousesAndGotRequestAck = false;
        }

        /// <summary>
        /// Client requests to join a party with a clintName and a flag if he is player or spectator.
        /// To join to the party, the connectionCode from the JoinMessage has to be equal to the lobbyCode of the created party.
        /// </summary>
        /// <param name="msg">JoinMessage with the value clientName, connectionCode and active flag if he is a player.</param>
        /// <param name="sessionID">the session id of the client, who wants to join</param>
        /// TODO: handle reconnect
        public void OnJoinMessage(JoinMessage msg, string sessionID)
        {
            Client client;

            // check, whether the new client is a player or spectator
            if (msg.active)
            {
                // check, whether there are already two active player
                if (party.AreTwoPlayersRegistred())
                {
                    // already two players are registred, so send error
                    DoSendError(003, "There are already two players registred", sessionID);
                    return;
                }

                // check, whether active player is a human or an ai
                if (msg.isCpu)
                {
                    // client is an ai
                    client = new AIPlayer(msg.clientName, sessionID);
                }
                else
                {
                    // client is a human player
                    client = new HumanPlayer(msg.clientName, sessionID);
                }
            }
            else
            {
                client = new Spectator(msg.clientName, sessionID);

            }
            party.AddClient(client);
            // send join accept
            DoAcceptJoin(client.ClientSecret, client.ClientID, sessionID);

            // check, if with new client two players are registred and start party
            if (party.AreTwoPlayersRegistred())
            {
                party.PrepareGame();
                DoSendGameConfig();
            }
        }

        /// <summary>
        /// executed, if the clients requested a certain great house
        /// </summary>
        /// <param name="msg">the HouseRequestMessage, which contains the house name</param>
        /// <param name="sessionID">the session id of the requesting client</param>
        public void OnHouseRequestMessage(HouseRequestMessage msg, string sessionID)
        {
            GreatHouseType chosenGreatHouse = (GreatHouseType)Enum.Parse(typeof(GreatHouseType), msg.houseName);

            // get the player, who send this request
            Player requestingPlayer = party.GetPlayerBySessionID(sessionID);

            if (requestingPlayer != null)
            {
                // check, whether this decision is valid, if not resend house offer and strike
                if (requestingPlayer.OfferedGreatHouses.Contains(chosenGreatHouse))
                {
                    requestingPlayer.UsedGreatHouse = GreatHouseFactory.CreateNewGreatHouse(chosenGreatHouse);
                    DoSendHouseAck(requestingPlayer.ClientID, chosenGreatHouse.ToString());
                    Log.Information("The player with the session id: " + sessionID + " chose the great house " + chosenGreatHouse.ToString());

                    // check, whether the other player already got confirmation
                    if (!firstPlayerGotGreatHousesAndGotRequestAck)
                    {
                        firstPlayerGotGreatHousesAndGotRequestAck = true;
                    } else
                    {
                        // first player already has great house, so start the game
                        party.Start();
                    }
                }
                else
                {
                    Log.Error("The player with the session id: " + sessionID + " requested " + chosenGreatHouse.ToString() + ", but the server didn't offered this greathouse");

                    // resend house offer:
                    DoSendHouseOffer(requestingPlayer.ClientID, requestingPlayer.OfferedGreatHouses);

                    // send a strike
                    DoSendStrike(requestingPlayer, msg);
                }

            }
            else
            {
                Log.Error("There is no player with the session id: " + sessionID);
            }

        }

        /// <summary>
        /// executed, when a player wants to move his character along a path while it's his turn
        /// </summary>
        /// <param name="msg">contains informations about the player, the character he wants to move and the path he wants to move his character along</param>
        /// <exception cref="NotImplementedException"></exception>
        public void OnMovementRequestMessage(MovementRequestMessage msg)
        {
            throw new NotImplementedException("not implemented completely");

            //request from client to move a character

            //get the player who wants to move his character
            Player activePlayer;
            foreach (var player in party.GetActivePlayers())
            {
                if(player.ClientID == msg.clientID)
                {
                    activePlayer = player;
                }
            }

            //get the character which should be moved
            Character movingCharacter;
            foreach (var character in activePlayer.UsedGreatHouse.Characters)
            {
                if (character.CharacterId == msg.characterID)
                {
                    movingCharacter = character;
                }
            }

            List<Position> path = new List<Position>();
            foreach (var position in path)
            {
                //check if Character has enough Movement Points
                if (movingCharacter.MPcurrent > 0)
                {
                    //check if movement is in bounds of the map
                    if (position.x >= 0 && position.x < party.map.MAP_WIDTH && position.y >= 0 && position.y < party.map.MAP_HEIGHT)
                    {
                        //check if movement is on walkable terrain
                        if (party.map.fields[position.x, position.y].tileType != "Mountain" && party.map.fields[position.x, position.y].tileType != "City") //check needed and not implemented utils
                        {
                            movingCharacter.Movement(movingCharacter.CurrentMapfield, party.map.fields[position.x, position.y]); //move character 1 field along its path
                            path.Add(position);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            DoSendMovementDemand(msg.clientID, msg.characterID, path);
        }

        /// <summary>
        /// executed, when a player want to do a action with his character while it's his turn
        /// </summary>
        /// <param name="msg">contains informations about the player, the character he wants to do a action with and the action he wants his character to do</param>
        public void OnActionRequestMessage(ActionRequestMessage msg)
        {

            //request from client to run an action
            Player activePlayer = null;
            foreach (var player in party.GetActivePlayers())
            {
                if (player.ClientID == msg.clientID)
                {
                    activePlayer = player;
                }
            }

            //get the character which should do the action
            Character actionCharacter = null;
            Character targetCharacter = null;
            foreach (var character in activePlayer.UsedGreatHouse.Characters)
            {
                if (character.CharacterId == msg.characterID)
                {
                    actionCharacter = character;
                }
                if (character.CurrentMapfield.stormEye == msg.specs.target)
                {
                    targetCharacter = character;
                }
            }

            //set Attack as standard enum and change it if needed
            ActionType action = ActionType.ATTACK;

            if (actionCharacter.APcurrent > 0)
            {
                //check which action the player wants to do with his character
                switch (action)
                {
                    case ActionType.ATTACK:
                        action = ActionType.ATTACK;
                        actionCharacter.Atack(targetCharacter);
                        break;

                    case ActionType.COLLECT:
                        action = ActionType.COLLECT;
                        actionCharacter.CollectSpice();
                        break;

                    case ActionType.TRANSFER:
                        action = ActionType.TRANSFER;
                        throw new NotImplementedException("not implemented");
                        //actionCharacter.GiftSpice(targetCharacter, amount);
                        break;

                        //check in every special action if the character is from the right character type to do the special aciton and check if his ap is full
                    case ActionType.KANLY:
                        action = ActionType.KANLY;
                        if (actionCharacter.APcurrent == actionCharacter.APmax)
                        {
                            if (actionCharacter.characterType == Enum.GetName(typeof(CharacterType), CharacterType.NOBEL) && targetCharacter.characterType == Enum.GetName(typeof(CharacterType), CharacterType.NOBEL))
                            {
                               actionCharacter.Kanly(targetCharacter);
                            }
                        }
                        break;

                    case ActionType.FAMILY_ATOMICS:
                        action = ActionType.FAMILY_ATOMICS;
                        if (actionCharacter.APcurrent == actionCharacter.APmax)
                        {
                            if (actionCharacter.characterType == Enum.GetName(typeof(CharacterType), CharacterType.NOBEL))
                            {
                                //get the mapfield where the active Character aims to
                                MapField targetMapField = null;
                                foreach (var mapfield in party.map.fields)
                                {
                                    if(mapfield.stormEye == msg.specs.target)
                                    {
                                        targetMapField = mapfield;
                                    }
                                }
                                
                                actionCharacter.AtomicBomb(targetMapField);
                            }
                        }
                        break;

                    case ActionType.SPICE_HORDING:
                        action = ActionType.SPICE_HORDING;
                        if (actionCharacter.APcurrent == actionCharacter.APmax)
                        {
                            if (actionCharacter.characterType == Enum.GetName(typeof(CharacterType), CharacterType.MENTAT))
                            {
                                actionCharacter.SpiceHoarding();
                            }
                        }
                        break;

                    case ActionType.VOICE:
                        action = ActionType.VOICE;
                        if (actionCharacter.APcurrent == actionCharacter.APmax)
                        {
                            if (actionCharacter.characterType == Enum.GetName(typeof(CharacterType), CharacterType.BENEGESSERIT))
                            {
                                actionCharacter.Voice(targetCharacter);
                            }
                        }
                        break;

                    case ActionType.SWORD_SPIN:
                        action = ActionType.SWORD_SPIN;
                        if (actionCharacter.APcurrent == actionCharacter.APmax)
                        {
                            if (actionCharacter.characterType == Enum.GetName(typeof(CharacterType), CharacterType.FIGHTHER))
                            {
                                actionCharacter.SwordSpin();
                            }
                        }
                        break;
                }
            }
            DoSendActionDemand(msg.clientID, msg.characterID, action, msg.specs.target);
        }

        public void OnTransferRequestMessage(TransferRequestMessage msg)
        {
            throw new NotImplementedException("not implemented");
        }

        public void OnEndTurnRequestMessage(EndTurnRequestMessage msg)
        {
            throw new NotImplementedException("not implemented");

            //End move phase prematurely

            //int clientID
            //int characterID
        }

        public void OnGameStateRequestMessage(GameStateRequestMessage msg)
        {
            throw new NotImplementedException("not implemented");

            //Requirement complete game state

            //int clientID
        }

        public void OnPauseGameRequestMessage(PauseGameRequestMessage msg)
        {
            throw new NotImplementedException("not implemented");

            //request for pause from client

            //bool pause
        }



        /// <summary>
        /// /// The server is sending a JoinAcceptedMessage if the join was successful
        /// TODO: what is sending back if a exception is thrown?
        /// </summary>
        /// <param name="clientSecret">Unique identifikator for the client, which is just known between the affected parties</param>
        public void DoAcceptJoin(string clientSecret, int clientID, string sessionID)
        {
            JoinAcceptedMessage joinAcceptedMessage = new JoinAcceptedMessage(clientSecret, clientID);
            NetworkController.HandleSendingMessage(joinAcceptedMessage, sessionID);
            Log.Information("Join request of " + clientID + " was accepted");
        }

        public void DoSendAck()
        {
            AckMessage ackMessage = new AckMessage();
            NetworkController.HandleSendingMessage(ackMessage);
        }

        /// <summary>
        /// sends the an error message to the client
        /// </summary>
        /// <param name="errorCode">the error code (see "Standardisierungsdokument")</param>
        /// <param name="errorDescription">a further description of the error</param>
        /// <param name="sessionID">the session id of the client, the message need to be send to</param>
        public void DoSendError(int errorCode, string errorDescription, string sessionID)
        {
            ErrorMessage errorMessage = new ErrorMessage(errorCode, errorDescription);
            NetworkController.HandleSendingMessage(errorMessage, sessionID);
            Log.Debug("An error (code = " + errorCode + " ) occured: " + errorDescription);
        }

        public void DoSendGameConfig()
        {
            int client0ID = party.GetActivePlayers()[0].ClientID;
            int client1ID = party.GetActivePlayers()[0].ClientID;

            List<List<string>> scenario = ScenarioConfiguration.GetInstance().scenario;
            string partyConfiguration = JsonConvert.SerializeObject(PartyConfiguration.GetInstance());

            GameConfigMessage gameConfigMessage = new GameConfigMessage(scenario, partyConfiguration, client0ID, client1ID);
            NetworkController.HandleSendingMessage(gameConfigMessage);
        }

        public void DoSendHouseOffer(int clientID, GreatHouseType[] houses)
        {
            GreatHouse[] greatHouses = { GreatHouseFactory.CreateNewGreatHouse(houses[0]), GreatHouseFactory.CreateNewGreatHouse(houses[1]) };
            HouseOfferMessage houseOfferMessage = new HouseOfferMessage(clientID, greatHouses);
            NetworkController.HandleSendingMessage(houseOfferMessage);
        }

        public void DoSendHouseAck(int clientID, string houseName)
        {
            HouseAcknowledgementMessage houseACKMessage = new HouseAcknowledgementMessage(clientID, houseName);
            NetworkController.HandleSendingMessage(houseACKMessage);
        }

        public void DoSendTurnDemand(int clientID, int characterID)
        {
            TurnDemandMessage turnDemandMessage = new TurnDemandMessage(clientID, characterID);
            NetworkController.HandleSendingMessage(turnDemandMessage);
        }

        public void DoSendMovementDemand(int clientID, int characterID, List<Position> path)
        {
            MovementDemandMessage movementDemandMessage = new MovementDemandMessage(clientID, characterID, path);
            NetworkController.HandleSendingMessage(movementDemandMessage);
        }

        public void DoSendActionDemand(int clientID, int characterID, ActionType action, Position target)
        {
            ActionDemandMessage actionDemandMessage = new ActionDemandMessage(clientID, characterID, action, target);
            NetworkController.HandleSendingMessage(actionDemandMessage);
        }

        public void DoSendTransferDemand(int clientID, int characterID, int targetID)
        {
            TransferDemandMessage transferDemandMessage = new TransferDemandMessage(clientID, characterID, targetID);
            NetworkController.HandleSendingMessage(transferDemandMessage);
        }

        public void DoSendChangeCharacterStatsDemand(int clientID, int characterID, CharacterStatistics stats)
        {
            ChangeCharacterStatisticsDemandMessage changeCharacterStatisticsDemandMessage = new ChangeCharacterStatisticsDemandMessage(clientID, characterID, stats);
            NetworkController.HandleSendingMessage(changeCharacterStatisticsDemandMessage);
        }

        public void DoSendMapChangeDemand(MapChangeReasons mapChangeReasons, MapField[,] newMap)
        {
            MapChangeDemandMessage mapChangeDemandMessage = new MapChangeDemandMessage(mapChangeReasons, newMap);
            NetworkController.HandleSendingMessage(mapChangeDemandMessage);
        }

        public void DoSpawnCharacterDemand(Character attributes)
        {
            string characterName = attributes.HouseCharacter.characterName;
            Position pos = new Position(attributes.CurrentMapfield.XCoordinate, attributes.CurrentMapfield.ZCoordinate);

            // todo set clientid reasonable.
            SpawnCharacterDemandMessage spawnCharacterDemandMessage = new SpawnCharacterDemandMessage(1234, attributes.CharacterId, characterName, pos, attributes);
            NetworkController.HandleSendingMessage(spawnCharacterDemandMessage);
        }

        public void DoChangePlayerSpiceDemand(int clientID, int newSpiceVal)
        {
            ChangePlayerSpiceDemandMessage changePlayerSpiceDemandMessage = new ChangePlayerSpiceDemandMessage(clientID, newSpiceVal);
            NetworkController.HandleSendingMessage(changePlayerSpiceDemandMessage);
        }

        public void DoSpawnSandwormDemand(int characterID, MapField mapField)
        {
            int x = mapField.XCoordinate;
            int z = mapField.ZCoordinate;
            Position position = new Position(x,z);

            SandwormSpawnDemandMessage sandwormSpawnDemandMessage = new SandwormSpawnDemandMessage(/*Party.GetInstance().GetClientID()*/1, characterID, position);
            NetworkController.HandleSendingMessage(sandwormSpawnDemandMessage);
        }

        public void DoMoveSandwormDemand(List<MapField> list)
        {
            List<Position> path = new List<Position>();
            foreach (MapField mapField in list)
            {
                Position position = new Position(mapField.XCoordinate, mapField.ZCoordinate);
                path.Add(position);
            }
            SandwormMoveDemandMessage sandwormMoveDemandMessage = new SandwormMoveDemandMessage(path);
            NetworkController.HandleSendingMessage(sandwormMoveDemandMessage);
        }

        public void DoDespawnSandwormDemand()
        {
            SandwormDespawnDemandMessage sandwormDespawnDemandMessage = new SandwormDespawnDemandMessage();
            NetworkController.HandleSendingMessage(sandwormDespawnDemandMessage);
        }

        /// <summary>
        /// This method will be called, when the overlengthmechanism is aktive.
        /// </summary>
        public void DoEndGame()
        {
            EndGameMessage endGameMessage = new EndGameMessage();
            NetworkController.HandleSendingMessage(endGameMessage);
        }

        /// <summary>
        /// This message will be sent to the clients when the game ends.
        /// </summary>
        /// <param name="winnerID">ID of the winner of the party</param>
        /// <param name="loserID">ID of the loser of the party</param>
        /// <param name="stats">Repräsentation of the statistics of the Game</param>
        public void DoGameEndMessage(int winnerID, int loserID, Statistics stats)
        {
            GameEndMessage gameEndMessage = new GameEndMessage(winnerID, loserID, stats);
            NetworkController.HandleSendingMessage(gameEndMessage);
        }

        public void DoSendGameState(int clientID, int[] activlyPlayingIDs, String[] history)
        {
            GameStateMessage gameStateMessage = new GameStateMessage(history, activlyPlayingIDs, clientID);
            NetworkController.HandleSendingMessage(gameStateMessage);
        }

        /// <summary>
        /// increases the amount of strikes of a client and sends a strike message
        /// </summary>
        /// <param name="player">the player, who gets a strike</param>
        /// <param name="wrongMessage">the wrong message, who was send by the client</param>
        public void DoSendStrike(Player player, Message wrongMessage)
        {
            string wrongMessageAsString = MessageConverter.FromMessage(wrongMessage);
            player.AddStrike();
            StrikeMessage strikeMessage = new StrikeMessage(player.ClientID, wrongMessageAsString, player.AmountOfStrikes);
            NetworkController.HandleSendingMessage(strikeMessage);
        }

        public void DoGamePauseDemand(int requestedByClientID, bool pause)
        {
            PausGameDemandMessage gamePauseDemandMessage = new PausGameDemandMessage(requestedByClientID, pause);
            NetworkController.HandleSendingMessage(gamePauseDemandMessage);
        }

        public void OnUnpauseGameOffer(int requestedByClientID)
        {
            UnpauseGameOfferMessage unpauseGameOfferMessage = new UnpauseGameOfferMessage(requestedByClientID);
            NetworkController.HandleSendingMessage(unpauseGameOfferMessage);
        }
    }
}
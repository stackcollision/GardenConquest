
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Library.Utils;
using VRageMath;
using Interfaces = Sandbox.ModAPI.Interfaces;
using InGame = Sandbox.ModAPI.Ingame;

using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.Common.Components;
using Sandbox.Game.Entities;

using GardenConquest.Messaging;
using GardenConquest.Blocks;
using GardenConquest.Records;
using GardenConquest.Extensions;

namespace GardenConquest.Core {

	/// <summary>
	/// Core of the server.  Manages rounds and reward distribution.
	/// </summary>
	class Core_Server : Core_Base {

		#region Class Members

		private bool m_Initialized = false;
		private CommandProcessor m_CmdProc;
		private MyTimer m_RoundTimer;
		private MyTimer m_SaveTimer;
		private RequestProcessor m_MailMan;
		private ResponseProcessor m_LocalReceiver;
		private bool m_RoundEnded = false;

		private IMyPlayer m_Player;
		private int m_CurrentFrame;

		private static ConquestSettings s_Settings;
		private static bool s_DelayedSettingWrite = false;

		#endregion
		#region Inherited Methods

		/// <summary>
		/// Starts up the core on the server
		/// </summary>
		public override void initialize() {
			if (MyAPIGateway.Session == null || m_Initialized)
				return;

			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Server");
			log("Conquest core (Server) started");

			s_Settings = ConquestSettings.getInstance();
			s_DelayedSettingWrite = s_Settings.WriteFailed;
			// Start round timer
			m_RoundTimer = new MyTimer(s_Settings.CPPeriod * 1000, roundEnd);
			m_RoundTimer.Start();
			log("Round timer started with " + s_Settings.CPPeriod + " seconds");

			// Start save timer
			m_SaveTimer = new MyTimer(Constants.SaveInterval * 1000, saveTimer);
			m_SaveTimer.Start();
			log("Save timer started");

			m_MailMan = new RequestProcessor();
			
			// If the server is a player (non-dedicated) they also need to receive notifications
			if (!MyAPIGateway.Utilities.IsDedicated) {
				m_LocalReceiver = new ResponseProcessor(false);
				m_MailMan.localMsgSent += m_LocalReceiver.incomming;
				m_CmdProc = new CommandProcessor(m_LocalReceiver);
				m_CmdProc.initialize();
				m_LocalReceiver.requestSettings();
				m_Player = MyAPIGateway.Session.Player;
			}

			// Subscribe events
			GridEnforcer.OnPlacementViolation += eventPlacementViolation;
			GridEnforcer.OnCleanupViolation += eventCleanupViolation;
			GridEnforcer.OnCleanupTimerStart += eventCleanupTimerStart;
			GridEnforcer.OnCleanupTimerEnd += eventCleanupTimerEnd;
			ControlPoint.OnRewardsDistributed += notifyPlayersOfCPResults;

			m_CurrentFrame = 0;

			m_Initialized = true;
		}

		public override void unloadData() {
			log("Unloading", "unloadData");

			GridEnforcer.OnPlacementViolation -= eventPlacementViolation;
			GridEnforcer.OnCleanupViolation -= eventCleanupViolation;
			GridEnforcer.OnCleanupTimerStart -= eventCleanupTimerStart;
			GridEnforcer.OnCleanupTimerEnd -= eventCleanupTimerEnd;
			ControlPoint.OnRewardsDistributed -= notifyPlayersOfCPResults;

			if (m_LocalReceiver != null) {
				m_MailMan.localMsgSent -= m_LocalReceiver.incomming;
				m_LocalReceiver.unload();
				m_LocalReceiver = null;
			}

			m_MailMan.unload();

			if (!MyAPIGateway.Utilities.IsDedicated) m_CmdProc.shutdown();

			m_RoundTimer.Dispose();
			m_RoundTimer = null;
			m_SaveTimer.Dispose();
			m_SaveTimer = null;

			s_Logger = null;
		}

		public override void updateBeforeSimulation() {
			try {
				if (m_RoundEnded) {
					log("Round ended, distributing CP Rewards", "updateBeforeSimulation");
					foreach (ControlPoint cp in s_Settings.ControlPoints) {
						cp.distributeRewards();
					}
					m_RoundEnded = false;
				}
				if (s_DelayedSettingWrite) {
					log("Settings Write was delayed, trying again", "updateBeforeSimulation");
					s_Settings.writeSettings();
					if (!s_Settings.WriteFailed) {
						s_DelayedSettingWrite = false;
						log("Setting Write Success", "updateBeforeSimulation");
					}

				}

				// Performing this notification if server is player
				if (!MyAPIGateway.Utilities.IsDedicated) {
					if (m_CurrentFrame >= Constants.UpdateFrequency - 1) {
						if (m_Player.Controller.ControlledEntity is InGame.IMyShipController) {
							IMyCubeGrid currentControllerGrid = (m_Player.Controller.ControlledEntity as IMyCubeBlock).CubeGrid;
							IMyCubeBlock classifierBlock = currentControllerGrid.getClassifierBlock();
							if (classifierBlock != null && classifierBlock.OwnerId != m_Player.PlayerID && s_Settings.CommandsRequireClassifier) {
								MyAPIGateway.Utilities.ShowNotification("WARNING: Take control of the hull classifier or your position may be tracked!", 1250, MyFontEnum.Red);
							}
						}
						m_CurrentFrame = 0;
					}
					++m_CurrentFrame;
				}

			}
			catch (Exception e) {
				log("Error" + e, "updateBeforeSimulation", Logger.severity.ERROR);
			}
		}

		#endregion
		#region Event Handlers

		public void eventPlacementViolation(GridEnforcer ge, GridEnforcer.VIOLATION_TYPE v) {
			log("hit", "eventGridViolation");

			// Check for players within the vicinity of the grid, since there's no
			// built-in way to tell who just placed the block
			List<long> players = ge.Grid.getPlayerIDsWithinPlacementRadius();

			if (players.Count <= 0)
				return;
			
			string message = "";
			if (v == GridEnforcer.VIOLATION_TYPE.TOTAL_BLOCKS)
				message = "No more blocks allowed for this Class";
			else if (v == GridEnforcer.VIOLATION_TYPE.BLOCK_TYPE)
				message = "No more blocks of this type allowed for this Class";
			else if (v == GridEnforcer.VIOLATION_TYPE.TOO_MANY_CLASSIFIERS)
				message = "Only one Hull Classifier allowed";
			else if (v == GridEnforcer.VIOLATION_TYPE.SHOULD_BE_STATIC)
				message = "This classifier is only allowed on Stations";
			else if (v == GridEnforcer.VIOLATION_TYPE.TOO_MANY_OF_CLASS) {
				GridOwner.OWNER_TYPE owner_type = ge.Owner.OwnerType;
				if (owner_type == GridOwner.OWNER_TYPE.UNOWNED) {
					message = "Take ownership of this grid or it will eventually be removed.";
				}
				else if (owner_type == GridOwner.OWNER_TYPE.PLAYER) {
					message = "No more ships of this class allowed in this player's fleet. " +
						"Try joining a faction.";
				} else if (owner_type == GridOwner.OWNER_TYPE.FACTION) {
					message = "No more ships of this class allowed in this faction's fleet. ";
				}
			}

			log("Sending message", "eventPlacementViolation");
			NotificationResponse noti = new NotificationResponse() {
				NotificationText = message,
				Time = Constants.NotificationMillis,
				Font = MyFontEnum.Red,
				Destination = players,
				DestType = BaseResponse.DEST_TYPE.PLAYER
			};
			m_MailMan.send(noti);
		}

		public void eventCleanupViolation(GridEnforcer ge, List<GridEnforcer.VIOLATION> violations) {
			log("Start", "eventCleanupViolation");
			if (ge == null)
				return;

			log("Determine destination", "eventCleanupViolation");
			GridOwner owner = ge.Owner;
			GridOwner.OWNER_TYPE owner_type = owner.OwnerType;
			long gridFactionID = ge.Owner.FactionID;

			BaseResponse.DEST_TYPE destType = BaseResponse.DEST_TYPE.NONE;
			List<long> Destinations = new List<long>();
			string message = "";

			if (owner_type == GridOwner.OWNER_TYPE.FACTION) {
				destType = BaseResponse.DEST_TYPE.FACTION;
				Destinations.Add(gridFactionID);
				message += "Your faction's ";
			} else if (owner_type == GridOwner.OWNER_TYPE.PLAYER) {
				destType = BaseResponse.DEST_TYPE.PLAYER;
				Destinations.Add(ge.Owner.PlayerID);
				message += "Your ";
			} else {
				List<long> nearbyPlayers = ge.Grid.getPlayerIDsWithinPlacementRadius();
				if (nearbyPlayers.Count > 0) {
					destType = BaseResponse.DEST_TYPE.PLAYER;
					Destinations = nearbyPlayers;
					message += "Nearby unowned ";
				} else {
					return;
				}
			}

			message += "grid '" + ge.Grid.DisplayName + "' ";

			log("Build violations message", "eventCleanupViolation");
			if (violations != null) {
				message += "is violating: ";

				foreach (GridEnforcer.VIOLATION violation in violations)
					message += violation.Name + ": " + violation.Count + "/" +
						violation.Limit + " ";

				message += " and ";
			}

			log("Build time message", "eventCleanupViolation");
			int secondsUntilCleanup = ge.TimeUntilCleanup;

			message += "will have some blocks removed in " +
				Utility.prettySeconds(secondsUntilCleanup);

			// send
			log("Sending message", "eventDerelictStart");
			NotificationResponse noti = new NotificationResponse() {
				NotificationText = message,
				Time = Constants.NotificationMillis,
				Font = MyFontEnum.Red,
				Destination = Destinations,
				DestType = destType
			};
			m_MailMan.send(noti);
		}

		public void eventCleanupTimerStart(GridEnforcer ge, int secondsRemaining) {
			if (ge == null)
				return;

			GridOwner owner = ge.Owner;
			GridOwner.OWNER_TYPE owner_type = owner.OwnerType;
			long gridFactionID = ge.Owner.FactionID;

			BaseResponse.DEST_TYPE destType = BaseResponse.DEST_TYPE.NONE;
			List<long> Destinations = new List<long>();
			string message = "";

			if (owner_type == GridOwner.OWNER_TYPE.FACTION) {
				destType = BaseResponse.DEST_TYPE.FACTION;
				Destinations.Add(gridFactionID);
				message += "Your faction's ";
			}
			else if (owner_type == GridOwner.OWNER_TYPE.PLAYER) {
				destType = BaseResponse.DEST_TYPE.PLAYER;
				Destinations.Add(ge.Owner.PlayerID);
				message += "Your ";
			}
			else {
				List<long> nearbyPlayers = ge.Grid.getPlayerIDsWithinPlacementRadius();
				if (nearbyPlayers.Count > 0) {
					destType = BaseResponse.DEST_TYPE.PLAYER;
					Destinations = nearbyPlayers;
					message += "Nearby ";
				}
				else {
					return;
				}
			}
			log("msg details built", "eventCleanupTimerStart", Logger.severity.TRACE);

			// build notification
			message += "grid " + ge.Grid.DisplayName +
				" will have some of its offending blocks removed in " +
				Utility.prettySeconds(secondsRemaining);

			log("msg built, building noti", "eventDerelictStart");
			NotificationResponse noti = new NotificationResponse() {
				NotificationText = message,
				Time = Constants.NotificationMillis,
				Font = MyFontEnum.Red,
				Destination = Destinations,
				DestType = destType
			};
			log("notification built, sending message", "eventDerelictStart");
			m_MailMan.send(noti);
			log("Msg sent", "eventDerelictStart");
		}

		public void eventCleanupTimerEnd(GridEnforcer ge, DerelictTimer.COMPLETION c) {
			//log("start", "eventCleanupTimerEnd", Logger.severity.TRACE);
			if (ge == null)
				return;

			//log("grid exists, getting owner", "eventCleanupTimerEnd", Logger.severity.TRACE);
			GridOwner owner = ge.Owner;
			//log("grid exists, getting owner type", "eventCleanupTimerEnd", Logger.severity.TRACE);
			GridOwner.OWNER_TYPE owner_type = owner.OwnerType;
			//log("grid exists, getting faction", "eventCleanupTimerEnd", Logger.severity.TRACE);
			long gridFactionID = ge.Owner.FactionID;

			//log("determining destinations", "eventCleanupTimerEnd", Logger.severity.TRACE);
			BaseResponse.DEST_TYPE destType = BaseResponse.DEST_TYPE.NONE;
			List<long> Destinations = new List<long>();
			string message = "";

			if (owner_type == GridOwner.OWNER_TYPE.FACTION) {
				destType = BaseResponse.DEST_TYPE.FACTION;
				Destinations.Add(gridFactionID);
				message += "Your faction's ";
			}
			else if (owner_type == GridOwner.OWNER_TYPE.PLAYER) {
				destType = BaseResponse.DEST_TYPE.PLAYER;
				Destinations.Add(ge.Owner.PlayerID);
				message += "Your ";
			}
			else {
				List<long> nearbyPlayers = ge.Grid.getPlayerIDsWithinPlacementRadius();
				if (nearbyPlayers.Count > 0) {
					destType = BaseResponse.DEST_TYPE.PLAYER;
					Destinations = nearbyPlayers;
					message += "Nearby ";
				}
				else {
					return;
				}
			}

			log("building message", "eventCleanupTimerEnd", Logger.severity.TRACE);
			MyFontEnum font = MyFontEnum.Red;
			if (c == DerelictTimer.COMPLETION.CANCELLED) {
				message += "grid " + ge.Grid.DisplayName + " is now within limits.";
				font = MyFontEnum.Green;
			} else if (c == DerelictTimer.COMPLETION.ELAPSED) {
				message += "grid " + ge.Grid.DisplayName +
					" had some of its offending blocks removed.";
				font = MyFontEnum.Red;
			}

			log("Sending message", "eventDerelictEnd");
			NotificationResponse noti = new NotificationResponse() {
				NotificationText = message,
				Time = Constants.NotificationMillis,
				Font = font,
				Destination = Destinations,
				DestType = destType
			};
			m_MailMan.send(noti);
		}

		/// <summary>
		/// Notify players within the CP whether they won, lost, or tied
		/// </summary>
		public void notifyPlayersOfCPResults(int rewardsDistributed,
			List<long> winningFleetIds, List<IMyPlayer> nearbyPlayers, ControlPoint cp) {
			bool tie = (winningFleetIds.Count > 1);
			long winningFleetId = 0;
			if (!tie) winningFleetId = winningFleetIds.First();

			var winningPlayers = new List<long>();
			var tiedPlayers = new List<long>();
			var losingPlayers = new List<long>();

			foreach (IMyPlayer player in nearbyPlayers) {
				long fleetID = player.FleetID();

				if (!tie && fleetID == winningFleetId) {
					winningPlayers.Add(player.PlayerID);
				} else if (tie && winningFleetIds.Contains(fleetID)) {
					tiedPlayers.Add(player.PlayerID);
				} else {
					losingPlayers.Add(player.PlayerID);
				}
			}

			if (winningPlayers.Count > 0) {
				notifyPlayers(String.Format(
					"You control {0} and received {1} licenses.", 
					cp.Name, rewardsDistributed),
					winningPlayers, MyFontEnum.Green);
			}
			if (tiedPlayers.Count > 0) {
				notifyPlayers(String.Format(
					"You tied for control of {0} and received no licenses.",
					cp.Name), tiedPlayers, MyFontEnum.Red);
			}
			if (losingPlayers.Count > 0) {
				notifyPlayers("Someone else controls " + cp.Name,
					losingPlayers, MyFontEnum.Red);
			}
		}

		#endregion
		#region Class Timer Events

		/// <summary>
		/// Sets the round ended flag.  Necessary because of synchonization issues
		/// </summary>
		private void roundEnd() {
			log("hit", "roundEnd");
			m_RoundEnded = true;
		}

		private void saveTimer() {
			log("Save timer triggered", "saveTimer");
			StateTracker.getInstance().saveState();
		}

		#endregion

		public void notifyPlayers(String msg, List<long> playerIDs, MyFontEnum color) {
			m_MailMan.send(new NotificationResponse() {
				NotificationText = msg,
				Time = Constants.NotificationMillis,
				Font = color,
				Destination = playerIDs,
				DestType = BaseResponse.DEST_TYPE.PLAYER
			});
		}
	}
}

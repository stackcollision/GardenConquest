
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
		private CommandProcessor m_CmdProc = null;
		private MyTimer m_RoundTimer = null;
		private MyTimer m_SaveTimer = null;
		private RequestProcessor m_MailMan = null;
		private ResponseProcessor m_LocalReceiver = null;

		private bool m_RoundEnded = false;
		//private Object m_SyncObject = new Object();

		private static readonly int INGAME_PLACEMENT_MAX_DISTANCE = 60;
		private static MyObjectBuilder_Component s_TokenBuilder = null;
		private static VRage.ObjectBuilders.SerializableDefinitionId? s_TokenDef = null;
		private static IComparer<FACGRID> s_Sorter = null;
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

			s_TokenBuilder = new MyObjectBuilder_Component() { SubtypeName = "ShipLicense" };
			s_TokenDef = new VRage.ObjectBuilders.SerializableDefinitionId(
				typeof(MyObjectBuilder_InventoryItem), "ShipLicense");
			s_Sorter = new GridSorter();

			s_Settings = ConquestSettings.getInstance();
			s_DelayedSettingWrite = s_Settings.WriteFailed;
			// Start round timer
			m_RoundTimer = new MyTimer(s_Settings.CPPeriod * 1000, roundEnd);
			m_RoundTimer.Start();
			log("Round timer started");

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
			}

			// Subscribe events
			GridEnforcer.OnPlacementViolation += eventPlacementViolation;
			GridEnforcer.OnCleanupViolation += eventCleanupViolation;
			GridEnforcer.OnCleanupTimerStart += eventCleanupTimerStart;
			GridEnforcer.OnCleanupTimerEnd += eventCleanupTimerEnd;

			m_Initialized = true;
		}

		public override void unloadData() {
			log("Unloading", "unloadData");

			GridEnforcer.OnPlacementViolation -= eventPlacementViolation;
			GridEnforcer.OnCleanupViolation -= eventCleanupViolation;
			GridEnforcer.OnCleanupTimerStart -= eventCleanupTimerStart;
			GridEnforcer.OnCleanupTimerEnd -= eventCleanupTimerEnd;

			if (m_LocalReceiver != null) {
				m_MailMan.localMsgSent -= m_LocalReceiver.incomming;
				m_LocalReceiver.unload();
				m_LocalReceiver = null;
			}

			m_MailMan.unload();

			if (!MyAPIGateway.Utilities.IsDedicated) m_CmdProc.shutdown();

			s_Logger = null;
		}

		public override void updateBeforeSimulation() {
			try {
				//lock (m_SyncObject) {
				if (m_RoundEnded) {
					distributeRewards();
					m_RoundEnded = false;
				}
				//}
				if (s_DelayedSettingWrite) {
					log("Settings Write was delayed, trying again", "updateBeforeSimulation");
					s_Settings.writeSettings();
					if (!s_Settings.WriteFailed) {
						s_DelayedSettingWrite = false;
						log("Setting Write Success", "updateBeforeSimulation");
					}

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
			List<long> players = getPlayersNearGrid(ge.Grid);
			
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
				Time = 5000,
				Font = MyFontEnum.Red,
				Destination = players,
				DestType = BaseResponse.DEST_TYPE.PLAYER
			};
			m_MailMan.send(noti);
		}

		public void eventCleanupViolation(GridEnforcer ge, List<GridEnforcer.VIOLATION> violations) {
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
			} else if (owner_type == GridOwner.OWNER_TYPE.PLAYER) {
				destType = BaseResponse.DEST_TYPE.PLAYER;
				Destinations.Add(ge.Owner.PlayerID);
				message += "Your ";
			} else {
				List<long> nearbyPlayers = getPlayersNearGrid(ge.Grid);
				if (nearbyPlayers.Count > 0) {
					destType = BaseResponse.DEST_TYPE.PLAYER;
					Destinations = nearbyPlayers;
					message += "Nearby unowned ";
				} else {
					return;
				}
			}


			// build notification
			message += "grid " + ge.Grid.DisplayName + " is violating:"; //: \n";

			foreach (GridEnforcer.VIOLATION violation in violations)
				message += violation.Name + ": " + violation.Count + "/" + 
					violation.Limit + "  ";

			int secondsUntilCleanup = ge.TimeUntilCleanup;

			if (secondsUntilCleanup > 0)
				message += "and will have some offending blocks removed in " +
					Utility.prettySeconds(secondsUntilCleanup);

			// send
			log("Sending message", "eventDerelictStart");
			NotificationResponse noti = new NotificationResponse() {
				NotificationText = message,
				Time = 6000,
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
				List<long> nearbyPlayers = getPlayersNearGrid(ge.Grid);
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
				Time = 6000,
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
				List<long> nearbyPlayers = getPlayersNearGrid(ge.Grid);
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
				Time = 6000,
				Font = font,
				Destination = Destinations,
				DestType = destType
			};
			m_MailMan.send(noti);
		}

		#endregion
		#region Class Timer Events

		/// <summary>
		/// Sets the round ended flag.  Necessary because of synchonization issues
		/// </summary>
		private void roundEnd() {
			log("hit", "roundEnd");
			//lock (m_SyncObject) {
				m_RoundEnded = true;
			//}
		}

		private void saveTimer() {
			log("Save timer triggered", "saveTimer");
			StateTracker.getInstance().saveState();
		}

		/// <summary>
		/// Called at the end of a round.  Distributes rewards to winning factions.
		/// </summary>
		private void distributeRewards() {
			log("Timer triggered", "roundEnd");

			try {
				if (!m_Initialized)
					return;

				// Check each CP in turn
				Dictionary<long, int> totalTokens = new Dictionary<long, int>();
				foreach (ControlPoint cp in s_Settings.ControlPoints) {
					log("Processing control point " + cp.Name, "roundEnd");

					// Get a list of all grids within this CPs sphere of influence
					List<IMyCubeGrid> gridsInSOI = getGridsInCPRadius(cp);
					log("Found " + gridsInSOI.Count + " grids in CP SOI", "roundEnd");

					// Group all of the grids in the SOI into their factions
					// This will only return grids which conform to the rules which make them valid
					// for counting.  All other grids discarded.
					Dictionary<long, List<FACGRID>> allFactionGrids = 
						groupFactionGrids(gridsInSOI, cp.Position);
					log("After aggregation there are " + allFactionGrids.Count + " factions present", "roundEnd");
					foreach (KeyValuePair<long, List<FACGRID>> entry in allFactionGrids) {
						log("Grids for faction " + entry.Key, "roundEnd");
						foreach (FACGRID grid in entry.Value) {
							log("\t" + grid.grid.Name, "roundEnd");
						}
					}

					// Now that we have an aggregation of grids for factions
					// in the SOI, we can decide who wins
					long greatestFaction = -1;
					int greatestTotal = -1;
					bool tie = false;
					foreach (KeyValuePair<long, List<FACGRID>> entry in allFactionGrids) {
						int weightedTotal = 0;
						foreach (FACGRID fg in entry.Value) {
							weightedTotal +=
								s_Settings.HullRules[(int)fg.hullClass].CaptureMultiplier;
						}

						if (weightedTotal >= greatestTotal) {
							tie = weightedTotal == greatestTotal;

							greatestFaction = entry.Key;
							greatestTotal = weightedTotal;
						}
					}

					log("Faction with most grids: " + greatestFaction, "roundEnd");
					log("Number of (weighted) grids: " + greatestTotal, "roundEnd");
					log("Tie? " + tie, "roundEnd");

					// If we have a tie, nobody gets the tokens
					// If we don't, award tokens to the faction with the most ships in the SOI
					if (greatestFaction != -1 && !tie) {
						// Deposit order:
						// 1. Largest station (by block count)
						// 2. If no stations, largest (by block count) large ship with cargo
						// 3. Otherwise largest (by block count) small ship with cargo

						// Sort the list by these rules ^
						log("Sorting list of grids", "roundEnd");
						List<FACGRID> grids = allFactionGrids[greatestFaction];
						grids.Sort(s_Sorter);

						//foreach (FACGRID g in grids) {
						//	log(g.grid.EntityId + " " + g.gtype + " " + g.blockCount);
						//}

						// Go through the sorted list and find the first ship with a cargo container
						// with space.  If the faction has no free cargo container they are S.O.L.
						log("Looking for valid container", "roundEnd");
						InGame.IMyCargoContainer container = null;
						foreach (FACGRID grid in grids) {
							container = grid.grid.getAvailableCargo(s_TokenDef, cp.TokensPerPeriod);
							if (container != null)
								break;
						}
						
						if (container != null) {
							// Award the tokens
							log("Found a ship to put tokens in", "roundEnd");
							((container as Interfaces.IMyInventoryOwner).GetInventory(0)
								as IMyInventory).AddItems(
								cp.TokensPerPeriod,
								s_TokenBuilder);

							// Track totals
							if (totalTokens.ContainsKey(greatestFaction)) {
								totalTokens[greatestFaction] += cp.TokensPerPeriod;
							} else {
								totalTokens.Add(greatestFaction, cp.TokensPerPeriod);
							}
						}
					}
				}

				// Anounce round ended
				log("Sending message", "roundEnd");
				NotificationResponse endedMessage = new NotificationResponse() {
					NotificationText = "Conquest Round Ended",
					Time = 10000,
					Font = MyFontEnum.White,
					Destination = null,
					DestType = BaseResponse.DEST_TYPE.EVERYONE
				};
				m_MailMan.send(endedMessage);

				// Report round results
				// For every faction that got rewards, tell them
				NotificationResponse rewardMessage = new NotificationResponse() {
					NotificationText = "",
					Time = 10000,
					Font = MyFontEnum.White,
					Destination = new List<long>() { 0 },
					DestType = BaseResponse.DEST_TYPE.FACTION
				};
				foreach (KeyValuePair<long, int> entry in totalTokens) {
					rewardMessage.NotificationText = "Your faction has been awarded " +
						entry.Value + " licenses";
					rewardMessage.Destination[0] = entry.Key;
					m_MailMan.send(endedMessage);
				}


			} catch (Exception e) {
				log("An exception occured: " + e, "roundEnd", Logger.severity.ERROR);
			}
		}

		#endregion
		#region Class Helpers



		/// <summary>
		/// Returns a list of players near a grid.  Used to send messages
		/// </summary>
		/// <param name="grid"></param>
		/// <returns></returns>
		private List<long> getPlayersNearGrid(IMyCubeGrid grid) {
			log("Getting players near grid " + grid.DisplayName);

			Vector3 gridPos = grid.GetPosition();
			VRageMath.Vector3 gridSize = grid.LocalAABB.Size;
			float gridMaxLength = 
				Math.Max(gridSize.X, Math.Max(gridSize.Y, gridSize.Z));
			int maxDistFromGrid = (int)gridMaxLength + INGAME_PLACEMENT_MAX_DISTANCE;

			List<IMyPlayer> allPlayers = new List<IMyPlayer>();
			MyAPIGateway.Players.GetPlayers(allPlayers);

			float pDistFromGrid = 0.0f;
			List<long> nearbyPlayerIds = new List<long>();
			foreach (IMyPlayer p in allPlayers)
			{
				pDistFromGrid = VRageMath.Vector3.Distance(p.GetPosition(), gridPos);
				if (pDistFromGrid < maxDistFromGrid) {
					nearbyPlayerIds.Add((long)p.PlayerID);
				}
			}

			log(nearbyPlayerIds.Count + " Nearby players: " + String.Join(" ,", nearbyPlayerIds));
			return nearbyPlayerIds;
		}

		/// <summary>
		/// Returns a list of grids in the vicinity of the CP
		/// </summary>
		/// <param name="cp">Control point to check</param>
		/// <returns></returns>
		private List<IMyCubeGrid> getGridsInCPRadius(ControlPoint cp) {
			// Get all ents within the radius
			VRageMath.BoundingSphereD bounds =
				new VRageMath.BoundingSphereD(cp.Position, (double)cp.Radius);
			List<IMyEntity> ents =
				MyAPIGateway.Entities.GetEntitiesInSphere(ref bounds);

			// Get only the ships/stations
			List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
			foreach (IMyEntity e in ents) {
				if (e is IMyCubeGrid)
					grids.Add(e as IMyCubeGrid);
			}

			return grids;
		}

		/// <summary>
		/// Separates a list of grids by their fleet.  Also discards invalid grids.
		/// </summary>
		/// <param name="grids">Grids to aggregate</param>
		/// <param name="cpPos">The position of the CP</param>
		/// <returns></returns>
		private Dictionary<long, List<FACGRID>> groupFactionGrids(List<IMyCubeGrid> grids, VRageMath.Vector3D cpPos) {
			Dictionary<long, List<FACGRID>> result = new Dictionary<long, List<FACGRID>>();

			foreach (IMyCubeGrid grid in grids) {
				// GridEnforcer
				GridEnforcer ge = grid.Components.Get<MyGameLogicComponent>() as GridEnforcer;
				if (ge == null) {
					log("No grid enforcer on grid " + grid.EntityId,
						"groupFactionGrids", Logger.severity.ERROR);
					continue;
				}

				// Owner
				ge.reevaluateOwnership();

				if (ge.Owner.OwnerType == GridOwner.OWNER_TYPE.UNOWNED) {
					log("Grid " + grid.EntityId + " is unowned, skipping",
						"groupFactionGrids");
					continue;
				}

				// Fleet
				long fleetID = ge.Owner.FleetID;
				if (ge.SupportedByFleet) {
					log("Grid " + grid.DisplayName + " belongs to fleet " + fleetID,
						"groupFactionGrids");
				} else {
					log("Grid " + grid.DisplayName + " is unsupported by its fleet " + fleetID +
					", skipping.", "groupFactionGrids");
					continue;
				}

				// Hull Classifier conditions for the grid to count:
				// 1. Must have a hull classifier
				HullClassifier classifier = ge.Classifier;
				if (classifier == null) {
					log("Grid has no classifier, skipping", "groupFactionGrids");
					continue;
				}

				// 2. HC must be working
				InGame.IMyBeacon beacon = classifier.FatBlock as InGame.IMyBeacon;
				if (beacon == null || !beacon.IsWorking) {
					log("Classifier beacon not working, skipping", "groupFactionGrids");
					continue;
				}

				// 3. HC must have a beacon radius greater than the distance to the grid
				if (beacon.Radius < VRageMath.Vector3.Distance(cpPos, grid.GetPosition())) {
					log("Classifier range too small, skipping", "groupFactionGrids");
					continue;
				}

				// The grid can be counted
				List<IMySlimBlock> blocks = new List<IMySlimBlock>();
				grid.GetBlocks(blocks);

				FACGRID fg = new FACGRID();
				fg.grid = grid;
				fg.blockCount = blocks.Count;
				fg.gtype = Utility.getGridType(grid);
				fg.hullClass = ge.Class;

				List<FACGRID> gridsOfCurrent = null;
				if (result.ContainsKey(fleetID)) {
					gridsOfCurrent = result[fleetID];
				} else {
					gridsOfCurrent = new List<FACGRID>();
					result.Add(fleetID, gridsOfCurrent);
				}
				gridsOfCurrent.Add(fg);
			}

			return result;
		}

		#endregion
	}
}

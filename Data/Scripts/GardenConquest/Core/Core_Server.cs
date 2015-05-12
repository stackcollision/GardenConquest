
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Library.Utils;
using Interfaces = Sandbox.ModAPI.Interfaces;
using InGame = Sandbox.ModAPI.Ingame;

using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.Common.Components;
using Sandbox.Game.Entities;

using GardenConquest.Messaging;
using GardenConquest.Blocks;
using GardenConquest.Records;

namespace GardenConquest.Core {

	/// <summary>
	/// Core of the server.  Manages rounds and reward distribution.
	/// </summary>
	class Core_Server : Core_Base {

		#region Class Members

		private bool m_Initialized = false;
		private MyTimer m_RoundTimer = null;
		private MyTimer m_SaveTimer = null;
		private RequestProcessor m_MailMan = null;

		private bool m_RoundEnded = false;
		//private Object m_SyncObject = new Object();

		private static MyObjectBuilder_Component s_TokenBuilder = null;
		private static Sandbox.Common.ObjectBuilders.Definitions.SerializableDefinitionId? s_TokenDef = null;
		private static IComparer<FACGRID> s_Sorter = null;

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
			s_TokenDef = new Sandbox.Common.ObjectBuilders.Definitions.SerializableDefinitionId(
				typeof(MyObjectBuilder_InventoryItem), "ShipLicense");
			s_Sorter = new GridSorter();

			log("Loading config");
			if (!ConquestSettings.getInstance().loadSettings())
				ConquestSettings.getInstance().loadDefaults();

			// Start round timer
			m_RoundTimer = new MyTimer(ConquestSettings.getInstance().Period * 1000, roundEnd);
			m_RoundTimer.Start();
			log("Round timer started");

			// Start save timer
			m_SaveTimer = new MyTimer(Constants.SaveInterval * 1000, saveTimer);
			m_SaveTimer.Start();
			log("Save timer started");

			m_MailMan = new RequestProcessor();

			// Subscribe events
			GridEnforcer.OnViolation += eventGridViolation;
			GridEnforcer.OnDerelictStart += eventDerelictStart;
			GridEnforcer.OnDerelictEnd += eventDerelictEnd;
			GridEnforcer.OnClassProhibited += eventClassProhibited;

			m_Initialized = true;
		}

		public override void unloadData() {
			log("Unloading", "unloadData");

			GridEnforcer.OnViolation -= eventGridViolation;
			GridEnforcer.OnDerelictStart -= eventDerelictStart;
			GridEnforcer.OnDerelictEnd -= eventDerelictEnd;
			GridEnforcer.OnClassProhibited -= eventClassProhibited;

			m_MailMan.unload();

			s_Logger = null;
		}

		public override void updateBeforeSimulation() {
			//lock (m_SyncObject) {
				if (m_RoundEnded) {
					distributeRewards();
					m_RoundEnded = false;
				}
			//}
		}

		#endregion
		#region Event Handlers

		public void eventGridViolation(GridEnforcer ge, GridEnforcer.VIOLATION_TYPE v) {
			// Check for players within the vicinity of the grid, since there's no
			// built-in way to tell who just placed the block
			List<long> players = getPlayersNearGrid(ge.Grid);
			
			string message = "";
			if(v == GridEnforcer.VIOLATION_TYPE.BLOCK)
				message = "Block limit reached";
			else
				message = "Turret limit reached";

			log("Sending message", "eventDerelictStart");
			NotificationResponse noti = new NotificationResponse() {
				NotificationText = message,
				Time = 4000,
				Font = MyFontEnum.Red,
				Destination = players,
				DestType = BaseMessage.DEST_TYPE.FACTION
			};
			m_MailMan.send(noti);
		}

		public void eventDerelictStart(ActiveDerelictTimer dt) {
			GridEnforcer ge = dt.Grid.Components.Get<MyGameLogicComponent>() as GridEnforcer;
			if (ge == null || ge.Faction == null)
				return;

			string message = "Your faction's grid " + dt.Grid.DisplayName + " will become a " +
				"derelict in " + ConquestSettings.getInstance().DerelictCountdown / 60.0f +
				" minutes";

			log("Sending message", "eventDerelictStart");
			NotificationResponse noti = new NotificationResponse() {
				NotificationText = message,
				Time = 10000,
				Font = MyFontEnum.Red,
				Destination = new List<long>() { ge.Faction.FactionId },
				DestType = BaseMessage.DEST_TYPE.FACTION
			};
			m_MailMan.send(noti);
		}

		public void eventDerelictEnd(ActiveDerelictTimer dt, ActiveDerelictTimer.COMPLETION c) {
			GridEnforcer ge =
				dt.Grid.Components.Get<MyGameLogicComponent>() as GridEnforcer;
			if (ge == null || ge.Faction == null)
				return;

			string message = "";
			MyFontEnum font = MyFontEnum.Red;

			if (c == ActiveDerelictTimer.COMPLETION.CANCELLED) {
				message = "Your faction's grid " + dt.Grid.DisplayName +
					" is no longer " +
					"in danger of becoming a derelict";
				font = MyFontEnum.Green;
			} else if (c == ActiveDerelictTimer.COMPLETION.ELAPSED) {
				message = "Your faction's grid " + dt.Grid.DisplayName +
					" has become a derelict";
				font = MyFontEnum.Red;
			}

			log("Sending message", "eventDerelictEnd");
			NotificationResponse noti = new NotificationResponse() {
				NotificationText = message,
				Time = 10000,
				Font = font,
				Destination = new List<long>() { ge.Faction.FactionId },
				DestType = BaseMessage.DEST_TYPE.FACTION
			};
			m_MailMan.send(noti);
		}

		public void eventClassProhibited(GridEnforcer ge, HullClass.CLASS c) {
			List<long> players = getPlayersNearGrid(ge.Grid);

			string message = "No more ships of class " + HullClass.ClassStrings[(int)c] +
				" permitted for this faction.";

			log("Sending message", "eventClassProhibited");
			NotificationResponse noti = new NotificationResponse() {
				NotificationText = message,
				Time = 10000,
				Font = MyFontEnum.Red,
				Destination = players,
				DestType = BaseMessage.DEST_TYPE.FACTION
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
				foreach (ControlPoint cp in ConquestSettings.getInstance().ControlPoints) {
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
							weightedTotal += HullClass.captureMultiplier[(int)fg.hullClass];
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
						InGame.IMyCargoContainer container =
							getFirstAvailableCargo(grids, cp.TokensPerPeriod);
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
				MyAPIGateway.Utilities.ShowNotification("Conquest Round Ended", 6000);
				NotificationResponse endedMessage = new NotificationResponse() {
					NotificationText = "Conquest Round Ended",
					Time = 10000,
					Font = MyFontEnum.White,
					Destination = null,
					DestType = BaseMessage.DEST_TYPE.EVERYONE
				};
				m_MailMan.send(endedMessage);

				// Report round results
				// For every faction that got rewards, tell them
				NotificationResponse rewardMessage = new NotificationResponse() {
					NotificationText = "",
					Time = 10000,
					Font = MyFontEnum.White,
					Destination = new List<long>() { 0 },
					DestType = BaseMessage.DEST_TYPE.FACTION
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
			VRageMath.Vector3 size = grid.LocalAABB.Size;
			float maxAxis = Math.Max(size.X, Math.Max(size.Y, size.Z));
			VRageMath.BoundingSphereD bounds =
				new VRageMath.BoundingSphereD(grid.GetPosition(), maxAxis * 2.0);
			List<IMyEntity> ents =
				MyAPIGateway.Entities.GetEntitiesInSphere(ref bounds);

			List<long> players = new List<long>();
			foreach (IMyEntity e in ents) {
				if (e is IMyPlayer)
					players.Add((e as IMyPlayer).PlayerID);
			}

			return players;
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
		/// Separates a list of grids by their faction.  Also discards invalid grids.
		/// </summary>
		/// <param name="grids">Grids to aggregate</param>
		/// <param name="cpPos">The position of the CP</param>
		/// <returns></returns>
		private Dictionary<long, List<FACGRID>> groupFactionGrids(List<IMyCubeGrid> grids, VRageMath.Vector3D cpPos) {
			Dictionary<long, List<FACGRID>> result = new Dictionary<long, List<FACGRID>>();

			foreach (IMyCubeGrid grid in grids) {
				GridEnforcer ge = grid.Components.Get<MyGameLogicComponent>() as GridEnforcer;
				if (ge == null)
					continue;

				IMyFaction fac = ge.Faction;

				// Player must be in a faction to get tokens
				if (fac == null)
					continue;

				List<IMySlimBlock> blocks = new List<IMySlimBlock>();
				grid.GetBlocks(blocks);

				// Conditions which must be met for the grid to count towards faction total:
				// 1. Must have a powered hull classifier
				// 2. HC beacon radius must be greater than the distance to the grid
				bool hasHC = false;
				bool radiusOK = false;

				InGame.IMyBeacon beacon = ge.Classifier as InGame.IMyBeacon;
				if (beacon == null)
					continue;

				hasHC = beacon != null && beacon.IsWorking;

				radiusOK = beacon.Radius >= VRageMath.Vector3.Distance(
					cpPos, grid.GetPosition());

				// If the grid doesn't pass the above conditions, skip it
				log("Grid " + grid.EntityId + ": " + hasHC + " " 
					+ radiusOK, "groupFactionGrids");
				if (!(hasHC && radiusOK))
					continue;

				// The grid can be counted
				FACGRID fg = new FACGRID();
				fg.grid = grid;
				fg.blockCount = blocks.Count;
				fg.gtype = Utility.getGridType(grid);
				fg.hullClass = ge.ActualClass;

				List<FACGRID> gridsOfCurrent = null;
				if (result.ContainsKey(fac.FactionId)) {
					gridsOfCurrent = result[fac.FactionId];
				} else {
					gridsOfCurrent = new List<FACGRID>();
					result.Add(fac.FactionId, gridsOfCurrent);
				}
				gridsOfCurrent.Add(fg);
			}

			return result;
		}

		/// <summary>
		/// Finds the first cargo container in the list of grids which can hold the reward.
		/// </summary>
		/// <param name="grids"></param>
		/// <param name="numTok"></param>
		/// <returns></returns>
		private InGame.IMyCargoContainer getFirstAvailableCargo(List<FACGRID> grids, int numTok) {
			// This list is sorted by preference rules
			// Find the first one which has a cargo container with space
			List<IMySlimBlock> containers = new List<IMySlimBlock>();
			foreach (FACGRID grid in grids) {
				log("Checking grid " + grid.grid.Name, "getFirstAvailableCargo");
				// Check if it has a cargo container
				grid.grid.GetBlocks(containers, x => x.FatBlock != null && x.FatBlock is InGame.IMyCargoContainer);
				if (containers.Count != 0) {
					log("Has containers", "getFirstAvailableCargo");
					// Find first container with space
					foreach (IMySlimBlock block in containers) {
						InGame.IMyCargoContainer c = block.FatBlock as InGame.IMyCargoContainer;
						Interfaces.IMyInventoryOwner invo = c as Interfaces.IMyInventoryOwner;
						Interfaces.IMyInventory inv = invo.GetInventory(0);
						// TODO: Check that it can fit
						//if (inv.CanItemsBeAdded(numTok, 
						//	(Sandbox.Common.ObjectBuilders.Definitions.SerializableDefinitionId)m_TokenDef)) {
						log("Can fit tokens", "getFirstAvailableCargo");
						return c;
						//}
					}
				}

				containers.Clear();
			}

			return null;
		}

		#endregion
	}
}

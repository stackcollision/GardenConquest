
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

using GardenConquest.Records;
using Sandbox.Common.Components;
using GardenConquest.Blocks;

namespace GardenConquest.Core {

	/// <summary>
	/// Core of the server.  Manages rounds and reward distribution.
	/// </summary>
	class Core_Server : Core_Base {

		#region Class Members

		private bool m_Initialized = false;
		private MyTimer m_RoundTimer = null;
		private MyTimer m_SaveTimer = null;
		private byte m_Frame = 0;

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

			if(s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Core");
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
	
			m_Initialized = true;
		}

		public override void unloadData() {


			s_Logger = null;
		}

		public override void updateBeforeSimulation() {
			// Do this only every 100 frames
			if (m_Frame++ > 100) {
				m_Frame = 0;

				// Check for new derelict timers
				StateTracker st = StateTracker.getInstance();
				while (st.newDerelictTimers()) {
					StateTracker.DERELICT_TIMER dt = st.nextNewDerelictTimer();
					
					// Alert the whole faction
					GridEnforcer enf = 
						dt.grid.Components.Get<MyGameLogicComponent>() as GridEnforcer;
					IMyFaction fac = enf.Faction;

					// If there is no faction only alert the player who owns it
					if (fac == null) {
						// Alert the big owner.  If no big owner, no one gets an alert
						if (dt.grid.BigOwners.Count > 0) {
							// TODO: send message
						}
					} else {
						if(MyAPIGateway.Multiplayer != null) {
							List<IMyPlayer> players = new List<IMyPlayer>();
							MyAPIGateway.Multiplayer.Players.GetPlayers(players);
							foreach (IMyPlayer p in players) {
								if (fac.IsMember(p.PlayerID)) {
									// TODO: send message
								}
							}
						}
					}

					// Add it to the persistent state
					
				}
			}
		}

		#endregion
		#region Class Timer Events

		/// <summary>
		/// Called at the end of a round.  Distributes rewards to winning factions.
		/// </summary>
		private void roundEnd() {
			log("Timer triggered", "roundEnd");

			try {
				if (!m_Initialized)
					return;

				// Check each CP in turn
				Dictionary<long, long> totalTokens = new Dictionary<long, long>();
				foreach (ControlPoint cp in ConquestSettings.getInstance().ControlPoints) {
					log("Processing control point " + cp.Name, "roundEnd");

					// Get a list of all grids within this CPs sphere of influence
					List<IMyCubeGrid> gridsInSOI = getGridsInCPRadius(cp);
					log("Found " + gridsInSOI.Count + " grids in CP SOI", "roundEnd");

					// Group all of the grids in the SOI into their factions
					// This will only return grids which conform to the rules which make them valid
					// for counting.  All other grids discarded.
					Dictionary<long, List<FACGRID>> allFactionGrids = groupFactionGrids(gridsInSOI);
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
					int mostGrids = -1;
					bool tie = false;
					foreach (KeyValuePair<long, List<FACGRID>> entry in allFactionGrids) {
						if (entry.Value.Count >= mostGrids) {
							tie = entry.Value.Count == mostGrids;

							greatestFaction = entry.Key;
							mostGrids = entry.Value.Count;
						}
					}

					log("Faction with most grids: " + greatestFaction, "roundEnd");
					log("Number of grids: " + mostGrids, "roundEnd");
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

				// Report round results
				MyAPIGateway.Utilities.ShowNotification("Conquest Round Ended");

			} catch (Exception e) {
				log("An exception occured: " + e, "roundEnd", Logger.severity.ERROR);
			}
		}

		private void saveTimer() {
			log("Save timer triggered", "saveTimer");
			StateTracker.getInstance().saveState();
		}

		#endregion

		#region Class Helpers

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
		/// <returns></returns>
		private Dictionary<long, List<FACGRID>> groupFactionGrids(List<IMyCubeGrid> grids) {
			Dictionary<long, List<FACGRID>> result = new Dictionary<long, List<FACGRID>>();

			foreach (IMyCubeGrid grid in grids) {
				// TODO: use full owners list
				if (grid.BigOwners.Count == 0)
					continue;
				long owner = grid.BigOwners[0];
				IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner);

				// Player must be in a faction to get tokens
				if (fac == null)
					continue;

				List<IMySlimBlock> blocks = new List<IMySlimBlock>();
				grid.GetBlocks(blocks);

				// Conditions which must be met for the grid to count towards faction total:
				// 1. Must be powered
				// 2. Must have a HullClassifier beacon on it
				// 3. HC beacon radius must be greater than the distance to the grid
				bool isPowered = false;
				bool hasHC = false;
				bool radiusOK = false;

				foreach (IMySlimBlock block in blocks) {
					IMyCubeBlock fat = block.FatBlock;

					if (fat != null) {
						if (fat is InGame.IMyReactor) {
							isPowered |= fat.IsFunctional && fat.IsWorking;
						} else if (fat is InGame.IMyBeacon) {
							if (fat.BlockDefinition.SubtypeName.Contains("HullClassifier")) {
								hasHC |= fat.IsFunctional;
								radiusOK |= true; // TODO
							}
						}
					}
				}

				// If the grid doesn't pass the above conditions, skip it
				if (!(isPowered && hasHC && radiusOK))
					continue;
				
				// The grid can be counted
				FACGRID fg = new FACGRID();
				fg.grid = grid;
				fg.blockCount = blocks.Count;
				fg.gtype = Utility.getGridType(grid);

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
			InGame.IMyCargoContainer result = null;

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
							result = c;
							break;
						//}
					}
				}

				containers.Clear();
			}

			return result;
		}

		#endregion
	}
}

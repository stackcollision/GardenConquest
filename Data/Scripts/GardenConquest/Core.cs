
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

namespace GardenConquest {

	[Sandbox.Common.MySessionComponentDescriptor(Sandbox.Common.MyUpdateOrder.BeforeSimulation)]
	class Core : Sandbox.Common.MySessionComponentBase {
		#region Structs

		private enum GRIDTYPE {
			STATION,
			LARGESHIP,
			SMALLSHIP
		}

		private struct FACGRID {
			public IMyCubeGrid grid;
			public long blockCount;
			public GRIDTYPE gtype;
		}

		#endregion

		#region Class Members

		private bool m_Initialized = false;
		private bool m_IsServer = false;
		private MyTimer m_Timer = null;

		private static Logger s_Logger = null;

		private static Core s_Instance = null;

		private static MyObjectBuilder_Component s_TokenBuilder = null;
		private static Sandbox.Common.ObjectBuilders.Definitions.SerializableDefinitionId? s_TokenDef = null;

		#endregion
		#region Class Lifecycle

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent) {
			base.Init(sessionComponent);
			initialize();
			log("Init");
		}

		public void initialize() {
			if (MyAPIGateway.Session == null || m_Initialized)
				return;

			if(s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Core");
			log("Conquest core started");

			s_TokenBuilder = new MyObjectBuilder_Component() { SubtypeName = "ShipLicense" };
			s_TokenDef = new Sandbox.Common.ObjectBuilders.Definitions.SerializableDefinitionId(
				typeof(MyObjectBuilder_InventoryItem), "ShipLicense");

			if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.MultiplayerActive) {
				m_IsServer = true;
			} else {
				m_IsServer = MyAPIGateway.Multiplayer.IsServer;
			}

			if (m_IsServer) {
				log("Loading config");
				if (!ConquestSettings.getInstance().loadSettings())
					ConquestSettings.getInstance().loadDefaults();

				// Start timer
				m_Timer = new MyTimer(ConquestSettings.getInstance().Period * 1000, timerTriggered);
				m_Timer.Start();
				log("Timer started");
			} else {
				MyAPIGateway.Utilities.MessageEntered += handleChatCommand;
			}

			m_Initialized = true;
			s_Instance = this;
		}

		#endregion
		#region SessionComponent Hooks

		public override void UpdateBeforeSimulation() {
			if (!m_Initialized)
				initialize();
		}

		protected override void UnloadData() {
			MyAPIGateway.Utilities.MessageEntered -= handleChatCommand;
		}

		private void handleChatCommand(string messageText, ref bool sendToOthers) {
			try {
				if (messageText[0] != '/')
					return;

				string[] cmd = 
					messageText.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
				if (cmd[0].ToLower() != "/gc")
					return;

				int numCommands = cmd.Length - 1;
				if (numCommands == 1) {
					if (cmd[1].ToLower() == "about") {
						// TODO
					}
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "handleChatCommand", Logger.severity.ERROR);
			}
		}

		#endregion
		#region Class Timer Events

		private void timerTriggered() {
			log("Timer triggered", "timerTriggered");

			try {
				// Only the host processes this stuff
				if (!m_Initialized || !m_IsServer)
					return;

				// Check each CP in turn
				Dictionary<long, long> totalTokens = new Dictionary<long, long>();
				foreach (ControlPoint cp in ConquestSettings.getInstance().ControlPoints) {
					log("Processing control point " + cp.Name, "timerTriggered");

					// Get a list of all grids within this CPs sphere of influence
					List<IMyCubeGrid> gridsInSOI = getGridsInCPRadius(cp);
					log("Found " + gridsInSOI.Count + " grids in CP SOI", "timerTriggered");

					// Group all of the grids in the SOI into their factions
					// This will only return grids which conform to the rules which make them valid
					// for counting.  All other grids discarded.
					Dictionary<long, List<FACGRID>> allFactionGrids = groupFactionGrids(gridsInSOI);
					log("After aggregation there are " + allFactionGrids.Count + " factions present", "timerTriggered");
					foreach (KeyValuePair<long, List<FACGRID>> entry in allFactionGrids) {
						log("Grids for faction " + entry.Key, "timerTriggered");
						foreach (FACGRID grid in entry.Value) {
							log("\t" + grid.grid.Name, "timerTriggered");
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

					log("Faction with most grids: " + greatestFaction, "timerTriggered");
					log("Number of grids: " + mostGrids, "timerTriggered");
					log("Tie? " + tie, "timerTriggered");

					// If we have a tie, nobody gets the tokens
					// If we don't, award tokens to the faction with the most ships in the SOI
					if (greatestFaction != -1 && !tie) {
						// Deposit order:
						// 1. Largest station (by block count)
						// 2. If no stations, largest (by block count) large ship with cargo
						// 3. Otherwise largest (by block count) small ship with cargo

						// Sort the list by these rules ^
						log("Sorting list of grids", "timerTriggered");
						List<FACGRID> grids = allFactionGrids[greatestFaction];
						grids.Sort(delegate(FACGRID A, FACGRID B) {
							// TODO: unfuck this
							if (A.gtype == GRIDTYPE.STATION) {
								if (B.gtype == GRIDTYPE.STATION) {
									// Resolve by block count
									if (A.blockCount > B.blockCount)
										return -1;
									else if (A.blockCount == B.blockCount)
										return 0;
									else
										return 1;
								} else {
									// Stations go first no matter what
									return -1;
								}
							} else if (A.gtype == GRIDTYPE.LARGESHIP) {
								if (B.gtype == GRIDTYPE.STATION) {
									// B is a station so it goes first
									return 1;
								} else if (B.gtype == GRIDTYPE.LARGESHIP) {
									// Resolve by block count
									if (A.blockCount > B.blockCount)
										return -1;
									else if (A.blockCount == B.blockCount)
										return 0;
									else
										return 1;
								} else {
									// B is a small ship, goes last
									return -1;
								}
							} else {
								if (B.gtype == GRIDTYPE.SMALLSHIP) {
									// Resolve by block count
									if (A.blockCount > B.blockCount)
										return -1;
									else if (A.blockCount == B.blockCount)
										return 0;
									else
										return 1;
								} else {
									// A is a small ship and B isn't, so A goes last
									return 1;
								}
							}
						});

						// Go through the sorted list and find the first ship with a cargo container
						// with space.  If the faction has no free cargo container they are S.O.L.
						log("Looking for valid container", "timerTriggered");
						InGame.IMyCargoContainer container = 
							getFirstAvailableCargo(grids, cp.TokensPerPeriod);
						if (container != null) {
							// Award the tokens
							log("Found a ship to put tokens in", "timerTriggered");
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
				// TODO
			} catch (Exception e) {
				log("An exception occured: " + e, "timerTriggered", Logger.severity.ERROR);
			}
		}

		#endregion

		#region Class Helpers

		private List<IMyCubeGrid> getGridsInCPRadius(ControlPoint cp) {
			// Get all ents within the radius
			VRageMath.BoundingSphereD bounds = 
				new VRageMath.BoundingSphereD(cp.Position, (double)cp.Radius);
			List<IMyEntity> ents = MyAPIGateway.Entities.GetEntitiesInSphere(ref bounds);

			// Get only the ships/stations
			List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
			foreach (IMyEntity e in ents) {
				if (e is IMyCubeGrid)
					grids.Add(e as IMyCubeGrid);
			}

			return grids;
		}

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
				fg.gtype = getGridType(grid);

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

		private GRIDTYPE getGridType(IMyCubeGrid grid) {
			if (grid.IsStatic) {
				return GRIDTYPE.STATION;
			} else {
				if (grid.GridSizeEnum == MyCubeSize.Large)
					return GRIDTYPE.LARGESHIP;
				else
					return GRIDTYPE.SMALLSHIP;
			}
		}

		private int sortGridsByRules(IMyCubeGrid x, IMyCubeGrid y) {
			return 0;
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}

		#endregion
	}
}

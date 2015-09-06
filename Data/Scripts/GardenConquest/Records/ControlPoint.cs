using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using InGame = Sandbox.ModAPI.Ingame;
using VRage.ModAPI;

using GardenConquest.Blocks;
using GardenConquest.Extensions;
using GardenConquest.PhysicalObjects;

namespace GardenConquest.Records {
	[XmlType("CP")]
	public class ControlPoint {

		public class Subfleet {
			public long ID = 0;
			public List<GridEnforcer> Enforcers = new List<GridEnforcer>();
			public int TotalValue = 0;
		}

		#region Static

		private static Logger s_Logger = new Logger("ControlPoint", "Static");

		private static void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			s_Logger.log(level, method, message);
		}

		private static Action<int, List<long>, List<IMyPlayer>, ControlPoint> actionDistributeRewards;
		public static event Action<int, List<long>, List<IMyPlayer>, ControlPoint> OnRewardsDistributed {
			add { actionDistributeRewards += value; }
			remove { actionDistributeRewards -= value; }
		}
		private static void notifyRewardsDistributed(int distributed, List<long> winningFleets,
			List<IMyPlayer> nearbyPlayers, ControlPoint cp) {
			if (actionDistributeRewards != null)
				actionDistributeRewards(distributed, winningFleets, nearbyPlayers, cp);
		}

		#endregion
		#region Instance Members

		[XmlElement("Name")]
		public String Name { get; set; }
		[XmlElement("Position")]
		public VRageMath.Vector3D Position { get; set; }
		[XmlElement("Radius")]
		public int Radius { get; set; }
		[XmlElement("Reward")]
		public int TokensPerPeriod { get; set; }

		#endregion

		#region Serialization

		public void serialize(VRage.ByteStream stream) {
			stream.addLong((long)Position.X);
			stream.addLong((long)Position.Y);
			stream.addLong((long)Position.Z);
			stream.addString(Name);
			stream.addLong(Radius);
			stream.addLong(TokensPerPeriod);
		}

		public static ControlPoint deserialize(VRage.ByteStream stream) {
			ControlPoint result = new ControlPoint();

			long x, y, z;
			x = stream.getLong();
			y = stream.getLong();
			z = stream.getLong();
			result.Position = new VRageMath.Vector3D(x, y, z);

			result.Name = stream.getString();
			result.Radius = (int)stream.getLong();
			result.TokensPerPeriod = (int)stream.getLong();

			return result;
		}

		#endregion
		#region Reward Distribution

		/// <summary>
		/// Called at the end of a round.  Distributes rewards to winning factions.
		/// </summary>
		public void distributeRewards() {
			if (!Utility.isServer()) return;

			log("Distributing rewards for CP " + Name, "distributeRewards");

			// group nearby grids into their corresponding fleets
			Dictionary<long, Subfleet> subfleets = nearbySubfleets();

			// if there are no grids, nothing to do
			if (subfleets.Keys.Count == 0) return;

			// determine which subfleet won
			List<long> winningSubfleets = winningSubfleetIDs(subfleets);

			log("Winning fleets: " + String.Join(",", winningSubfleets.ToArray()), "distributeRewards");

			// distribute rewards
			int remainingReward = TokensPerPeriod;
			//var rewardsByFleet = new Dictionary<long, int>();
			if (winningSubfleets.Count == 1) {
				long winningFleetID = winningSubfleets.First();
				Subfleet winningFleet = subfleets[winningFleetID];

				// Place them in grids in order of decreasing multiplier
				winningFleet.Enforcers.Sort((a, b) =>
					(int)a.CaptureMultiplier.CompareTo((int)b.CaptureMultiplier));

				foreach (GridEnforcer ge in winningFleet.Enforcers) {
					if (remainingReward > 0) {
						log(String.Format("Attempting to place {0} licenses in {1}", remainingReward, ge.Grid.DisplayName), "distributeRewards");
						remainingReward = ge.Grid.placeInCargo(
							ShipLicense.Definition, 
							ShipLicense.Builder, 
							remainingReward);
					}
					else {
						break;
					}
				}
			}

			log("Unplaced reward: " + remainingReward, "distributeRewards");

			// notify players
			notifyRewardsDistributed(TokensPerPeriod - remainingReward, winningSubfleets, nearbyPlayers(), this);
		}

		/// <summary>
		/// Find all players within the CP
		/// </summary>
		private List<IMyPlayer> nearbyPlayers() {
			return MyAPIGateway.Players.getPlayersNearPoint(Position, Radius);
		}

		/// <summary>
		/// Group grids in the CP into subfleets
		/// </summary>
		private Dictionary<long, Subfleet> nearbySubfleets() {
			var foundSubfleets = new Dictionary<long, Subfleet>();
			log("Grouping nearby grids into Subfleets.", "nearbySubfleets");

			VRageMath.BoundingSphereD bounds = new VRageMath.BoundingSphereD(Position, (double)Radius);
			List<IMyEntity> entitiesInBounds = MyAPIGateway.Entities.GetEntitiesInSphere(ref bounds);

			foreach (IMyEntity e in entitiesInBounds) {

				// Is it a grid?
				IMyCubeGrid grid = e as IMyCubeGrid;
				if (grid == null) continue;

				// does it have a GE?
				GridEnforcer ge = grid.Components.Get<MyGameLogicComponent>() as GridEnforcer;
				if (ge == null) {
					log("Failed to retrieve GridEnforcer for grid " + grid.EntityId,
						"nearbySubfleets", Logger.severity.ERROR);
					continue;
				}

				// Is it classified?
				if (ge.Class == HullClass.CLASS.UNCLASSIFIED) {
					continue;
				}


				// There are no hooks to check if someone changed factions,
				// so reevaluate here to make sure info is up to date for fleet groups
				ge.reevaluateOwnership();

				/*
				if (ge.Owner.OwnerType == GridOwner.OWNER_TYPE.UNOWNED) {
					log("Grid " + grid.EntityId + " is unowned, skipping",
						"nearbySubfleets");
					continue;
				}
				*/

				// We could check here if the grid is supported by its fleet,
				// or more generally if it's violation any rules
				// But we should notify players, b/c that could be confusing
				/*
				if (!ge.SupportedByFleet) {
					log("Grid " + grid.DisplayName + " is unsupported by its fleet, skipping.", 
						"getRoundWinner");
					continue;
				}
				*/

				// Is its Hull Classifier broadcasting far enough?
				HullClassifier classifier = ge.Classifier;
				if (classifier == null) {
					log("Grid has no classifier but was classified",
						"nearbySubfleets", Logger.severity.ERROR);
					continue;
				}
				InGame.IMyRadioAntenna antenna = classifier.FatBlock as InGame.IMyRadioAntenna;
				if (antenna == null) {
					log("Classifier could not be referenced as antenna",
						"nearbySubfleets", Logger.severity.ERROR);
					continue;
				}
				if (!antenna.IsWorking) {
					log("Classifier antenna not working but grid was classified",
						"nearbySubfleets", Logger.severity.ERROR);
					continue;
				}
				
				if (antenna.Radius < VRageMath.Vector3.Distance(Position, grid.GetPosition())) {
					log("Classifier range too small, skipping", "nearbySubfleets");
					// TODO notify pilot
					continue;
				}

				// Grid passed all tests!
				long fleetID = ge.Owner.FleetID;
				log("Grid '" + ge.Grid.DisplayName + "'passed all tests, including in fleet " + fleetID, "nearbySubfleets");
				if (!foundSubfleets.ContainsKey(fleetID)) {
					foundSubfleets[fleetID] = new Subfleet {
						ID = fleetID,
						Enforcers = new List<GridEnforcer>() { ge },
						TotalValue = ge.CaptureMultiplier,
					};
				} else {
					foundSubfleets[fleetID].Enforcers.Add(ge);
					foundSubfleets[fleetID].TotalValue += ge.CaptureMultiplier;
				}
			}

			return foundSubfleets;     
		}

		/// <summary>
		/// Return the FleetIDs for each winning subfleet in subfleets
		/// </summary>
		private List<long> winningSubfleetIDs(Dictionary<long, Subfleet> subfleets) {
			var winningSubfleets = new List<long>();
			int winningFleetValue = 0;
			int currentValue;

			foreach (KeyValuePair<long, Subfleet> entry in subfleets) {
				currentValue = entry.Value.TotalValue;
				if (currentValue == winningFleetValue) {
					// tie
					winningSubfleets.Add(entry.Key);
				}
				else if (currentValue > winningFleetValue) {
					// new winner
					winningSubfleets.Clear();
					winningSubfleets.Add(entry.Key);
					winningFleetValue = currentValue;
				}
			}

			return winningSubfleets;
		}

		#endregion

	}
}

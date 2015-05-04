using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Definitions;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Serializer;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;
using Interfaces = Sandbox.ModAPI.Interfaces;
using InGame = Sandbox.ModAPI.Ingame;

namespace GardenConquest {
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid))]
	public class GridEnforcer : MyGameLogicComponent {

		private IMyCubeGrid m_Grid = null;
		public InGame.IMyBeacon m_Classifier { get; private set; }
		private int m_BlockCount = 0;
		private int m_TurretCount = 0;
		private HullClass.CLASS m_Class = HullClass.CLASS.UNCLASSIFIED;
		private bool m_BeyondFirst100 = false;

		private Logger m_Logger = null;

		public GridEnforcer() {
			m_Classifier = null;
		}

		public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
			base.Init(objectBuilder);
			m_Grid = Entity as IMyCubeGrid;

			// If this is not the server we don't need this class.
			// When we modify the grid on the server the changes should be
			// sent to all clients
			// Can we remove components?  Let's find out
			if (MyAPIGateway.Multiplayer != null &&
				MyAPIGateway.Multiplayer.MultiplayerActive &&
				!MyAPIGateway.Multiplayer.IsServer
			) {
				m_Grid.Components.Remove<GridEnforcer>();
				return;
			}

			m_Logger = new Logger(Entity.Name, "GridEnforcer");
			log("Loaded into new grid");


			// We need to only turn on our rule checking after startup. Otherwise, if
			// a beacon is destroyed and then the server restarts, all but the first
			// 25 blocks will be deleted on startup.
			m_Grid.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

			// Get the initial block count
			// TODO: Is this really necessary?  Will this always be 0?
			List<IMySlimBlock> blocks = new List<IMySlimBlock>();
			m_Grid.GetBlocks(blocks);
			m_BlockCount = blocks.Count;

			m_Grid.OnBlockAdded += blockAdded;
			m_Grid.OnBlockRemoved += blockRemoved;

			// TODO: Start a timer for unclassified grids.  Timer will be stopped when
			// a classifier is detected
		}

		private void Close(IMyEntity ent) {
			log("Grid closed", "Close");

			m_Grid.OnBlockAdded -= blockAdded;
			m_Grid.OnBlockRemoved -= blockRemoved;

			m_Grid = null;
		}

		public override void UpdateBeforeSimulation100() {
			// NOTE: Can't turn off this update, because other scripts might also want it
			if (!m_BeyondFirst100) {
				m_BeyondFirst100 = true;

				// Once the server has loaded all block for this grid, check if we are
				// classified.  If not, warn the owner about the timer
				// TODO
			}
		}

		private void blockAdded(IMySlimBlock added) {
			m_BlockCount++;
			log("Block added to grid.  Count now: " + m_BlockCount, "blockAdded");
			if (added.FatBlock != null && (
					added.FatBlock is InGame.IMyLargeGatlingTurret ||
					added.FatBlock is InGame.IMyLargeMissileTurret
			)) {
				m_TurretCount++;
				log("Turret count now: " + m_TurretCount, "blockAdded");
			}

			// Check if its a class beacon
			if (added.FatBlock != null &&
				added.FatBlock is InGame.IMyBeacon &&
				added.FatBlock.BlockDefinition.SubtypeName.Contains("HullClassifier")
			) {
				// Is this grid already classified?
				if (m_Class != HullClass.CLASS.UNCLASSIFIED) {
					log("Grid is already classified.  Removing this new one.", "blockAdded");
					m_Grid.RemoveBlock(added);
				} else {
					m_Class = HullClass.hullClassFromString(
						added.FatBlock.BlockDefinition.SubtypeName);
					m_Classifier = added.FatBlock as InGame.IMyBeacon;
					log("Hull has been classified as " +
						HullClass.ClassStrings[(int)m_Class], "blockAdded");
				}

				// Return after classification
				// It is recommended that the distance between the unclassified block limit and 
				// the fighter/corvette block limits be substantial to prevent an issue
				// where as soon as the hull is classified it's over the limit.
				return;
			}

			// Check if we are violating class rules
			if (checkRules()) {
				m_Grid.RemoveBlock(added);
			}
		}

		// Returns true if any rule is violated
		private bool checkRules() {
			// Don't apply rules until server startup is completed
			if (!m_BeyondFirst100)
				return false;

			HullRule r = ConquestSettings.getInstance().HullRules[(int)m_Class];

			// Check general block count limit
			if (m_BlockCount > r.MaxBlocks) {
				log("Grid has violated block limit for class", "blockAdded");
				// TODO: Get a message to the player who placed it
				return true;
			}

			// Check number of turrets
			if (m_TurretCount > r.MaxTurrets) {
				log("Grid has violated turret limit for class", "blockAdded");
				// TODO: Get a message to the player who placed it
				return true;
			}

			return false;
		}

		private void blockRemoved(IMySlimBlock removed) {
			m_BlockCount--;
			log("Block removed from grid.  Count now: " + m_BlockCount, "blockAdded");

			// Check if the removed block was the class beacon
			if (removed.FatBlock != null &&
				removed.FatBlock is InGame.IMyBeacon &&
				removed.FatBlock.BlockDefinition.SubtypeName.Contains("HullClassifier")
			) {
				// If the classifier was removed change the class and start the timer
				m_Class = HullClass.CLASS.UNCLASSIFIED;
				// TODO: start timer
			}
		}

		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false) {
			return Entity.GetObjectBuilder();
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}
	}
}

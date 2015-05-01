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
	public class HullClassifier : MyGameLogicComponent {

		private IMyCubeGrid m_Grid = null;
		public InGame.IMyBeacon m_Classifier { get; private set; }
		private int m_BlockCount = 0;
		private HullClass.CLASS m_Class = HullClass.CLASS.UNCLASSIFIED;

		private Logger m_Logger = null;

		public HullClassifier() {
			m_Classifier = null;
		}

		public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
			base.Init(objectBuilder);
			m_Grid = Entity as IMyCubeGrid;

			m_Logger = new Logger(Entity.Name, "HullClassifier");
			log("Loaded into new grid");

			// Get the initial block count
			List<IMySlimBlock> blocks = new List<IMySlimBlock>();
			m_Grid.GetBlocks(blocks);
			m_BlockCount = blocks.Count;
			log("Block count at load: " + m_BlockCount, "Init");

			// See if this grid is classifier
			foreach (IMySlimBlock b in blocks) {
				if (b.FatBlock != null && b.FatBlock is InGame.IMyBeacon) {
					if (b.FatBlock.BlockDefinition.SubtypeName.Contains("HullClassifier")) {
						// Are we already classified?
						// TODO: If there are two classification blocks on a grid what do we do??
						if (m_Class != HullClass.CLASS.UNCLASSIFIED) {
							log("This grid has more than one classifier on it",
								"Init", Logger.severity.ERROR);
						} else {
							m_Class = HullClass.hullClassFromString(
								b.FatBlock.BlockDefinition.SubtypeName);
						}
					}
				}
			}

			log("Grid initial classification: " + HullClass.ClassStrings[(int)m_Class], "Init");

			m_Grid.OnBlockAdded += blockAdded;
			m_Grid.OnBlockRemoved += blockRemoved;

			//if (m_Block.BlockDefinition.SubtypeName.Contains("HullClassifier")) {
			//	m_IsHullClass = true;
			//	m_Block.CubeGrid.OnBlockAdded += blockAdded;
			//	m_Block.CubeGrid.OnBlockRemoved += blockRemoved;

			//	// Get initial block count
			//	List<IMySlimBlock> blocks = new List<IMySlimBlock>();
			//	m_Block.CubeGrid.GetBlocks(blocks);
			//	m_BlockCount = blocks.Count;

			//} else {
			//	m_IsHullClass = false;
			//}
		}

		private void Close(IMyEntity ent) {
			log("Grid closed", "Close");

			m_Grid.OnBlockAdded -= blockAdded;
			m_Grid.OnBlockRemoved -= blockRemoved;

			m_Grid = null;
		}

		private void blockAdded(IMySlimBlock added) {
			m_BlockCount++;
			log("Block added to grid.  Count now: " + m_BlockCount, "blockAdded");

			// Check if its a class beacon
			if (added.FatBlock != null &&
				added.FatBlock is InGame.IMyBeacon &&
				added.FatBlock.BlockDefinition.SubtypeName.Contains("HullClassifier")
			) {
				// Is this grid already classified?
				if (m_Class != HullClass.CLASS.UNCLASSIFIED) {
					// TODO: multiple classifiers
				} else {
					m_Class = HullClass.hullClassFromString(
						added.FatBlock.BlockDefinition.SubtypeName);
					m_Classifier = added.FatBlock as InGame.IMyBeacon;
					log("Hull has been classified as " +
						HullClass.ClassStrings[(int)m_Class], "blockAdded");
				}
			}

			// Check if we are violating class rules
			if (m_Class != HullClass.CLASS.UNCLASSIFIED) {
				//HullRule r = [(int)m_Class];

				//if (m_BlockCount > r.maxBlocks) {
				//	log("Grid has violated block limit for class", "blockAdded");
				//	// TODO: how do we fix this?
				//}
			}
		}

		private void blockRemoved(IMySlimBlock removed) {
			m_BlockCount--;
			log("Block removed from grid.  Count now: " + m_BlockCount, "blockAdded");
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

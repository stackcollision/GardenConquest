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
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon))]
	public class HullClassifier : MyGameLogicComponent {
		private IMyCubeBlock m_Block = null;
		private InGame.IMyBeacon m_Beacon = null;
		private bool m_IsHullClass = false;

		private Logger m_Logger = null;

		public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
			base.Init(objectBuilder);
			m_Block = Entity as IMyCubeBlock;
			m_Beacon = m_Block as InGame.IMyBeacon;

			m_Logger = new Logger(m_Block.CubeGrid.DisplayName, "HullClassifier");
			log("Initializing");

			if (m_Block.BlockDefinition.SubtypeName.Contains("HullClassifier")) {
				m_IsHullClass = true;
				//m_Block.NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
			} else {
				m_IsHullClass = false;
			}
		}

		private void Close(IMyEntity ent) {
			m_Block = null;
			m_Beacon = null;
		}

		//public override void UpdateAfterSimulation100() {
		//	if (!m_IsHullClass)
		//		return;

		//	try {

		//		List<IMySlimBlock> containers = new List<IMySlimBlock>();
		//		m_Block.CubeGrid.GetBlocks(containers, collectContainers);
		//		log("Got " + containers.Count + " containers in the grid");

		//		foreach (IMySlimBlock b in containers) {
		//			InGame.IMyCargoContainer box = b as InGame.IMyCargoContainer;

		//			MyObjectBuilder_InventoryItem itemBuilder = new MyObjectBuilder_InventoryItem() {
		//				Amount = 1,
		//				Content = new MyObjectBuilder_Ore() { SubtypeName = "Stone" },
		//				PhysicalContent = new MyObjectBuilder_PhysicalObject()
		//			};

		//			//log("getting inventory");
		//			//IMyInventory inv = (IMyInventory)InGame.TerminalBlockExtentions.GetInventory(box, 0);
		//			//if (inv != null) {
		//			//	log("inventory gotten");
		//			//}
		//			Interfaces.IMyInventoryOwner owner = (Interfaces.IMyInventoryOwner)b.FatBlock;
		//			if (owner != null) {
		//				//log("owner was successfully converted");
		//				IMyInventory inventory = (IMyInventory)owner.GetInventory(0);
		//				if (inventory != null) {
		//					//log("inventory was gotten");
		//					inventory.AddItems(1, itemBuilder.PhysicalContent);
		//				}
		//			}
		//		}
		//	} catch (Exception e) {
		//		log("Exception occured: " + e);
		//	}
		//}

		//private bool collectContainers(IMySlimBlock block) {
		//	return block != null && block.FatBlock != null &&
		//		block.FatBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_CargoContainer);
		//}

		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false) {
			return Entity.GetObjectBuilder();
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Definitions;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Components;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;
using Interfaces = Sandbox.ModAPI.Interfaces;
using InGame = Sandbox.ModAPI.Ingame;

namespace GardenConquest.Blocks {
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_MergeBlock))]
	class MergeBlock : MyGameLogicComponent {

		//private IMyCubeGrid m_Grid = null;
		// Unfortunately, grid can change without this entity being re-initialized through an unmerge, so
		// we have to either get the grid from the merge block each time, or the GE would need to
		// let the MergeBlock know that it's been added to a new grid and should refresh
		// since we use this so infrequently, it seems simpler and more maintainable to go with the former
		private IMyCubeGrid Grid { get { return m_MergeBlock.CubeGrid as IMyCubeGrid; } }
		private InGame.IMyShipMergeBlock m_MergeBlock = null;

		private Logger m_Logger = null;

		public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
			base.Init(objectBuilder);
			m_MergeBlock = Container.Entity as InGame.IMyShipMergeBlock;
			//m_Grid = m_MergeBlock.CubeGrid as IMyCubeGrid;

			m_Logger = new Logger(m_MergeBlock.EntityId.ToString(), "MergeBlock");
			log("Attached to merge block", "Init");

			(m_MergeBlock as IMyShipMergeBlock).BeforeMerge += beforeMerge;
		}

		public override void Close() {
			log("Merge block closing", "Close");
			(m_MergeBlock as IMyShipMergeBlock).BeforeMerge -= beforeMerge;
		}

		private void beforeMerge() {
			GridEnforcer ge = Grid.Components.Get<MyGameLogicComponent>() as GridEnforcer;
			if (ge != null) {
				log("Merge about to occur.  Marking grid " + ge.Entity.EntityId.ToString(),
					"beforeMerge");
				ge.markForMerge();
			}
			else {
				log("GridEnforcer is null", "beforeMerge", Logger.severity.ERROR);
			}
		}

		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false) {
			return Container.Entity.GetObjectBuilder();
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}
	}
}

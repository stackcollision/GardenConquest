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

		private static Logger s_Logger = null;

		public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
			base.Init(objectBuilder);
			m_Block = Entity as IMyCubeBlock;
			m_Beacon = m_Block as InGame.IMyBeacon;

			if(s_Logger == null)
				s_Logger = new Logger(m_Block.CubeGrid.Name, "HullClassifier");
			log("Initializing");

			if (m_Block.BlockDefinition.SubtypeName.Contains("HullClassifier")) {
				m_IsHullClass = true;
			} else {
				m_IsHullClass = false;
			}
		}

		private void Close(IMyEntity ent) {
			m_Block = null;
			m_Beacon = null;
		}

		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false) {
			return Entity.GetObjectBuilder();
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

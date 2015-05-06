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
	class CoreComponent : Sandbox.Common.MySessionComponentBase {

		private Core_Base m_CoreProcessor = null;

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent) {
			base.Init(sessionComponent);

			if (m_CoreProcessor == null)
				startCore();
		}

		public override void UpdateBeforeSimulation() {
			base.UpdateBeforeSimulation();

			if (m_CoreProcessor == null)
				startCore();
		}

		protected override void UnloadData() {
			base.UnloadData();

			if (m_CoreProcessor != null)
				m_CoreProcessor.unloadData();
		}

		private void startCore() {
			if (Utility.isServer()) {
				m_CoreProcessor = new Core_Server();
			} else {
				m_CoreProcessor = new Core_Client();
			}

			m_CoreProcessor.initialize();
		}
	}

}

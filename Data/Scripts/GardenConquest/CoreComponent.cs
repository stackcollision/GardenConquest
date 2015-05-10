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

	/// <summary>
	/// Hooks into SE session.  Only a passthrough to launch Server or Client core.
	/// </summary>
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

			m_CoreProcessor.updateBeforeSimulation();
		}

		protected override void UnloadData() {
			base.UnloadData();

			if (m_CoreProcessor != null)
				m_CoreProcessor.unloadData();
		}

		/// <summary>
		/// Starts up the proper core process depending on whether we are a client or server.
		/// </summary>
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

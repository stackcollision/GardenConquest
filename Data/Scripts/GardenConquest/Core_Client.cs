using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI;

namespace GardenConquest {

	/// <summary>
	/// Core of the client
	/// </summary>
	public class Core_Client : Core_Base {
		#region Class Members

		private CommandProcessor m_CmdProc = null;

		#endregion
		#region Inherited Methods

		public override void initialize() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Client");

			// Chat command processor will already hook into message entered event
			m_CmdProc = new CommandProcessor();
			if (MyAPIGateway.Multiplayer != null)
				MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.GCMessageId, testHandler);
		}

		public override void unloadData() {
			// TODO
		}

		public override void updateBeforeSimulation() {
			// TODO
		}

		#endregion
		#region Hooks

		public void testHandler(byte[] buffer) {
			log(Encoding.Default.GetString(buffer), "testHandler");
		}

		#endregion
	}
}

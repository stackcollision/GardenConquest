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
		private ResponseProcessor m_MailMan = null;

		#endregion
		#region Inherited Methods

		public override void initialize() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Client");

			// Chat command processor will already hook into message entered event
			m_CmdProc = new CommandProcessor();
			m_MailMan = new ResponseProcessor();
		}

		public override void unloadData() {
			// TODO
		}

		public override void updateBeforeSimulation() {
			// TODO
		}

		#endregion
		#region Hooks

		#endregion
	}
}

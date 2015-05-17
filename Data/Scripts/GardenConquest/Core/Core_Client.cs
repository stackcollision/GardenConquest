using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.ModAPI;

using GardenConquest.Messaging;

namespace GardenConquest.Core {

	/// <summary>
	/// Core of the client
	/// </summary>
	public class Core_Client : Core_Base {
		#region Class Members

		private CommandProcessor m_CmdProc = null;
		private ResponseProcessor m_MailMan = null;

		private bool m_NeedCPGPS = false;

		#endregion
		#region Inherited Methods

		public override void initialize() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Client");

			m_CmdProc = new CommandProcessor();
			m_CmdProc.initialize();

			m_MailMan = new ResponseProcessor();

			// Request a fresh set of GPS coordinates for the CPs
			m_NeedCPGPS = !m_MailMan.requestCPGPS();
		}

		public override void unloadData() {
			log("Unloading", "unloadData");
			m_CmdProc.shutdown();
			m_MailMan.unload();
		}

		public override void updateBeforeSimulation() {
			if (m_NeedCPGPS) {
				m_NeedCPGPS = !m_MailMan.requestCPGPS();
			}
		}

		#endregion
		#region Hooks

		#endregion
	}
}

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
		private bool m_NeedSettings = true;

		#endregion
		#region Inherited Methods

		public override void initialize() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Client");

			m_MailMan = new ResponseProcessor();

			m_CmdProc = new CommandProcessor(m_MailMan);
			m_CmdProc.initialize();
		}

		public override void unloadData() {
			log("Unloading", "unloadData");
			m_CmdProc.shutdown();
			m_MailMan.unload();
		}

		public override void updateBeforeSimulation() {
			if (m_NeedSettings) {
				try {
					m_NeedSettings = !m_MailMan.requestSettings();
				} catch (Exception e) {
					log("Error" + e, "updateBeforeSimulation", Logger.severity.ERROR);
				}
			}
		}

		#endregion
		#region Hooks

		#endregion
	}
}

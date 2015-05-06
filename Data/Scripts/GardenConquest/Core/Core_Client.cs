using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GardenConquest.Core {

	/// <summary>
	/// Core of the client
	/// </summary>
	public class Core_Client : Core_Base {
		#region Class Members

		private CommandProcessor m_CmdProc = null;

		#endregion
		#region Inherited Methods

		public override void initialize() {
			// Chat command processor will already hook into message entered event
			m_CmdProc = new CommandProcessor();
		}

		public override void unloadData() {
			// TODO
		}

		#endregion
		#region Hooks

		#endregion
	}
}

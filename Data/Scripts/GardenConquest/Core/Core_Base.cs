using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GardenConquest.Core {

	/// <summary>
	/// Base class for GardenConquest Core processes
	/// </summary>
	public abstract class Core_Base {
		protected static Logger s_Logger = null;

		public abstract void initialize();
		public abstract void unloadData();

		protected void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

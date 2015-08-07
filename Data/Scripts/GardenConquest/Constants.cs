using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GardenConquest {

	/// <summary>
	/// Collection of constants
	/// </summary>
	public static class Constants {
		/// <summary>
		/// The message ID used to send messages between client and server
		/// </summary>
		public const ushort GCMessageId = 43501;

		/// <summary>
		/// Name of the configuration file
		/// </summary>
		public const String ConfigFileName = "GCConfig.xml";

		/// <summary>
		/// Name of the file persistent state is saved to
		/// </summary>	
		public const String StateFileName = "GCState.xml";

		/// <summary>
		/// Time between state saves (in seconds)
		/// </summary>
		public const int SaveInterval = 300;

		/// <summary>
		/// Current Version of Garden Conquest
		/// </summary>
		public const String Version = "1.0.9.5";

		/// <summary>
		/// Notification duration
		/// </summary>
		public const int NotificationMillis = 7500;
	}
}

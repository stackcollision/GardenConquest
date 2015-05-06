using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.ModAPI;

namespace GardenConquest {
	/// <summary>
	/// Static helper functions
	/// </summary>
	public static class Utility {

		/// <summary>
		/// Gets the hash value for the grid to identify it
		/// (because apparently DisplayName doesn't work)
		/// </summary>
		/// <param name="grid"></param>
		/// <returns></returns>
		public static String gridIdentifier(IMyCubeGrid grid) {
			String id = grid.ToString();
			int start = id.IndexOf('{');
			int end = id.IndexOf('}');
			return id.Substring(start + 1, end - start);
		}

		/// <summary>
		/// Checks if this session is the server
		/// </summary>
		/// <returns></returns>
		public static bool isServer() {
			// If multiplayer is inactive, we are the "server" in single player
			if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.MultiplayerActive)
				return true;
			else
				return MyAPIGateway.Multiplayer.IsServer;
		}
	}
}

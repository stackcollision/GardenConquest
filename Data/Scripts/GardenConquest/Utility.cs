using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.ModAPI;

namespace GardenConquest {
	public static class Utility {
		public static String gridIdentifier(IMyCubeGrid grid) {
			String id = grid.ToString();
			int start = id.IndexOf('{');
			int end = id.IndexOf('}');
			return id.Substring(start + 1, end - start);
		}

		public static bool isServer() {
			// If multiplayer is inactive, we are the "server" in single player
			if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.MultiplayerActive)
				return true;
			else
				return MyAPIGateway.Multiplayer.IsServer;
		}
	}
}

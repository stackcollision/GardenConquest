using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;

namespace GardenConquest {
	/// <summary>
	/// Static helper functions
	/// </summary>
	public static class Utility {
		public enum GRIDTYPE {
			STATION = 0,
			LARGESHIP = 1,
			SMALLSHIP = 2
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="grid"></param>
		/// <returns>Type of the grid</returns>
		public static GRIDTYPE getGridType(IMyCubeGrid grid) {
			if (grid.IsStatic) {
				return GRIDTYPE.STATION;
			} else {
				if (grid.GridSizeEnum == MyCubeSize.Large)
					return GRIDTYPE.LARGESHIP;
				else
					return GRIDTYPE.SMALLSHIP;
			}
		}

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
		/// <returns>True if this is a server</returns>
		/// <exception cref="NullReferenceException">Thrown is Multiplayer pointer is null.</exception>
		public static bool isServer() {
			if (MyAPIGateway.Multiplayer.MultiplayerActive == false)
				return true;
			else
				return MyAPIGateway.Multiplayer.IsServer;
		}

		public static void showDialog(string topic, string body, string button) {
			MyAPIGateway.Utilities.ShowMissionScreen("Garden Conquest", null, topic, body, null, button);
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using BuilderDefs = Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using InGame = Sandbox.ModAPI.Ingame;
using Interfaces = Sandbox.ModAPI.Interfaces;

using GardenConquest;
using GardenConquest.Records;

namespace GardenConquest.Extensions {

	/// <summary>
	/// Helper functions for SE Players
	/// </summary>
	public static class PlayerExtensions {


		public static bool IsAdmin(this IMyPlayer player) {
			if (Utility.isOffline())
				return true;

			if (player.IsHost())
				return true;

			if (player.isAuthenticatedAdmin())
				return true;

			return false;
		}

		public static bool isAuthenticatedAdmin(this IMyPlayer player) {
			try {
				var clients = MyAPIGateway.Session.GetCheckpoint("null").Clients;
				if (clients != null) {
					var client = clients.FirstOrDefault(
						c => c.SteamId == player.SteamUserId && c.IsAdmin);
					return (client != null);
				}
			}
			catch { }

			return false;
		}

		public static bool IsHost(this IMyPlayer player) {
			try {
				return MyAPIGateway.Multiplayer.IsServerPlayer(player.Client);
			}
			catch { }

			return false;
		}

        public static long FleetID(this IMyPlayer player) {
            return GridOwner.ownerFromPlayerID(player.PlayerID).FleetID;
        }

	}
}


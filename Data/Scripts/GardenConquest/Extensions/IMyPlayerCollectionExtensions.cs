using System;
using System.Collections.Generic;
using System.Text;

using Sandbox.ModAPI;
using VRageMath;

namespace GardenConquest.Extensions {

	/// <summary>
	/// Helper functions for SE Player Collection
	/// </summary>
	public static class IMyPlayerCollectionExtensions {

        private static Logger s_Logger = new Logger("IMyPlayerCollection", "Static");

        public static List<IMyPlayer> getPlayersNearPoint(this IMyPlayerCollection self, Vector3D point, float radius) {
            log("Getting players within " + radius + " of " + point, "getPlayersNearPoint");

            var allPlayers = new List<IMyPlayer>();
            self.GetPlayers(allPlayers);

            float distanceFromPoint = 0.0f;
            var nearbyPlayers = new List<IMyPlayer>();
            foreach (IMyPlayer player in allPlayers) {
                distanceFromPoint = VRageMath.Vector3.Distance(player.GetPosition(), point);
                if (distanceFromPoint < radius) {
                    nearbyPlayers.Add(player);
                }
            }

            log(nearbyPlayers.Count + " Nearby players.", "getPlayersNearPoint");
            return nearbyPlayers;
        }

        private static void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
            s_Logger.log(level, method, message);
        }

	}
}


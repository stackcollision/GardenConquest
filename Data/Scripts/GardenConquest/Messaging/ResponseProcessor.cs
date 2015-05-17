using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Library.Utils;
using Interfaces = Sandbox.ModAPI.Interfaces;
using InGame = Sandbox.ModAPI.Ingame;

namespace GardenConquest.Messaging {
	/// <summary>
	/// Client side message hooks.  Processed messages coming from the server.
	/// </summary>
	public class ResponseProcessor {
		private static Logger s_Logger = null;

		public ResponseProcessor() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "ResponseProcessor");

			log("Started", "ctor");
			if (MyAPIGateway.Multiplayer != null) {
				MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.GCMessageId, incomming);
			}
		}

		public void unload() {
			if (MyAPIGateway.Multiplayer != null)
				MyAPIGateway.Multiplayer.UnregisterMessageHandler(Constants.GCMessageId, incomming);
		}

		public void send(BaseRequest msg) {
			if (msg == null)
				return;

			byte[] buffer = msg.serialize();
			MyAPIGateway.Multiplayer.SendMessageToServer(Constants.GCMessageId, buffer);
			log("Sent packet of " + buffer.Length + " bytes", "send");
		}

		public bool requestCPGPS() {
			try {
				CPGPSRequest req = new CPGPSRequest();
				req.ReturnAddress = MyAPIGateway.Session.Player.PlayerID;
				send(req);
				return true;
			} catch (Exception e) {
				log("Exception occured: " + e, "requestCPGPS");
				return false;
			}
		}

		public void incomming(byte[] buffer) {
			log("Got message of size " + buffer.Length, "incomming");

			try {
				// Deserialize the message
				BaseResponse msg = BaseResponse.messageFromBytes(buffer);

				// Is this message even intended for us?
				if (msg.DestType == BaseResponse.DEST_TYPE.FACTION) {
					IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(
						MyAPIGateway.Session.Player.PlayerID);
					if (fac == null || !msg.Destination.Contains(fac.FactionId))
						return; // Message not meant for us
				} else if (msg.DestType == BaseResponse.DEST_TYPE.PLAYER) {
					if (!msg.Destination.Contains(MyAPIGateway.Session.Player.PlayerID))
						return; // Message not meant for us
				}

				switch (msg.MsgType) {
					case BaseResponse.TYPE.NOTIFICATION:
						processNotificationResponse(msg as NotificationResponse);
						break;

					case BaseResponse.TYPE.CPGPS:
						processCPGPSResponse(msg as CPGPSResponse);
						break;
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "incomming");
			}
		}

		private void processNotificationResponse(NotificationResponse noti) {
			log("Hit", "processNotificationResponse");
			MyAPIGateway.Utilities.ShowNotification(noti.NotificationText, noti.Time, noti.Font);
		}

		private void processCPGPSResponse(CPGPSResponse resp) {
			log("Loading " + resp.CPs.Count + " GPS coordinates from server", "processCPGPSResponse");

			foreach (CPGPSResponse.CPGPS cp in resp.CPs) {
				IMyGps gps = MyAPIGateway.Session.GPS.Create(cp.name, "Conquest Capture Point",
					new VRageMath.Vector3D(cp.x, cp.y, cp.z), true, true);
				MyAPIGateway.Session.GPS.AddLocalGps(gps);
			}
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}

	}
}

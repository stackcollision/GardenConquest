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

using GardenConquest.Core;
using GardenConquest.Records;

namespace GardenConquest.Messaging {

	/// <summary>
	/// Server side messaging hooks.  Recieves requests from the clients and sends
	/// responses.
	/// </summary>
	public class RequestProcessor {

		static Logger s_Logger = null;

		private Action<byte[]> onSend;
		public event Action<byte[]> localReceiver {
			add { onSend += value; }
			remove { onSend -= value; }
		}

		public RequestProcessor() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "RequestProcessor");

			if (MyAPIGateway.Multiplayer != null) {
				MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.GCMessageId, incomming);
				log("Message handler registered", "ctor");
			} else {
				log("Multiplayer null.  No message handler registered", "ctor");
			}
		}

		public void unload() {
			if (MyAPIGateway.Multiplayer != null)
				MyAPIGateway.Multiplayer.UnregisterMessageHandler(Constants.GCMessageId, incomming);
		}

		public void incomming(byte[] buffer) {
			log("Got message of size " + buffer.Length, "incomming");

			try {
				//Deserialize message
				BaseRequest msg = BaseRequest.messageFromBytes(buffer);

				// Process type
				switch (msg.MsgType) {
					case BaseRequest.TYPE.CPGPS:
						processCPGPSRequest(msg as CPGPSRequest);
						break;
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "incomming");
			}
		}

		public void incommingResponse(byte[] buffer) {
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
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "incomming");
			}
		}

		private void processNotificationResponse(NotificationResponse noti)
		{
			log("Hit", "processNotificationResponse");
			MyAPIGateway.Utilities.ShowNotification(noti.NotificationText, noti.Time, noti.Font);
		}

		public void send(BaseResponse msg) {
			if (msg == null)
				return;

			byte[] buffer = msg.serialize();

			if (MyAPIGateway.Multiplayer.MultiplayerActive) {
				MyAPIGateway.Multiplayer.SendMessageToOthers(Constants.GCMessageId, buffer);
			} else {
				incommingResponse(buffer);
			}

			log("Sent packet of " + buffer.Length + " bytes", "send");

			onSend(buffer);
		}

		private void processCPGPSRequest(CPGPSRequest req) {
			CPGPSResponse resp = new CPGPSResponse();

			resp.DestType = BaseResponse.DEST_TYPE.PLAYER;
			resp.Destination = new List<long>() { req.ReturnAddress };

			foreach(ControlPoint cp in ConquestSettings.getInstance().ControlPoints) {
				resp.CPs.Add(new CPGPSResponse.CPGPS() {
					x = (long)cp.Position.X,
					y = (long)cp.Position.Y,
					z = (long)cp.Position.Z,
					name = cp.Name
				});
			}

			send(resp);
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}

	}
}

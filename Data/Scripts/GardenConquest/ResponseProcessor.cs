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

namespace GardenConquest {
	/// <summary>
	/// Client side message hooks.  Processed messages coming from the server.
	/// </summary>
	public class ResponseProcessor {
		//private static VRage.Serialization.ProtoSerializer<BaseMessage> m_Serializer = null;

		public ResponseProcessor() {
			//m_Serializer = new VRage.Serialization.ProtoSerializer<BaseMessage>();

			if (MyAPIGateway.Multiplayer != null) {
				MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.GCMessageId, incomming);
			}
		}

		public void incomming(byte[] buffer) {
			try {
				// Deserialize the message
				BaseMessage msg = BaseMessage.messageFromBytes(buffer);

				// Is this message even intended for us?
				if (msg.DestType == BaseMessage.DEST_TYPE.FACTION) {
					IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(
						MyAPIGateway.Session.Player.PlayerID);
					if (fac == null || fac.FactionId != msg.Destination)
						return; // Message not meant for us
				} else if (msg.DestType == BaseMessage.DEST_TYPE.PLAYER) {
					if (msg.Destination != MyAPIGateway.Session.Player.PlayerID)
						return; // Message not meant for us
				}

				switch (msg.MsgType) {
					case BaseMessage.TYPE.NOTIFICATION:
						processNotificationResponse(msg as NotificationResponse);
						break;
				}
			} catch (Exception e) {
			}
		}

		private void processNotificationResponse(NotificationResponse noti) {
			MyAPIGateway.Utilities.ShowNotification(noti.NotificationText, noti.Time, noti.Font);
		}

	}
}

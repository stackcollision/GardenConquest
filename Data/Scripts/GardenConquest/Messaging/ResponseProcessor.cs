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
using GardenConquest.Messaging;

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

		public void incomming(byte[] stream) {
			// Deserialize the message
			//BaseMessage msg = null;
			//VRage.ByteStream bstream = new VRage.ByteStream(stream, stream.Length);
			//m_Serializer.Deserialize(bstream, out msg);

			// Process based on type
			//switch (msg.MsgType) {
			//	case BaseMessage.TYPE.NOTIFICATION:
			//		processNotificationResponse(msg as NotificationResponse);
			//		break;
			//}
		}

		private void processNotificationResponse(NotificationResponse noti) {
			MyAPIGateway.Utilities.ShowNotification(noti.NotificationText, noti.Time, noti.Font);
		}

	}
}

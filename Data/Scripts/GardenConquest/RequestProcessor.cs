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
	/// Server side messaging hooks.  Recieves requests from the clients and sends
	/// responses.
	/// </summary>
	public class RequestProcessor {

		static Logger s_Logger = null;

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

		public void incomming(byte[] stream) {

		}

		public void send(BaseMessage msg) {
			if (msg == null)
				return;

			byte[] buffer = msg.serialize();
			MyAPIGateway.Multiplayer.SendMessageToOthers(Constants.GCMessageId, buffer);
			log("Sent packet of " + buffer.Length + " bytes", "send");
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}

	}
}

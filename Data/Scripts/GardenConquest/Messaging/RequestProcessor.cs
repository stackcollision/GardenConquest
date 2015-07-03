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

		private Action<byte[]> localMsgSend;
		public event Action<byte[]> localMsgSent {
			add { localMsgSend += value; }
			remove { localMsgSend -= value; }
		}
		private void sendLocalMsg(byte[] buffer) {
			if (localMsgSend != null)
				localMsgSend(buffer);
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
					case BaseRequest.TYPE.FLEET:
						processFleetRequest(msg as FleetRequest);
						break;
					case BaseRequest.TYPE.SETTINGS:
						processSettingsRequest(msg as SettingsRequest);
						break;
					case BaseRequest.TYPE.VIOLATIONS:
						processViolationsRequest(msg as ViolationsRequest);
						break;
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "incomming");
			}
		}

		public void send(BaseResponse msg) {
			log("Sending " + msg.MsgType + " response", "send");

			if (msg == null)
				return;

			try {
				byte[] buffer = msg.serialize();
				MyAPIGateway.Multiplayer.SendMessageToOthers(Constants.GCMessageId, buffer);
				sendLocalMsg(buffer);
				log("Sent packet of " + buffer.Length + " bytes", "send");
			}
			catch (Exception e) {
				log("Error: " + e, "send", Logger.severity.ERROR);
			}
		}

		private void processFleetRequest(FleetRequest req) {
			// Get an Owner object from the player ID of the request
			GridOwner.OWNER owner = GridOwner.ownerFromPlayerID(req.ReturnAddress);

			// Retrieve that owner's fleet
			FactionFleet fleet = GardenConquest.Core.StateTracker.
				getInstance().getFleet(owner.FleetID, owner.OwnerType);

			// Get the fleet's juicy description
			String body = fleet.classesToString();

			// build the title
			String title = "";
			switch (owner.OwnerType) {
				case GridOwner.OWNER_TYPE.FACTION:
					title = "Your Faction's Fleet:";
					break;
				case GridOwner.OWNER_TYPE.PLAYER:
					title = "Your Fleet";
					break;
			}

			// send the response
			DialogResponse resp = new DialogResponse() {
				Body = body,
				Title = title,
				Destination = new List<long>() { req.ReturnAddress },
				DestType = BaseResponse.DEST_TYPE.PLAYER
			};

			send(resp);
		}

		private void processSettingsRequest(SettingsRequest req) {
			log("", "processSettingsRequest");
			SettingsResponse resp = new SettingsResponse() {
				Settings = ConquestSettings.getInstance().Settings,
				Destination = new List<long>() { req.ReturnAddress },
				DestType = BaseResponse.DEST_TYPE.PLAYER
			};

			send(resp);
		}

		private void processViolationsRequest(ViolationsRequest req) {
			// Get an Owner object from the player ID of the request
			GridOwner.OWNER owner = GridOwner.ownerFromPlayerID(req.ReturnAddress);

			// Retrieve that owner's fleet
			FactionFleet fleet = GardenConquest.Core.StateTracker.
				getInstance().getFleet(owner.FleetID, owner.OwnerType);

			// Get the fleet's juicy description
			String body = fleet.violationsToString();

			// build the title
			String title = "";
			switch (owner.OwnerType) {
				case GridOwner.OWNER_TYPE.FACTION:
					title = "Your Faction's Fleet's Violations";
					break;
				case GridOwner.OWNER_TYPE.PLAYER:
					title = "Your Fleet Violations";
					break;
			}

			// send the response
			DialogResponse resp = new DialogResponse() {
				Body = body,
				Title = title,
				Destination = new List<long>() { req.ReturnAddress },
				DestType = BaseResponse.DEST_TYPE.PLAYER
			};

			send(resp);
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}

	}
}

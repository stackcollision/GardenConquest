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

using GardenConquest.Blocks;
using GardenConquest.Core;
using GardenConquest.Extensions;
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
					case BaseRequest.TYPE.DISOWN:
						processDisownRequest(msg as DisownRequest);
						break;
					case BaseRequest.TYPE.STOPGRID:
						processStopGridRequest(msg as StopGridRequest);
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
			// Get an Owner object from the palyer ID of the request
			GridOwner.OWNER owner = GridOwner.ownerFromPlayerID(req.ReturnAddress);

			// Retrieve that owner's fleet
			FactionFleet fleet = GardenConquest.Core.StateTracker
				.getInstance().getFleet(owner.FleetID, owner.OwnerType);

			FleetResponse resp = new FleetResponse() {
				Fleet = fleet,
				Owner = owner,
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

		private void processStopGridRequest(StopGridRequest req) {
			log("", "processStopGridRequest");
			IMyCubeGrid gridToStop = MyAPIGateway.Entities.GetEntityById(req.EntityID) as IMyCubeGrid;

			// @TODO: make it easy to find enforcers by entityId so we can provide
			// greater accuracy to our canInteractWith check
			//GridEnforcer enforcer = StateTracker.getInstance().

			// Can the player interact with this grid? If they can, stop the ship by enabling dampeners, turning off
			// space balls and artificial masses, and disable thruster override
			if (gridToStop.canInteractWith(req.ReturnAddress)) {

				// Get all thrusters, spaceballs, artificial masses, and cockpits
				List<IMySlimBlock> fatBlocks = new List<IMySlimBlock>();
				Func<IMySlimBlock, bool> selectBlocks = b =>
					b.FatBlock != null && (b.FatBlock is IMyThrust || b.FatBlock is IMySpaceBall || b.FatBlock is InGame.IMyVirtualMass || b.FatBlock is InGame.IMyShipController);
				gridToStop.GetBlocks(fatBlocks, selectBlocks);

				foreach (IMySlimBlock block in fatBlocks) {
					// Thruster
					if (block.FatBlock is IMyThrust) {
						Interfaces.TerminalPropertyExtensions.SetValueFloat(block.FatBlock as IMyTerminalBlock, "Override", 0);
					}
					// Spaceball
					else if (block.FatBlock is IMySpaceBall) {
						(block.FatBlock as InGame.IMyFunctionalBlock).RequestEnable(false);
					}
					// Artificial Mass
					else if (block.FatBlock is InGame.IMyVirtualMass) {
						(block.FatBlock as InGame.IMyFunctionalBlock).RequestEnable(false);
					}
					// Cockpit
					else if (block.FatBlock is InGame.IMyShipController) {
						Interfaces.TerminalPropertyExtensions.SetValueBool(block.FatBlock as InGame.IMyShipController, "DampenersOverride", true);
					}
				}
				gridToStop.Physics.ClearSpeed();
			}
			// Player can't interact with grid, send error message
			else {
				GridOwner.OWNER owner = GridOwner.ownerFromPlayerID(req.ReturnAddress);
				string errorMessage = "";

				// Build text based on whether or not player is in faction
				switch (owner.OwnerType) {
					case GridOwner.OWNER_TYPE.FACTION:
						errorMessage = "Your faction does not have control of that ship's Main Cockpit!";
						break;
					case GridOwner.OWNER_TYPE.PLAYER:
						errorMessage = "You do not have control of that ship's Main Cockpit!";
						break;
				}

				NotificationResponse noti = new NotificationResponse() {
					NotificationText = errorMessage,
					Time = Constants.NotificationMillis,
					Font = MyFontEnum.Red,
					Destination = new List<long>() { req.ReturnAddress },
					DestType = BaseResponse.DEST_TYPE.PLAYER
				};

				send(noti);
			}
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

		private void processDisownRequest(DisownRequest req) {
			IMyCubeGrid gridToDisown = MyAPIGateway.Entities.GetEntityById(req.EntityID) as IMyCubeGrid;

			List<IMySlimBlock> allBlocks = new List<IMySlimBlock>();

			// Get only FatBlocks from the blocks list from the grid
			Func<IMySlimBlock, bool> isFatBlock = b => b.FatBlock != null;
			gridToDisown.GetBlocks(allBlocks, isFatBlock);

			foreach (IMySlimBlock block in allBlocks) {
				if (block.FatBlock.OwnerId == req.ReturnAddress)  {
						// Code to disown blocks goes here
					// Disabled because current Space Engineer's Mod API does not have the capability to disown individual blocks
					//fatBlock.ChangeOwner(0, 0);
				}
			}
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}

	}
}

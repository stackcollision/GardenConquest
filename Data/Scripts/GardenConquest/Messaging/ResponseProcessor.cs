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
using GardenConquest.Records;
using GardenConquest.Core;

namespace GardenConquest.Messaging {
	/// <summary>
	/// Client side message hooks.  Processed messages coming from the server.
	/// </summary>
	public class ResponseProcessor {
		private static Logger s_Logger = null;

		private uint[] m_Counts = null;
		private List<GridEnforcer.GridData>[] m_SupportedGrids;
		private List<GridEnforcer.GridData>[] m_UnsupportedGrids;

		private ConquestSettings.SETTINGS m_ServerSettings;
		private List<IMyGps> m_ServerCPGPS = new List<IMyGps>();
		private bool m_ServerCPGPSAdded = false;
		private bool m_Registered = false;

		public ConquestSettings.SETTINGS ServerSettings {
			get {return m_ServerSettings; }
		}

		public ResponseProcessor(bool register = true) {
			log("Started", "ctor");
			if (register && MyAPIGateway.Multiplayer != null) {
				log("Registering for messages", "ctor");
				m_Registered = true;
				MyAPIGateway.Multiplayer.RegisterMessageHandler(Constants.GCMessageId, incomming);
			}
			int classCount = Enum.GetValues(typeof(HullClass.CLASS)).Length;
			m_Counts = new uint[classCount];

			m_SupportedGrids = new List<GridEnforcer.GridData>[classCount];
			for (int i = 0; i < classCount; ++i) {
				m_SupportedGrids[i] = new List<GridEnforcer.GridData>();
			}

			m_UnsupportedGrids = new List<GridEnforcer.GridData>[classCount];
			for (int i = 0; i < classCount; ++i) {
				m_UnsupportedGrids[i] = new List<GridEnforcer.GridData>();
			}
		}

		public void unload() {
			if (m_Registered && MyAPIGateway.Multiplayer != null)
				MyAPIGateway.Multiplayer.UnregisterMessageHandler(Constants.GCMessageId, incomming);
		}

		#region Make Requests

		public void send(BaseRequest msg) {
			if (msg == null)
				return;

			try {
				byte[] buffer = msg.serialize();
				MyAPIGateway.Multiplayer.SendMessageToServer(Constants.GCMessageId, buffer);
				log("Sent packet of " + buffer.Length + " bytes", "send");
			}
			catch (Exception e) {
				log("Error: " + e, "send", Logger.severity.ERROR);
			}
		}

		public bool requestSettings() {
			log("Sending Settings request", "requestSettings");
			try {
				SettingsRequest req = new SettingsRequest();
				req.ReturnAddress = MyAPIGateway.Session.Player.PlayerID;
				send(req);
				return true;
			}
			catch (Exception e) {
				log("Exception occured: " + e, "requestSettings", Logger.severity.ERROR);
				return false;
			}
		}

		public bool requestFleet(String hullClassString = "") {
			log("Sending Fleet request", "requestFleet");
			try {
				FleetRequest req = new FleetRequest();
				req.ReturnAddress = MyAPIGateway.Session.Player.PlayerID;
				send(req);
				return true;
			}
			catch (Exception e) {
				log("Exception occured: " + e, "requestFleet");
				return false;
			}
		}

		public bool requestViolations(String hullClassString = "") {
			log("Sending Violations request", "requestViolations");
			try {
				ViolationsRequest req = new ViolationsRequest();
				req.ReturnAddress = MyAPIGateway.Session.Player.PlayerID;
				send(req);
				return true;
			}
			catch (Exception e) {
				log("Exception occured: " + e, "requestViolations");
				return false;
			}
		}

		public bool requestStopGrid(string shipClass, string ID) {
			log("Sending Stop Grid request", "requestStopGrid");
			try {
				HullClass.CLASS classID;
				int localID = -1;

				// Check user's input for shipClass is valid
				if (!Enum.TryParse<HullClass.CLASS>(shipClass.ToUpper(), out classID)) {
					MyAPIGateway.Utilities.ShowNotification("Invalid Ship Class!", Constants.NotificationMillis, MyFontEnum.Red);
					return false;
				}
				// Check if user's input for index is valid
				if (!int.TryParse(ID, out localID) || localID < 1 || localID > m_SupportedGrids[(int)classID].Count + m_UnsupportedGrids[(int)classID].Count) {
					MyAPIGateway.Utilities.ShowNotification("Invalid Index!", Constants.NotificationMillis, MyFontEnum.Red);
					return false;
				}

				long entityID;

				//Some logic to decide whether or not the choice is a supported or unsupported grid, and its entityID
				if (localID - 1 >= m_SupportedGrids[(int)classID].Count ) {
					entityID = m_UnsupportedGrids[(int)classID][localID - 1 - m_SupportedGrids[(int)classID].Count].shipID;
				} else {
					entityID = m_SupportedGrids[(int)classID][localID - 1].shipID;
				}

				StopGridRequest req = new StopGridRequest();
				req.ReturnAddress = MyAPIGateway.Session.Player.PlayerID;
				req.EntityID = entityID;
				send(req);
				return true;
			}
			catch (Exception e) {
				log("Exception occured: " + e, "requestStopGrid");
				return false;
			}
		}

		public bool requestDisown(string shipClass, string ID) {
			log("Sending Disown request", "requestDisown");
			try {
				int classID = (int)Enum.Parse(typeof(HullClass.CLASS), shipClass.ToUpper());
				int localID = Convert.ToInt32(ID);
				long entityID;

				//Some logic to decide whether or not the choice is a supported or unsupported grid, and its entityID
				if (localID >= m_SupportedGrids[classID].Count) {
					entityID = m_UnsupportedGrids[classID][localID - m_SupportedGrids[classID].Count].shipID;
				} else {
					entityID = m_SupportedGrids[classID][localID].shipID;
				}

				DisownRequest req = new DisownRequest();
				req.ReturnAddress = MyAPIGateway.Session.Player.PlayerID;
				req.EntityID = entityID;
				send(req);
				return true;
			}
			catch (Exception e) {
				log("Exception occured: " + e, "requestDisown");
				return false;
			}
		}

		#endregion
		#region Process Responses

		public void incomming(byte[] buffer) {
			log("Got message of size " + buffer.Length, "incomming");

			try {
				// Deserialize the message
				BaseResponse msg = BaseResponse.messageFromBytes(buffer);

				// Is this message even intended for us?
				if (msg.DestType == BaseResponse.DEST_TYPE.FACTION) {
					IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(
						MyAPIGateway.Session.Player.PlayerID);
					if (fac == null || !msg.Destination.Contains(fac.FactionId)) {
						return; // Message not meant for us
					}
				} else if (msg.DestType == BaseResponse.DEST_TYPE.PLAYER) {
					long localUserId = (long)MyAPIGateway.Session.Player.PlayerID;
					if (!msg.Destination.Contains(localUserId)) {
						return; // Message not meant for us
					}
				}

				switch (msg.MsgType) {
					case BaseResponse.TYPE.NOTIFICATION:
						processNotificationResponse(msg as NotificationResponse);
						break;
					case BaseResponse.TYPE.DIALOG:
						processDialogResponse(msg as DialogResponse);
						break;
					case BaseResponse.TYPE.SETTINGS:
						processSettingsResponse(msg as SettingsResponse);
						break;
					case BaseResponse.TYPE.FLEET:
						processFleetResponse(msg as FleetResponse);
						break;
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "incomming", Logger.severity.ERROR);
			}
		}

		private void processNotificationResponse(NotificationResponse noti) {
			log("Hit", "processNotificationResponse");
			MyAPIGateway.Utilities.ShowNotification(noti.NotificationText, noti.Time, noti.Font);
		}

		private void processDialogResponse(DialogResponse resp) {
			log("Hit", "processDialogResponse");
			Utility.showDialog(resp.Title, resp.Body, "Close");
		}

		private void processSettingsResponse(SettingsResponse resp) {
			log("Loading settings from server", "processSettingsResponse");
			m_ServerSettings = resp.Settings;

			log("Adding CP GPS", "processSettingsResponse");
			foreach (Records.ControlPoint cp in ServerSettings.ControlPoints) {
				m_ServerCPGPS.Add(MyAPIGateway.Session.GPS.Create(
					cp.Name,
					"GardenConquest Control Point",
					new VRageMath.Vector3D(cp.Position.X, cp.Position.Y, cp.Position.Z),
					true, true
				));
			}

			addCPGPS();
		}

		private void processFleetResponse(FleetResponse resp) {
			log("Loading fleet data from server", "processFleetResponse");
			List<GridEnforcer.GridData> gridData = resp.FleetData;

			// Clear our current data to get fresh data from server
			Array.Clear(m_Counts, 0, m_Counts.Length);
			for (int i = 0; i < m_Counts.Length; ++i) {
				m_SupportedGrids[i].Clear();
				m_UnsupportedGrids[i].Clear();
			}

			// Saving data from server to client
			for (int i = 0; i < gridData.Count; ++i) {
				int classID = (int)gridData[i].shipClass;
				m_Counts[classID] += 1;
				if (gridData[i].supported) {
					m_SupportedGrids[classID].Add(gridData[i]);
				}
				else {
					m_UnsupportedGrids[classID].Add(gridData[i]);
				}
			}

			// Building fleet info to display in a dialog
			string fleetInfoBody = buildFleetInfoBody(resp.OwnerType);
			string fleetInfoTitle = buildFleetInfoTitle(resp.OwnerType);

			// Displaying the fleet information
			Utility.showDialog(fleetInfoTitle, fleetInfoBody, "Close");
		}

		#endregion
		#region Process Response Utilities

		private string buildFleetInfoBody(GridOwner.OWNER_TYPE ownerType) {
			log("Building Fleet Info Body", "buildFleetInfoBody");
			string fleetInfoBody = "";
			List<GridEnforcer.GridData> gdList;
			for (int i = 0; i < m_Counts.Length; ++i) {
				if (m_Counts[i] > 0) {
					fleetInfoBody += (HullClass.CLASS)i + ": " + m_Counts[i] + " / ";
					if (ownerType == GridOwner.OWNER_TYPE.FACTION) {
						fleetInfoBody += ServerSettings.HullRules[i].MaxPerFaction + "\n";
					}
					else if (ownerType == GridOwner.OWNER_TYPE.PLAYER) {
						fleetInfoBody += ServerSettings.HullRules[i].MaxPerSoloPlayer + "\n";
					}
					else {
						fleetInfoBody += "0\n";
					}

					if (m_SupportedGrids[i].Count > 0) {
						gdList = m_SupportedGrids[i];
						for (int j = 0; j < gdList.Count; ++j) {
							fleetInfoBody += "  " + (j + 1) + ". " + gdList[j].shipName + " - " + gdList[j].blockCount + " blocks\n";
							if (gdList[j].displayPos) {
								fleetInfoBody += "      GPS: " + gdList[j].shipPosition.X + ", " + gdList[j].shipPosition.Y + ", " + gdList[j].shipPosition.Z + "\n";
							}
							else {
								fleetInfoBody += "      GPS: Unavailable - Must own the Main Cockpit\n";
							}
						}
					}
					if (m_UnsupportedGrids[i].Count > 0) {
						gdList = m_UnsupportedGrids[i];
						int offset = m_SupportedGrids[i].Count;
						fleetInfoBody += "\n  Unsupported:\n";
						for (int j = 0; j < gdList.Count; ++j) {
							//Some code logic to continue the numbering of entries where m_SupportedGrid leaves off
							fleetInfoBody += "     " + (j + offset + 1) + ". " + gdList[j].shipName + " - " + gdList[j].blockCount + " blocks\n";
							if (gdList[j].displayPos) {
								fleetInfoBody += "         GPS: " + gdList[j].shipPosition.X + ", " + gdList[j].shipPosition.Y + ", " + gdList[j].shipPosition.Z + "\n";
							}
							else {
								fleetInfoBody += "         GPS: Unavailable - Must own the Main Cockpit\n";
							}
						}
					}

					fleetInfoBody += "\n";
				}
			}
			return fleetInfoBody;
		}
		private string buildFleetInfoTitle(GridOwner.OWNER_TYPE ownerType) {
			string fleetInfoTitle = "";
			switch (ownerType) {
				case GridOwner.OWNER_TYPE.FACTION:
					fleetInfoTitle = "Your Faction's Fleet:";
					break;
				case GridOwner.OWNER_TYPE.PLAYER:
					fleetInfoTitle = "Your Fleet:";
					break;
			}
			return fleetInfoTitle;
		}

		public void addCPGPS() {
			foreach (IMyGps gps in m_ServerCPGPS) {
				MyAPIGateway.Session.GPS.AddLocalGps(gps);
			}

			m_ServerCPGPSAdded = true;
		}

		public void removeCPGPS() {
			if (!m_ServerCPGPSAdded)
				return;

			foreach (IMyGps gps in m_ServerCPGPS) {
				MyAPIGateway.Session.GPS.RemoveLocalGps(gps.Hash);
			}

			m_ServerCPGPSAdded = false;
		}

		#endregion

		private static void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "ResponseProcessor");

			s_Logger.log(level, method, message);
		}

	}
}

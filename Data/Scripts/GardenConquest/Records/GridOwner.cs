using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.ModAPI;

using GardenConquest.Core;
using GardenConquest.Blocks;

namespace GardenConquest.Records {

	/// <summary>
	/// Provides a connection between a GridEnforcer and a Fleet
	/// One of these exists for every Enforcer
	/// Stores the details used to lookup a fleet
	/// Provides helpers to set and retrieve the fleet and ownership
	/// </summary>
	public class GridOwner {

		public enum OWNER_TYPE {
			UNOWNED,
			PLAYER,
			FACTION
		}

		public struct OWNER {
			public OWNER_TYPE OwnerType { get; set; }
			public long PlayerID { get; set; }
			public long FactionID { get; set; }
			public long FleetID { get {
				switch (OwnerType) {
					case OWNER_TYPE.FACTION:
						return FactionID;
					case OWNER_TYPE.PLAYER:
						return PlayerID;
					case OWNER_TYPE.UNOWNED:
					default:
						return StateTracker.UNOWNED_FLEET_ID;
				}
			}}
			public FactionFleet Fleet {
				get {
					if (GridEnforcer.StateTracker != null) {
						return GridEnforcer.StateTracker.getFleet(FleetID, OwnerType);
					}
					else {
						return null;
					}
				}
			}
		}

		public static OWNER ownerFromPlayerID(long playerID) {
			OWNER_TYPE type = OWNER_TYPE.UNOWNED;
			IMyFaction faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerID);
			long factionID = 0;

			// Is the player solo?
			if (faction == null) {
				type = OWNER_TYPE.PLAYER;
			}
			else {
				type = OWNER_TYPE.FACTION;
				factionID = faction.FactionId;
			}

			return new OWNER {
				OwnerType = type,
				PlayerID = playerID,
				FactionID = factionID,
			};
		}

		#region Fields

		private GridEnforcer m_Enforcer;
		//private IMyCubeGrid m_Grid = null;

		private OWNER_TYPE m_OwnerType;
		private long m_PlayerID;
		//private IMyPlayer m_Player;
		private long m_FactionID;
		//private IMyFaction m_OwningFaction = null;
		private long m_FleetID;
		private FactionFleet m_Fleet;
		private HullClass.CLASS m_Class;

		private bool m_StateLoaded;

		private Logger m_Logger = null;

		#endregion
		#region Properties

		public OWNER_TYPE OwnerType { get { return m_OwnerType; } }
		public long PlayerID { get { return m_PlayerID; } }
		public long FactionID { get { return m_FactionID; } }
		public long FleetID { get { return m_FleetID; } }
		public FactionFleet Fleet { get { return m_Fleet; } }

		#endregion
		#region Lifecycle

		public GridOwner(GridEnforcer ge) {
			m_Enforcer = ge;
			m_Logger = new Logger(m_Enforcer.Grid.EntityId.ToString(), "GridOwner");
			log("Loaded into new grid", "ctr");

			// the grid will update ownership later b/c this is initialized with the grid,
			// and the grid doesn't have any blocks yet
			m_OwnerType = OWNER_TYPE.UNOWNED;
			m_FleetID = getFleetID();

			m_Class = ge.Class;
			m_Fleet = getFleet();
			m_Fleet.add(m_Class, ge);
		}

		public void Close() {
			log("", "Close");
			try {
				m_Fleet.remove(m_Class, m_Enforcer);
				StateTracker.getInstance().removeFleetIfEmpty(m_FleetID, m_OwnerType);
			} catch (NullReferenceException e) {
				log("Error: " + e, "Close", Logger.severity.ERROR);
			}
			m_Fleet = null;
			m_Enforcer = null;
			m_Logger = null;
		}

		#endregion

		/// <summary>
		/// If the class has changed, update class the fleet stored it as
		/// </summary>
		/// <param name="c">New Hull Class</param>
		public void setClassification(HullClass.CLASS c) {
			if (m_Class == c)
				return;

			log("changing owner class to " + c, "setClassification");
			m_Fleet.remove(m_Class, m_Enforcer);
			m_Class = c;
			m_Fleet.add(m_Class, m_Enforcer);
			log("Owner class changed to " + m_Class, "setClassification");
		}

		/// <summary>
		/// Figures out who owns the grid now
		/// </summary>
		/// <returns>Returns true if the owner has changed</returns>
		public bool reevaluateOwnership(List<long> bigOwners) {
			log("Reevaluating owner", "reevaluateOwnership");
			bool changed = false;

			// = Get new ownership details
			OWNER_TYPE newType = OWNER_TYPE.UNOWNED;
			long newPlayerID = 0;
			long newFactionID = 0;

			if (bigOwners.Count == 0) {
				newType = OWNER_TYPE.UNOWNED;

			} else {
				if (bigOwners.Count > 1) {
					log("bigOwner tie! Using first owner.",
						"reevaluateOwnership", Logger.severity.WARNING);
				}

				newPlayerID = bigOwners[0];
				IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(newPlayerID);

				// Is the player solo?
				if (fac == null) {
					newType = OWNER_TYPE.PLAYER;
				} else {
					newType = OWNER_TYPE.FACTION;
					newFactionID = fac.FactionId;
				}
			}

			long newFleetID = calcFleetID(newType, newPlayerID, newFactionID);

			// = Effect changes
			if (m_OwnerType != newType) {
				log(String.Format("type changed from {0} => {1}",
					m_OwnerType, newType), "reevaluateOwnership");
				m_OwnerType = newType;
				changed = true;
			}

			if (m_PlayerID != newPlayerID) {
				log(String.Format("player changed from {0} => {1}",
					m_PlayerID, newPlayerID), "reevaluateOwnership");
				m_PlayerID = newPlayerID;
				changed = true;
			}

			if (m_FactionID != newFactionID) {
				log(String.Format("faction changed from {0} => {1}",
					m_FactionID, newFactionID), "reevaluateOwnership");
				m_FactionID = newFactionID;
				changed = true;
			}

			// If the fleet tracking ID has changed, or if we've got a temp fleet, update the fleet
			if ((m_FleetID != newFleetID) || (!m_StateLoaded && GridEnforcer.StateTracker != null)) {
				log(String.Format("Doing fleet change {0} => {1}, was temp? {2}",
					m_FleetID, newFleetID, !m_StateLoaded), "reevaluateOwnership");

				// Remove the grid from the previous fleet
				if (m_Fleet == null) {
					log("Fleet is null", "reevaluateOwnership", Logger.severity.ERROR);
				} else {
					m_Fleet.remove(m_Class, m_Enforcer);
				}

				m_FleetID = newFleetID;
				m_Fleet = getFleet();

				// Add the grid to the new fleet
				if (m_Fleet == null) {
					log("Failed to get new fleet for " + m_FleetID,
						"reevaluateOwnership", Logger.severity.ERROR);
				} else {
					m_Fleet.add(m_Class, m_Enforcer);
				}

				changed = true;
			}

			if (changed) {
				return true;
			} else {
				log(String.Format("no change, owner: {0} {1}", m_OwnerType, m_FleetID),
					"reevaluateOwnership");
				return false;
			}
		}

		/// <summary>
		/// Gets the fleet this grid belongs to
		/// </summary>
		/// <returns></returns>
		public FactionFleet getFleet() {
			if (GridEnforcer.StateTracker != null) {
				log("retreiving fleet from state tracker", "getFleet");
				FactionFleet loadedFleet = GridEnforcer.StateTracker.getFleet(m_FleetID, OwnerType);

				if (loadedFleet != null) {
					m_StateLoaded = true;
					return loadedFleet;
				} else {
					log("state tracker returned null fleet", "getFleet",
						Logger.severity.ERROR);
				}
			} else {
				log("null state, probably initing", "getFleet");
			}

			log("failed to load fleet from state tracker", "getFleet");
			m_StateLoaded = false;

			if (m_Fleet == null) {
				log("creating temporary untracked fleet", "getFleet");
				return new FactionFleet(m_FleetID, OwnerType);
			} else {
				log("returning existing stored fleet", "getFleet");
				return m_Fleet;
			}
		}

		private long getFleetID() {
			return calcFleetID(m_OwnerType, m_PlayerID, m_FactionID);
		}

		private long calcFleetID(OWNER_TYPE ownerType, long playerID, long factionID) {
			switch (ownerType) {
				case OWNER_TYPE.FACTION:
					return factionID;
				case OWNER_TYPE.PLAYER:
					return playerID;
				case OWNER_TYPE.UNOWNED:
				default:
					return StateTracker.UNOWNED_FLEET_ID;
			}
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}
	}
}

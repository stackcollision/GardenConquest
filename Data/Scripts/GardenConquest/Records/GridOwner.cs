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
            m_Fleet.remove(m_Class, m_Enforcer);
            StateTracker.getInstance().removeFleetIfEmpty(m_FleetID, m_OwnerType);
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

			log("changing classification to " + c, "setClassification");
			m_Fleet.remove(m_Class, m_Enforcer);
			m_Class = c;
			m_Fleet.add(m_Class, m_Enforcer);
			log("Classification changed to " + m_Class, "setClassification");
		}

		/// <summary>
		/// Figures out who owns the grid now
		/// </summary>
		/// <returns>Returns true if the owner has changed</returns>
		public bool reevaluateOwnership(List<long> bigOwners) {
			log("old type: " + m_OwnerType + ", player: " + m_PlayerID +
				", faction: " + m_FactionID + ", fleet: " + m_FleetID,
				"reevaluateOwnership");

			OWNER_TYPE newType = OWNER_TYPE.UNOWNED;
			long newPlayerID = 0;
			long newFactionID = 0;

			// = Get new ownership details
			// Is there an owner at all?
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

			// = Was there a change?
			if (m_OwnerType != newType || m_PlayerID != newPlayerID ||m_FactionID != newFactionID) {
				effectOwnershipChanged(newType, newFactionID, newPlayerID);
				return true;
			} else {
				log("no change", "reevaluateOwnership");
				return false;
			}
		}

		/// <summary>
		/// Gets the fleet this grid belongs to
		/// </summary>
		/// <returns></returns>
		public FactionFleet getFleet() {
			return StateTracker.getInstance().getFleet(m_FleetID, OwnerType);
		}

		/// <summary>
		/// Stores the new ownership data and changes the StateTracker fleets for the previous
		/// owner and the new owner
		/// </summary>
		/// <param name="newType"></param>
		/// <param name="newFac"></param>
		/// <param name="newPlayer"></param>
		private void effectOwnershipChanged(OWNER_TYPE newType, long newFactionID, long newPlayerID) {
			log("Changing ownership to: " + newType + ", player: " + newPlayerID +
				", faction: " + newFactionID, "effectOwnershipChanged");

			// Remove the grid from the previous fleet, if there was one
			if (m_Fleet == null) {
				log("Fleet is null", "effectOwnershipChanged", Logger.severity.ERROR);
			} else {
				m_Fleet.remove(m_Class, m_Enforcer);
			}

			// Update the stored details
			m_OwnerType = newType;
			m_FactionID = newFactionID;
			m_PlayerID = newPlayerID;
			long newFleetID = getFleetID();

			if (newFleetID == m_FleetID) {
				log("FleetID " + m_FleetID + "didn't change with new ownership",
					"effectOwnershipChanged", Logger.severity.ERROR);
				return;
			}

			m_FleetID = newFleetID;
			m_Fleet = getFleet();

			// Add the grid to the new fleet, if there is one
			FactionFleet newFleet = getFleet();
			if (m_Fleet == null) {
				log("Failed to get new fleet for " + m_FleetID,
					"effectOwnershipChanged", Logger.severity.ERROR);
			} else {
				newFleet.add(m_Class, m_Enforcer);
			}
		}

		private long getFleetID() {
			switch (m_OwnerType) {
				case OWNER_TYPE.FACTION:
					return m_FactionID;
				case OWNER_TYPE.PLAYER:
					return m_PlayerID;
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

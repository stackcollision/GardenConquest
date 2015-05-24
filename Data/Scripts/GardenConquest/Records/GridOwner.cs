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
	/// Records the ownership of a grid, whether it is a solo player or a faction
	/// </summary>
	public class GridOwner {

		public enum OWNER_TYPE {
			UNOWNED,
			PLAYER,
			FACTION
		}

		private GridEnforcer m_Enforcer;
		//private IMyCubeGrid m_Grid = null;

		private long m_OwningPlayer;
		private IMyFaction m_OwningFaction = null;
		private OWNER_TYPE m_OwnerType;

		private Logger m_Logger = null;

		public long PlayerID { get { return m_OwningPlayer; } }

		// Necessary for changing fleet counts
		private HullClass.CLASS m_Classification = HullClass.CLASS.UNCLASSIFIED;

		public GridOwner(GridEnforcer ge) {
			//m_Grid = grid;
			m_Enforcer = ge;
			m_Logger = new Logger(m_Enforcer.Grid.EntityId.ToString(), "GridOwner");
			log("Loaded into new grid", "ctr");

			/*
			if (m_Grid == null) {
				log("Grid is null!", "init");
			}
			else {
				log("Grid is " + grid.DisplayName, "init");
			}
			*/

			m_OwnerType = OWNER_TYPE.UNOWNED;

			// Add to fleet to start - they were set to only update
			FactionFleet fleet = getFleet();
			fleet.add(m_Classification, m_Enforcer);
		}

		public OWNER_TYPE getOwnerType() {
			return m_OwnerType;
		}

		public IMyFaction getFaction() {
			if (m_OwnerType == OWNER_TYPE.FACTION)
				return m_OwningFaction;
			else
				return null;
		}

		public void setClassification(HullClass.CLASS c) {
			if (m_Classification != c) {
				log("changing classification to " + c, "setClassification");
				// Modify the fleet records, if this grid belong to one
				FactionFleet fleet = getFleet();
				fleet.remove(m_Classification, m_Enforcer);
				fleet.add(c, m_Enforcer);

				m_Classification = c;
				log("Classification changed to " + m_Classification, "setClassification");
			}
		}

		/// <summary>
		/// Figures out who owns the grid now
		/// </summary>
		/// <returns>Returns true if the owner has changed</returns>
		public bool reevaluateOwnership(List<long> bigOwners) {
			//log("start", "reevaluateOwnership");
			bool changed = false;
			OWNER_TYPE newType = OWNER_TYPE.UNOWNED;
			IMyFaction newFac = null;
			long newPlayer = 0;
			//log("about to check owners, grid null?" + (m_Grid == null), "reevaluateOwnership");
			//List<long> bigOwners = m_Grid.BigOwners;
			//log("loaded data, owners: " + String.Join(",", bigOwners), "reevaluateOwnership");

			// Is there an owner at all?
			if (bigOwners.Count == 0) {
				// Was there an owner before?
				if (m_OwnerType != OWNER_TYPE.UNOWNED) {
					changed = true;
					newType = OWNER_TYPE.UNOWNED;
				}
			} else {
				if (bigOwners.Count > 1)
					log("bigOwner tie! Using first owner.",
						"reevaluateOwnership", Logger.severity.WARNING);

				long owner = bigOwners[0];
				IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner);
				
				// Is the player solo?
				if (fac == null) {
					// Was a solo player the owner before?
					if (m_OwnerType != OWNER_TYPE.PLAYER) {
						changed = true;
						newType = OWNER_TYPE.PLAYER;
						newPlayer = owner;
					} else {
						// If this grid was already owned by a solo player check that it
						// is the same player
						if (m_OwningPlayer != owner) {
							changed = true;
							newType = OWNER_TYPE.PLAYER;
							newPlayer = owner;
						}
					}
				} else {
					// Was a faction the owner before?
					if (m_OwnerType != OWNER_TYPE.FACTION) {
						changed = true;
						newType = OWNER_TYPE.FACTION;
						newFac = fac;
					} else {
						// If this grid was already owned by a faction check that it is the
						// same faction
						if (m_OwningFaction.FactionId != fac.FactionId) {
							changed = true;
							newType = OWNER_TYPE.FACTION;
							newFac = fac;
						}
					}
				}
			}

			if (changed)
				effectOwnershipChanged(newType, newFac, newPlayer);

			return changed;
		}

		/// <summary>
		/// Gets the fleet this grid belongs to
		/// </summary>
		/// <returns></returns>
		public FactionFleet getFleet() {
			return StateTracker.getInstance().getFleet(getFleetId(), m_OwnerType);
		}

		/// <summary>
		/// Gets the type-independent Fleet ID
		/// </summary>
		/// <returns></returns>
		public long getFleetId() {
			switch (m_OwnerType) {
				case OWNER_TYPE.FACTION:
					return m_OwningFaction.FactionId;
				case OWNER_TYPE.PLAYER:
					return m_OwningPlayer;
				case OWNER_TYPE.UNOWNED:
				default:
					return StateTracker.UNOWNED_FLEET_ID;
			}
		}

		/// <summary>
		/// Stores the new ownership data and changes the StateTracker fleets for the previous
		/// owner and the new owner
		/// </summary>
		/// <param name="newType"></param>
		/// <param name="newFac"></param>
		/// <param name="newPlayer"></param>
		private void effectOwnershipChanged(OWNER_TYPE newType, IMyFaction newFac, long newPlayer) {
			long newFacId = newFac == null ? 0 : newFac.FactionId;
			log("Ownership has changed: " + newType + " " + newFacId + " " + newPlayer, "effectOwnershipChanged");

			// Remove the grid from the previous fleet, if there was one
			FactionFleet oldFleet = getFleet();
			if (oldFleet != null) {
				oldFleet.remove(m_Classification, m_Enforcer);
			}

			// Change the state
			m_OwnerType = newType;
			m_OwningFaction = newFac;
			m_OwningPlayer = newPlayer;

			// Add the grid to the new fleet, if there is one
			FactionFleet newFleet = getFleet();
			if (newFleet != null) {
				newFleet.add(m_Classification, m_Enforcer);
			}
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.ModAPI;

using GardenConquest.Core;

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

		private IMyCubeGrid m_Grid = null;

		private long m_OwningPlayer;
		private IMyFaction m_OwningFaction = null;
		private OWNER_TYPE m_OwnerType;

		private Logger m_Logger = null;

		// Necessary for changing fleet counts
		private HullClass.CLASS m_Classification = HullClass.CLASS.UNCLASSIFIED;

		public GridOwner(IMyCubeGrid grid) {
			m_Grid = grid;
			m_OwnerType = OWNER_TYPE.UNOWNED;

			m_Logger = new Logger(m_Grid.EntityId.ToString(), "GridOwner");
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
				// Modify the fleet records, if this grid belong to one
				FactionFleet fleet = getFleet();
				if (fleet != null) {
					fleet.removeClass(m_Classification);
					fleet.addClass(c);
				}

				m_Classification = c;
				log("Classification changed to " + m_Classification, "setClassification");
			}
		}

		/// <summary>
		/// Figures out who owns the grid now
		/// </summary>
		/// <returns>Returns true if the owner has changed</returns>
		public bool reevaluateOwnership() {
			bool changed = false;
			OWNER_TYPE newType = OWNER_TYPE.UNOWNED;
			IMyFaction newFac = null;
			long newPlayer = 0;

			// Is there an owner at all?
			if (m_Grid.BigOwners.Count == 0) {
				// Was there an owner before?
				if (m_OwnerType != OWNER_TYPE.UNOWNED) {
					changed = true;
					newType = OWNER_TYPE.UNOWNED;
				}
			} else {
				long biggestOwner = m_Grid.BigOwners[0];
				IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(biggestOwner);
				
				// Is the player solo?
				if (fac == null) {
					// Was a solo player the owner before?
					if (m_OwnerType != OWNER_TYPE.PLAYER) {
						changed = true;
						newType = OWNER_TYPE.PLAYER;
						newPlayer = biggestOwner;
					} else {
						// If this grid was already owned by a solo player check that it
						// is the same player
						if (m_OwningPlayer != biggestOwner) {
							changed = true;
							newType = OWNER_TYPE.PLAYER;
							newPlayer = biggestOwner;
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
			switch (m_OwnerType) {
				case OWNER_TYPE.UNOWNED:
					return null;
				case OWNER_TYPE.PLAYER:
					return StateTracker.getInstance().getPlayerFleet(m_OwningPlayer);
				case OWNER_TYPE.FACTION:
					return StateTracker.getInstance().getFleet(m_OwningFaction.FactionId);
				default:
					return null;
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
				oldFleet.removeClass(m_Classification);
			}

			// Change the state
			m_OwnerType = newType;
			m_OwningFaction = newFac;
			m_OwningPlayer = newPlayer;

			// Add the grid to the new fleet, if there is one
			FactionFleet newFleet = getFleet();
			if (newFleet != null) {
				newFleet.addClass(m_Classification);
			}
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.ModAPI;

using GardenConquest.Records;

namespace GardenConquest.Core {

	/// <summary>
	/// Singleton class which tracks state, accessible to other classes.
	/// Contains information on last round results, current faction fleets, etc
	/// </summary>
	public class StateTracker {

		public static readonly int UNOWNED_FLEET_ID = 0;
		public Dictionary<long, long> TokensLastRound { get; private set; }
		private Dictionary<long, FactionFleet> m_Fleets = null;
		private Dictionary<long, FactionFleet> m_PlayerFleets = null;
		private SavedState m_SavedState = null;

		private static StateTracker s_Instance = null;

		private static Logger s_Logger = null;

		private StateTracker() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "StateTracker");

			TokensLastRound = new Dictionary<long, long>();
			m_Fleets = new Dictionary<long, FactionFleet>();
			m_PlayerFleets = new Dictionary<long, FactionFleet>();

			if (!loadState()) {
				// If the state is not loaded from the file we need to create an
				// empty state
				m_SavedState = new SavedState();
				log("State not loaded.  Creating blank state", "ctor");
			}
		}

		/// <summary>
		/// Get singleton instance
		/// </summary>
		/// <returns></returns>
		public static StateTracker getInstance() {
			if (s_Instance == null)
				s_Instance = new StateTracker();
			return s_Instance;
		}

		/// <summary>
		/// Returns the fleet for the fleet id.  If no fleet yet recorded creates a new one.
		/// </summary>
		/// <param name="factionId"></param>
		/// <returns></returns>
		public FactionFleet getFleet(long fleetId, GridOwner.OWNER_TYPE ownerType) {
			switch (ownerType) {
				case GridOwner.OWNER_TYPE.FACTION:
					if (!m_Fleets.ContainsKey(fleetId))
						m_Fleets.Add(fleetId, new FactionFleet(fleetId, ownerType));
					return m_Fleets[fleetId];
				case GridOwner.OWNER_TYPE.PLAYER:
					if (!m_PlayerFleets.ContainsKey(fleetId))
						m_PlayerFleets.Add(fleetId, new FactionFleet(fleetId, ownerType));
					return m_PlayerFleets[fleetId];
				case GridOwner.OWNER_TYPE.UNOWNED:
				default:
					if (!m_PlayerFleets.ContainsKey(UNOWNED_FLEET_ID))
						m_PlayerFleets.Add(UNOWNED_FLEET_ID, new FactionFleet(fleetId, ownerType));
					return m_PlayerFleets[fleetId];
			}
		}

		#region Timers

		/// <summary>
		/// Adds a new derelict timer to the queue.
		/// This will be used to alert the faction
		/// </summary>
		/// <param name="dt"></param>
		public void addNewDerelictTimer(DerelictTimer.DT_INFO dt) {
			m_SavedState.DerelictTimers.Add(dt);
		}

		/// <summary>
		/// Marks a derelict timer as finished.
		/// Used for sending alerts to the owner.
		/// Removes the timer from tracking.
		/// </summary>
		/// <param name="dt"></param>
		public void removeDerelictTimer(long gridId) {
			foreach (DerelictTimer.DT_INFO dt in m_SavedState.DerelictTimers) {
				if (dt.GridID == gridId) {
					m_SavedState.DerelictTimers.Remove(dt);
					break;
				}
			}
		}


		public DerelictTimer.DT_INFO findActiveDerelictTimer(long gridId) {
			foreach (DerelictTimer.DT_INFO dt in m_SavedState.DerelictTimers) {
				if (dt.GridID == gridId)
					return dt;
			}

			return null;
		}

		#endregion
		#region Load & Save

		/// <summary>
		/// Loads the last saved state from the file
		/// </summary>
		/// <returns>True if file could be found</returns>
		private bool loadState() {
			try {
				if (MyAPIGateway.Utilities.FileExistsInLocalStorage(
					Constants.StateFileName, typeof(SavedState))
				) {
					DateTime startTime = DateTime.UtcNow;

					TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(
						Constants.StateFileName, typeof(SavedState));
					m_SavedState =
						MyAPIGateway.Utilities.SerializeFromXML<SavedState>(reader.ReadToEnd());
					if (m_SavedState == null) {
						log("Read null m_SavedState", "loadState");
						return false;
					}

					// Once the state is loaded from the file there's some housekeeping to do
					// Make a copy of the list to iterate so we can remove from the actual one
					List<DerelictTimer.DT_INFO> copy =
						new List<DerelictTimer.DT_INFO>(m_SavedState.DerelictTimers);
					foreach (DerelictTimer.DT_INFO timer in copy) {
						// Need to keep track of when the server was started and how many
						// millis were remaining at that time
						// This is critical for saving again later
						timer.StartingMillisRemaining = timer.MillisRemaining;
						timer.StartTime = startTime;

						if (timer.StartingMillisRemaining <= 0) {
							m_SavedState.DerelictTimers.Remove(timer);
						}
					}

					log("State loaded from file", "loadState");
					return true;
				} else {
					log("State file not found", "loadState");
					return false;
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "loadState");
				return false;
			}
		}

		public void saveState() {
			try {
				log("Saving state to file", "saveState");

				// Before we can actually do any writing we need to see where the timers currently stand
				DateTime now = DateTime.UtcNow;
				foreach (DerelictTimer.DT_INFO timer in m_SavedState.DerelictTimers) {
					// If this results in a negative time remaining, it means the timer expired but
					// hasn't been removed from the dictionary yet.  We'll leave it alone and let it go
					// to the file, but when we try to load it later it'll get dropped
					int difference = (int)(now - timer.StartTime).TotalMilliseconds;
					timer.MillisRemaining = timer.StartingMillisRemaining - difference;
				}

				// Write the state to the file
				TextWriter writer =
					MyAPIGateway.Utilities.WriteFileInLocalStorage(
					Constants.StateFileName, typeof(SavedState));
				writer.Write(MyAPIGateway.Utilities.SerializeToXML<SavedState>(m_SavedState));
				writer.Flush();
				log("Write finished", "saveState");
			} catch (Exception e) {
				log("Exception occured: " + e, "saveState");
			}
		}

		#endregion

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

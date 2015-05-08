using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.ModAPI;

namespace GardenConquest {

	/// <summary>
	/// Singleton class which tracks state, accessible to other classes.
	/// Contains information on last round results, current faction fleets, etc
	/// </summary>
	public class StateTracker {

		public Dictionary<long, long> TokensLastRound { get; private set; }
		private Dictionary<long, FactionFleet> m_Fleets = null;
		private Queue<ActiveDerelictTimer> m_NewDerelictTimers = null;
		private Queue<ActiveDerelictTimer.COMPLETED_TIMER> m_FinishedDerelictTimers = null;
		private SavedState m_SavedState = null;

		private static StateTracker s_Instance = null;

		private static Logger s_Logger = null;

		private StateTracker() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "StateTracker");

			TokensLastRound = new Dictionary<long, long>();
			m_Fleets = new Dictionary<long, FactionFleet>();
			m_NewDerelictTimers = new Queue<ActiveDerelictTimer>();
			m_FinishedDerelictTimers = new Queue<ActiveDerelictTimer.COMPLETED_TIMER>();

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
		/// Returns the fleet for the faction id.  If no fleet yet recorded creates a new one.
		/// </summary>
		/// <param name="factionId"></param>
		/// <returns></returns>
		public FactionFleet getFleet(long factionId) {
			if (!m_Fleets.ContainsKey(factionId))
				m_Fleets.Add(factionId, new FactionFleet(factionId));
			return m_Fleets[factionId];
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns>True if queue has at least one grid</returns>
		public bool newDerelictTimers() {
			return m_NewDerelictTimers.Count > 0;
		}

		/// <summary>
		/// Gets the next grid off the new derelict timer queue
		/// </summary>
		/// <returns>Next grid</returns>
		public ActiveDerelictTimer nextNewDerelictTimer() {
			return m_NewDerelictTimers.Dequeue();
		}

		/// <summary>
		/// Adds a new derelict timer to the queue.
		/// This will be used to alert the faction
		/// </summary>
		/// <param name="dt"></param>
		public void addNewDerelictTimer(ActiveDerelictTimer dt) {
			m_NewDerelictTimers.Enqueue(dt);
			m_SavedState.DerelictTimers.Add(dt);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns>True if there is at least one new finished timer</returns>
		public bool finishedDerelictTimers() {
			return m_FinishedDerelictTimers.Count > 0;
		}

		/// <summary>
		/// Gets the next completed derelict timer
		/// </summary>
		/// <returns></returns>
		public ActiveDerelictTimer.COMPLETED_TIMER nextFinishedDerelictTimer() {
			return m_FinishedDerelictTimers.Dequeue();
		}

		public ActiveDerelictTimer findActiveDerelictTimer(long gridId) {
			foreach (ActiveDerelictTimer dt in m_SavedState.DerelictTimers) {
				if (dt.GridID == gridId)
					return dt;
			}

			return null;
		}

		/// <summary>
		/// Marks a derelict timer as finished.
		/// Used for sending alerts to the owner.
		/// Removes the timer from tracking.
		/// </summary>
		/// <param name="dt"></param>
		public void addFinishedDerelictTimer(ActiveDerelictTimer dt, ActiveDerelictTimer.COMPLETION c) {
			m_FinishedDerelictTimers.Enqueue(new ActiveDerelictTimer.COMPLETED_TIMER() {
				Timer = dt,
				Reason = c
			});
			m_SavedState.DerelictTimers.Remove(dt);
		}

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
					List<ActiveDerelictTimer> copy =
						new List<ActiveDerelictTimer>(m_SavedState.DerelictTimers);
					foreach (ActiveDerelictTimer timer in copy) {
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
				foreach (ActiveDerelictTimer timer in m_SavedState.DerelictTimers) {
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

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

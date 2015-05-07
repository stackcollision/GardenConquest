using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GardenConquest.Records;
using Sandbox.Common;
using Sandbox.ModAPI;

namespace GardenConquest.Core {

	/// <summary>
	/// Singleton class which tracks state, accessible to other classes.
	/// Contains information on last round results, current faction fleets, etc
	/// </summary>
	public class StateTracker {

		public struct DERELICT_TIMER {
			public enum TIMER_TYPE {
				STARTED, CANCELLED, FINISHED
			}

			public IMyCubeGrid grid;
			public TIMER_TYPE timerType;
		}

		public Dictionary<long, long> TokensLastRound { get; private set; }
		private Dictionary<long, FactionFleet> m_Fleets = null;
		private Queue<DERELICT_TIMER> m_NewDerelictTimers = null;
		private SavedState m_SavedState = null;

		private static StateTracker s_Instance = null;

		private static Logger s_Logger = null;

		private StateTracker() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "StateTracker");

			TokensLastRound = new Dictionary<long, long>();
			m_Fleets = new Dictionary<long, FactionFleet>();
			m_NewDerelictTimers = new Queue<DERELICT_TIMER>();

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
		public DERELICT_TIMER nextNewDerelictTimer() {
			return m_NewDerelictTimers.Dequeue();
		}

		/// <summary>
		/// Adds a new derelict timer to the queue.
		/// This will be used to alert the faction
		/// </summary>
		/// <param name="dt"></param>
		public void addNewDerelictTimer(DERELICT_TIMER dt) {
			m_NewDerelictTimers.Enqueue(dt);
		}

		/// <summary>
		/// Loads the last saved state from the file
		/// </summary>
		/// <returns>True if file could be found</returns>
		private bool loadState() {
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(
				Constants.StateFileName, typeof(SavedState))
			) {
				DateTime startTime = DateTime.UtcNow;

				TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(
					Constants.StateFileName, typeof(SavedState));
				m_SavedState =
					MyAPIGateway.Utilities.SerializeFromXML<SavedState>(reader.ReadToEnd());

				// Once the state is loaded from the file there's some housekeeping to do
				List<long> negative = new List<long>();
				foreach (
					KeyValuePair<long, SavedState.ActiveDerelictTimer> pair
					in m_SavedState.DerelictTimers
				) {
					// Need to keep track of when the server was started and how many
					// millis were remaining at that time
					// This is critical for saving again later
					pair.Value.StartingMillisRemaining = pair.Value.MillisRemaining;
					pair.Value.StartTime = startTime;

					// If a timer has a negative remaining time just ignore it
					negative.Add(pair.Key);
				}

				// Remove the negative timers we marked earlier
				foreach (long key in negative)
					m_SavedState.DerelictTimers.Remove(key);

				log("State loaded from file", "loadState");
				return true;
			} else {
				log("State file not found", "loadState");
				return false;
			}
		}

		public void saveState() {
			log("Saving state to file", "saveState");

			// Before we can actually do any writing we need to see where the timers currently stand
			DateTime now = DateTime.UtcNow;
			foreach (
					KeyValuePair<long, SavedState.ActiveDerelictTimer> pair
					in m_SavedState.DerelictTimers
			) {
				// If this results in a negative time remaining, it means the timer expired but
				// hasn't been removed from the dictionary yet.  We'll leave it alone and let it go
				// to the file, but when we try to load it later it'll get dropped
				long difference = (long)(now - pair.Value.StartTime).TotalMilliseconds;
				pair.Value.MillisRemaining = pair.Value.StartingMillisRemaining - difference;
			}

			// Write the state to the file
			TextWriter writer =
				MyAPIGateway.Utilities.WriteFileInLocalStorage(
				Constants.StateFileName, typeof(SavedState));
			writer.Write(MyAPIGateway.Utilities.SerializeToXML<SavedState>(m_SavedState));
			writer.Flush();
			log("Write finished", "saveState");
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

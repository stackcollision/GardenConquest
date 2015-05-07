using System;
using System.Collections.Generic;
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

		private static StateTracker s_Instance = null;
		
		private StateTracker() {
			TokensLastRound = new Dictionary<long, long>();
			m_Fleets = new Dictionary<long, FactionFleet>();
			m_NewDerelictTimers = new Queue<DERELICT_TIMER>();
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
	}
}

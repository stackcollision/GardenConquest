using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GardenConquest.Records;

namespace GardenConquest.Core {

	/// <summary>
	/// Singleton class which tracks state, accessible to other classes.
	/// Contains information on last round results, current faction fleets, etc
	/// </summary>
	public class StateTracker {

		public Dictionary<long, long> TokensLastRound { get; private set; }
		private Dictionary<long, FactionFleet> m_Fleets = null;

		private static StateTracker s_Instance = null;
		
		private StateTracker() {
			TokensLastRound = new Dictionary<long, long>();
			m_Fleets = new Dictionary<long, FactionFleet>();
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
	}
}

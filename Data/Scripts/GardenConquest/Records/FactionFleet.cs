using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GardenConquest.Records {

	/// <summary>
	/// Records the current classified fleet for a faction
	/// </summary>
	public class FactionFleet {
		private long m_FactionId;
		private uint[] m_Counts = null;

		private static Logger s_Logger = null;

		public FactionFleet(long facId) {
			m_FactionId = facId;
			m_Counts = new uint[Enum.GetValues(typeof(HullClass.CLASS)).Length];

			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "FactionFleet");
		}

		/// <summary>
		/// Increments the class count for a given class
		/// </summary>
		/// <param name="c">Class to increment</param>
		public void addClass(HullClass.CLASS c) {
			m_Counts[(int)c] += 1;
			debugPrint();
		}

		/// <summary>
		/// Decrements the class count for a given class
		/// </summary>
		/// <param name="c">Class to decrement</param>
		public void removeClass(HullClass.CLASS c) {
			if (m_Counts[(int)c] > 0)
				m_Counts[(int)c] -= 1;
			else
				log("Class " + c + " is already 0", "removeClass", Logger.severity.ERROR);
			debugPrint();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="c">Class to check</param>
		/// <returns>Count of c</returns>
		public uint countClass(HullClass.CLASS c) {
			return m_Counts[(int)c];
		}

		private void debugPrint() {
			log("Fleet " + m_FactionId + " has grid counts: " + 
				string.Join(", ", m_Counts));
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

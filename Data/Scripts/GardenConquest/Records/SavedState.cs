using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace GardenConquest.Records {
	
	/// <summary>
	/// Contains elements of the state which must be preserved across server restarts
	/// </summary>
	[XmlType("SavedState")]
	public class SavedState {

		/// <summary>
		/// A dereliction timer to be preserved across restarts
		/// </summary>
		/// <remarks>
		/// Can't just store the start datetime, because if the server is down for
		/// three hours the grid might be destroyed and the player can't do anything
		/// to stop it.
		/// </remarks>
		[XmlType("ActiveDerelictTimer")]
		public class ActiveDerelictTimer {
			/// <summary>
			/// What stage of dereliction is this grid at?
			/// </summary>
			public enum PHASE {
				INITIAL // The grid is not yet a derelict but is counting down to become one
			}

			public long MillisRemaining;
			public PHASE Phase;

			// Time and time remaining when server/timer starts
			[XmlIgnore]
			public DateTime StartTime;
			[XmlIgnore]
			public long StartingMillisRemaining;
		}

		public Dictionary<long, ActiveDerelictTimer> DerelictTimers;

	}
}

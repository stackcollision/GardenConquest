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

		public List<ActiveDerelictTimer> DerelictTimers;

		public SavedState() {
			DerelictTimers = new List<ActiveDerelictTimer>();
		}

	}
}

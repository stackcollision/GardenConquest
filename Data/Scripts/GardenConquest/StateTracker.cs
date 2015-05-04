using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GardenConquest {
	public class StateTracker {

		public Dictionary<long, long> TokensLastRound { get; private set; }
		
		public StateTracker() {
			TokensLastRound = new Dictionary<long, long>();
		}
	}
}

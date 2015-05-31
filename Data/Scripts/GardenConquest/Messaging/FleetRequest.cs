using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GardenConquest.Messaging {

	/// <summary>
	/// Requests Fleet data from the server
	/// </summary>
	public class FleetRequest : BaseRequest {
		// No special data needed

		public FleetRequest()
			: base(BaseRequest.TYPE.FLEET) {
			// Empty
		}
	}
}

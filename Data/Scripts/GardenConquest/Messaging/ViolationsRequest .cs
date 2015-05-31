using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GardenConquest.Messaging {

	/// <summary>
	/// Requests Violations from the server
	/// </summary>
	public class ViolationsRequest : BaseRequest {
		// No special data needed

		public ViolationsRequest()
			: base(BaseRequest.TYPE.VIOLATIONS) {
			// Empty
		}
	}
}

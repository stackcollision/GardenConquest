using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GardenConquest.Messaging {

	/// <summary>
	/// Requests CP GPS coordinates from the server
	/// </summary>
	public class SettingsRequest : BaseRequest {
		// No special data needed

		public SettingsRequest()
			: base(BaseRequest.TYPE.SETTINGS) {
			// Empty
		}
	}
}

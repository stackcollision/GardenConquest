using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GardenConquest.Extensions;

namespace GardenConquest.Messaging {

	/// <summary>
	/// Requests server to disown a client's grid
	/// </summary>
	public class DisownRequest : BaseRequest {
		private const int BaseSize = HeaderSize;

		public long EntityID;

		public DisownRequest()
			: base(BaseRequest.TYPE.DISOWN) {

		}

		public override byte[] serialize() {
			VRage.ByteStream bs = new VRage.ByteStream(BaseSize, true);

			byte[] bMessage = base.serialize();
			bs.Write(bMessage, 0, bMessage.Length);

			bs.addLong(EntityID);

			return bs.Data;
		}

		public override void deserialize(VRage.ByteStream stream) {
			base.deserialize(stream);

			EntityID = stream.getLong();
		}
	}
}

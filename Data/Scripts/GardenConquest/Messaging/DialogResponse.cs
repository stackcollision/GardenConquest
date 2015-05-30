using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Library;
using Sandbox.Common;
using System.IO;

using GardenConquest.Extensions;

namespace GardenConquest.Messaging {

	public class DialogResponse : BaseResponse {
		public String Title { get; set; }
		public String Body { get; set; }

		private const int BaseSize = sizeof(ushort) + sizeof(ushort);

		public DialogResponse()
			: base(BaseResponse.TYPE.DIALOG) {
		}

		public override byte[] serialize() {
			VRage.ByteStream bs = new VRage.ByteStream(BaseSize, true);

			byte[] bmessage = base.serialize();
			bs.Write(bmessage, 0, bmessage.Length);

			bs.addString(Body);
			bs.addString(Title);

			return bs.Data;
		}

		public override void deserialize(VRage.ByteStream stream) {
			base.deserialize(stream);

			Body = stream.getString();
			Title = stream.getString();
		}
	}
}

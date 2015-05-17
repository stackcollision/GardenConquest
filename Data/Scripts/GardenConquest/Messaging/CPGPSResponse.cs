using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GardenConquest.Extensions;

namespace GardenConquest.Messaging {

	/// <summary>
	/// Contains a list of all GPS coordinates for CPs.
	/// </summary>
	public class CPGPSResponse : BaseResponse {
		public struct CPGPS {
			public long x;
			public long y;
			public long z;
			public string name;
		}

		public List<CPGPS> CPs = null;

		private const int BaseSize = HeaderSize;

		public CPGPSResponse()
			: base(BaseResponse.TYPE.CPGPS) {
			CPs = new List<CPGPS>();
		}

		public override byte[] serialize() {
			VRage.ByteStream bs = new VRage.ByteStream(BaseSize, true);

			byte[] bmessage = base.serialize();
			bs.Write(bmessage, 0, bmessage.Length);

			bs.addUShort((ushort)CPs.Count);
			foreach (CPGPS gps in CPs) {
				bs.addLong(gps.x);
				bs.addLong(gps.y);
				bs.addLong(gps.z);
				bs.addString(gps.name);
			}

			return bs.Data;
		}

		public override void deserialize(VRage.ByteStream stream) {
			base.deserialize(stream);

			CPs.Clear();

			ushort cpCount = stream.getUShort();
			for (ushort i = 0; i < cpCount; ++i) {
				long tx, ty, tz;
				string tname;

				tx = stream.getLong();
				ty = stream.getLong();
				tz = stream.getLong();
				tname = stream.getString();

				CPs.Add(new CPGPS() { x = tx, y = ty, z = tz, name = tname });
			}
		}
	}
}

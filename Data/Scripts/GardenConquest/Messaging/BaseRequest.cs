using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GardenConquest.Extensions;

namespace GardenConquest.Messaging {
	
	/// <summary>
	/// Base class for all messages sent from client to server
	/// </summary>
	public class BaseRequest {
		public enum TYPE {
			FLEET,
			VIOLATIONS,
			SETTINGS,
			DISOWN
		}

		public TYPE MsgType { get; set; }
		public long ReturnAddress { get; set; }

		protected const int HeaderSize = sizeof(ushort) + sizeof(long);

		protected BaseRequest(TYPE t) {
			MsgType = t;
		}

		public virtual byte[] serialize() {
			VRage.ByteStream bs =
				new VRage.ByteStream(HeaderSize, true);
			bs.addUShort((ushort)MsgType);
			bs.addLong(ReturnAddress);
			return bs.Data;
		}

		public virtual void deserialize(VRage.ByteStream stream) {
			MsgType = (TYPE)stream.getUShort();
			ReturnAddress = stream.getLong();
		}

		public static BaseRequest messageFromBytes(byte[] buffer) {
			VRage.ByteStream stream = new VRage.ByteStream(buffer, buffer.Length);
			TYPE t = (TYPE)stream.getUShort();
			stream.Seek(0, System.IO.SeekOrigin.Begin);

			BaseRequest msg = null;
			switch (t) {
				case TYPE.FLEET:
					msg = new FleetRequest();
					break;
				case TYPE.SETTINGS:
					msg = new SettingsRequest();
					break;
				case TYPE.VIOLATIONS:
					msg = new ViolationsRequest();
					break;
				case TYPE.DISOWN:
					msg = new DisownRequest();
					break;
			}

			if (msg != null)
				msg.deserialize(stream);
			return msg;
		}
	}
}

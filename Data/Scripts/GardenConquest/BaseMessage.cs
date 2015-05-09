using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Library;

namespace GardenConquest {

	/// <summary>
	/// Base class for all messages sent between client and server
	/// </summary>
	public abstract class BaseMessage {
		public enum TYPE {
			NOTIFICATION
		}

		/// <summary>
		/// Determines what the destination field actually means
		/// </summary>
		public enum DEST_TYPE {
			EVERYONE,
			PLAYER,
			FACTION
		}

		public TYPE MsgType { get; protected set; }
		public DEST_TYPE DestType { get; set; }
		public long Destination { get; set; }

		protected const int HeaderSize = sizeof(ushort) + sizeof(ushort) + sizeof(long);

		protected BaseMessage(TYPE t) {
			MsgType = t;
		}

		// Thanks Keen for making me have to write these myself
		public virtual byte[] serialize() {
			VRage.ByteStream bs = new VRage.ByteStream(HeaderSize, true);
			bs.addUShort((ushort)MsgType);
			bs.addUShort((ushort)DestType);
			bs.addLong(Destination);
			return bs.Data;
		}

		public virtual void deserialize(VRage.ByteStream stream) {
			MsgType = (TYPE)stream.getUShort();
			DestType = (DEST_TYPE)stream.getUShort();
			Destination = (long)stream.getLong();
		}

		public static BaseMessage messageFromBytes(byte[] buffer) {
			VRage.ByteStream stream = new VRage.ByteStream(buffer, buffer.Length);
			TYPE t = (TYPE)stream.getUShort();
			stream.Seek(0, SeekOrigin.Begin);

			BaseMessage msg = null;
			switch (t) {
				case TYPE.NOTIFICATION:
					msg = new NotificationResponse();
					break;
			}

			if (msg != null)
				msg.deserialize(stream);
			return msg;
		}
	}
}

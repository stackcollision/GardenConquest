using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Library;

using GardenConquest.Extensions;

namespace GardenConquest.Messaging {

	/// <summary>
	/// Base class for all messages sent from server to client
	/// </summary>
	public abstract class BaseResponse {
		public enum TYPE {
			NOTIFICATION,
			CPGPS
		}

		/// <summary>
		/// Determines what the destination field actually means
		/// </summary>
		public enum DEST_TYPE {
			NONE,
			EVERYONE,
			PLAYER,
			FACTION
		}

		public TYPE MsgType { get; protected set; }
		public DEST_TYPE DestType { get; set; }
		public List<long> Destination { get; set; }

		protected const int HeaderSize = sizeof(ushort) * 3;

		protected BaseResponse(TYPE t) {
			MsgType = t;
		}

		// Thanks Keen for making me have to write these myself
		public virtual byte[] serialize() {
			int destBytes = Destination == null ? 0 : sizeof(long) * Destination.Count;
			VRage.ByteStream bs = 
				new VRage.ByteStream(HeaderSize + destBytes, true);
			bs.addUShort((ushort)MsgType);
			bs.addUShort((ushort)DestType);
			bs.addLongList(Destination);
			return bs.Data;
		}

		public virtual void deserialize(VRage.ByteStream stream) {
			MsgType = (TYPE)stream.getUShort();
			DestType = (DEST_TYPE)stream.getUShort();
			Destination = stream.getLongList();
		}

		public static BaseResponse messageFromBytes(byte[] buffer) {
			VRage.ByteStream stream = new VRage.ByteStream(buffer, buffer.Length);
			TYPE t = (TYPE)stream.getUShort();
			stream.Seek(0, SeekOrigin.Begin);

			BaseResponse msg = null;
			switch (t) {
				case TYPE.NOTIFICATION:
					msg = new NotificationResponse();
					break;

				case TYPE.CPGPS:
					msg = new CPGPSResponse();
					break;
			}

			if (msg != null)
				msg.deserialize(stream);
			return msg;
		}
	}
}

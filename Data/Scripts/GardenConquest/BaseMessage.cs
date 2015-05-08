using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;
using VRage.Library;

namespace GardenConquest {
	[ProtoContract]
	[ProtoInclude(1000, typeof(NotificationResponse))]
	public class BaseMessage {
		public enum TYPE {
			NOTIFICATION
		}

		[ProtoMember(1)]
		public TYPE MsgType { get; protected set; }
	}
}

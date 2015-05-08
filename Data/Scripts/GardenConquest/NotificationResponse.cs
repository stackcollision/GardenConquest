using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;
using VRage.Library;
using Sandbox.Common;

namespace GardenConquest {
	[ProtoContract]
	class NotificationResponse : BaseMessage {
		[ProtoMember(2)]
		public String NotificationText { get; set; }
		[ProtoMember(3)]
		public int Time { get; set; }
		[ProtoMember(4)]
		public MyFontEnum Font { get; set; }

		NotificationResponse() {
			base.MsgType = BaseMessage.TYPE.NOTIFICATION;
		}
	}
}

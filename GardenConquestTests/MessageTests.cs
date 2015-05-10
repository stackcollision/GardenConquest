using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GardenConquest;
using System.Collections.Generic;

namespace GardenConquestTests {
	[TestClass]
	public class MessageTests {
		[TestMethod]
		public void TestByteStream() {
			VRage.ByteStream bs = new VRage.ByteStream(10, true);
			bs.addUShort((ushort)37375);
			bs.addUShort((ushort)2511);
			bs.addLong(3020284874);
			byte[] buffer = bs.Data;

			VRage.ByteStream bs2 = new VRage.ByteStream(buffer, buffer.Length);
			Assert.AreEqual(37375, bs2.ReadUInt16());
			Assert.AreEqual(2511, bs2.ReadUInt16());
			Assert.AreEqual(3020284874, bs2.ReadInt64());
		}

		[TestMethod]
		public void TestNotificationMessage() {
			NotificationResponse nr = new NotificationResponse();
			nr.DestType = BaseMessage.DEST_TYPE.FACTION;
			nr.Destination = new List<long>() { 1, 2 };
			nr.NotificationText = "Test String";
			nr.Time = 2000;
			nr.Font = Sandbox.Common.MyFontEnum.Red;

			byte[] buffer = nr.serialize();

			NotificationResponse nr2 = new NotificationResponse();
			nr2.deserialize(new VRage.ByteStream(buffer, buffer.Length));

			Assert.AreEqual(nr.MsgType, nr2.MsgType);
			Assert.AreEqual(nr.DestType, nr2.DestType);
			CollectionAssert.AreEqual(nr.Destination, nr2.Destination);
			Assert.AreEqual(nr.NotificationText, nr2.NotificationText);
			Assert.AreEqual(nr.Time, nr2.Time);
			Assert.AreEqual(nr.Font, nr2.Font);
		}

		[TestMethod]
		public void TestGenericDeserialize() {
			NotificationResponse nr = new NotificationResponse();
			nr.DestType = BaseMessage.DEST_TYPE.FACTION;
			nr.Destination = new List<long>() { 2194 };
			nr.NotificationText = "Test String";
			nr.Time = 2000;
			nr.Font = Sandbox.Common.MyFontEnum.Red;

			byte[] buffer = nr.serialize();

			BaseMessage msg = BaseMessage.messageFromBytes(buffer);

			Assert.IsInstanceOfType(msg, typeof(NotificationResponse));
			Assert.AreEqual(nr.MsgType, msg.MsgType);
			Assert.AreEqual(nr.DestType, msg.DestType);
			CollectionAssert.AreEqual(nr.Destination, msg.Destination);
			Assert.AreEqual(nr.NotificationText, (msg as NotificationResponse).NotificationText);
			Assert.AreEqual(nr.Time, (msg as NotificationResponse).Time);
			Assert.AreEqual(nr.Font, (msg as NotificationResponse).Font);
		}
	}
}

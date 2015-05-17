using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GardenConquest;
using System.Collections.Generic;

using GardenConquest.Extensions;
using GardenConquest.Messaging;

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
		public void TestNotificationResponse() {
			NotificationResponse nr = new NotificationResponse();
			nr.DestType = BaseResponse.DEST_TYPE.FACTION;
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
		public void TestCPGPSRequest() {
			CPGPSRequest req = new CPGPSRequest();
			req.ReturnAddress = 5690820958;

			byte[] buffer = req.serialize();

			CPGPSRequest req2 = new CPGPSRequest();
			req2.deserialize(new VRage.ByteStream(buffer, buffer.Length));

			Assert.AreEqual(req.MsgType, req2.MsgType);
			Assert.AreEqual(req.ReturnAddress, req2.ReturnAddress);
		}

		[TestMethod]
		public void TestCPGPSResponse() {
			CPGPSResponse res = new CPGPSResponse();
			res.DestType = BaseResponse.DEST_TYPE.PLAYER;
			res.Destination = new List<long>() { 2350982 };
			res.CPs.Add(new CPGPSResponse.CPGPS() { x = 1000, y = 2000, z = 3000, name = "Test1" });
			res.CPs.Add(new CPGPSResponse.CPGPS() { x = 4000, y = 5000, z = 6000, name = "Test2" });

			byte[] buffer = res.serialize();

			CPGPSResponse res2 = new CPGPSResponse();
			res2.deserialize(new VRage.ByteStream(buffer, buffer.Length));

			Assert.AreEqual(res.MsgType, res2.MsgType);
			Assert.AreEqual(res.DestType, res2.DestType);
			CollectionAssert.AreEqual(res.Destination, res2.Destination);
			Assert.AreEqual(res.CPs[0].x, res2.CPs[0].x);
			Assert.AreEqual(res.CPs[0].y, res2.CPs[0].y);
			Assert.AreEqual(res.CPs[0].z, res2.CPs[0].z);
			Assert.AreEqual(res.CPs[0].name, res2.CPs[0].name);
			Assert.AreEqual(res.CPs[1].x, res2.CPs[1].x);
			Assert.AreEqual(res.CPs[1].y, res2.CPs[1].y);
			Assert.AreEqual(res.CPs[1].z, res2.CPs[1].z);
			Assert.AreEqual(res.CPs[1].name, res2.CPs[1].name);
		}

		[TestMethod]
		public void TestGenericDeserialize() {
			NotificationResponse nr = new NotificationResponse();
			nr.DestType = BaseResponse.DEST_TYPE.FACTION;
			nr.Destination = new List<long>() { 2194 };
			nr.NotificationText = "Test String";
			nr.Time = 2000;
			nr.Font = Sandbox.Common.MyFontEnum.Red;

			byte[] buffer = nr.serialize();

			BaseResponse msg = BaseResponse.messageFromBytes(buffer);

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

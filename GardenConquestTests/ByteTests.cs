using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GardenConquest;

namespace GardenConquestTests {
	[TestClass]
	public class ByteTests {
		[TestMethod]
		public void TestUShort() {
			ushort input = ushort.MaxValue;
			VRage.ByteStream stream = new VRage.ByteStream(1, true);
			stream.addUShort(input);
			stream.Seek(0, System.IO.SeekOrigin.Begin);
			Assert.AreEqual(input, stream.getUShort());
		}

		[TestMethod]
		public void TestLongPositive() {
			long input = long.MaxValue;
			VRage.ByteStream stream = new VRage.ByteStream(1, true);
			stream.addLong(input);
			stream.Seek(0, System.IO.SeekOrigin.Begin);
			Assert.AreEqual(input, stream.getLong());
		}

		[TestMethod]
		public void TestLongNegative() {
			long input = long.MinValue;
			VRage.ByteStream stream = new VRage.ByteStream(1, true);
			stream.addLong(input);
			stream.Seek(0, System.IO.SeekOrigin.Begin);
			Assert.AreEqual(input, stream.getLong());
		}

		[TestMethod]
		public void TestString() {
			string input = "This is a test string";
			VRage.ByteStream stream = new VRage.ByteStream(1, true);
			stream.addString(input);
			stream.Seek(0, System.IO.SeekOrigin.Begin);
			string output = stream.getString();

			Assert.AreEqual(input.Length, output.Length);
			Assert.AreEqual(input, output);
		}
	}
}

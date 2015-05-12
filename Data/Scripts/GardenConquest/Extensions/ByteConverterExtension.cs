using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Library;

namespace GardenConquest.Extensions {

	/// <summary>
	/// This class has to exist because apparently some of the most useful, basic stuff
	/// is not included on the whitelist. Read: ByteConverter, MemoryStream, etc
	/// 
	/// Keen, if you're reading this, I hate you
	/// </summary>
	public static class ByteConverterExtension {

		public static void addUShort(this VRage.ByteStream stream, ushort v) {
			for (byte i = 0; i < sizeof(ushort); ++i)
				stream.WriteByte((byte)((v >> (i * 8)) & 0xFF));
		}

		public static ushort getUShort(this VRage.ByteStream stream) {
			ushort v = 0;
			for (byte i = 0; i < sizeof(ushort); ++i)
				v |= (ushort)((ushort)(stream.ReadByte()) << (i * 8));
			return v;
		}

		public static void addLong(this VRage.ByteStream stream, long v) {
			ulong u = (ulong)v;
			for (byte i = 0; i < sizeof(ulong); ++i)
				stream.WriteByte((byte)((u >> (i * 8)) & 0xFF));
		}

		public static long getLong(this VRage.ByteStream stream) {
			ulong v = 0;
			for (byte i = 0; i < sizeof(ulong); ++i)
				v |= (ulong)((ulong)(stream.ReadByte()) << (i * 8));
			return (long)v;
		}

		public static void addString(this VRage.ByteStream stream, string s) {
			if (s.Length > ushort.MaxValue) {
				stream.addUShort(0);
				return;
			}

			// Write length
			stream.addUShort((ushort)s.Length);

			// Write data
			char[] sarray = s.ToCharArray();
			for (ushort i = 0; i < s.Length; ++i)
				stream.WriteByte((byte)sarray[i]);
		}

		public static string getString(this VRage.ByteStream stream) {
			// Read length
			ushort len = stream.getUShort();

			// Read data
			char[] cstr = new char[len];
			for (ushort i = 0; i < len; ++i)
				cstr[i] = (char)stream.ReadByte();
			return new string(cstr);
		}

		public static void addLongList(this VRage.ByteStream stream, List<long> L) {
			if (L == null) {
				stream.addUShort(0);
				return;
			}

			// Write length
			stream.addUShort((ushort)L.Count);

			// Write data
			foreach (long l in L)
				stream.addLong(l);
		}

		public static List<long> getLongList(this VRage.ByteStream stream) {
			List<long> L = new List<long>();

			// Read length
			ushort len = stream.getUShort();
			if (len == 0)
				return null;

			// Read data
			for (ushort i = 0; i < len; ++i)
				L.Add(stream.getLong());

			return L;
		}

	}
}

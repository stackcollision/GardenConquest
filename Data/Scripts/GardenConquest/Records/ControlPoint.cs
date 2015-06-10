using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;

using GardenConquest.Extensions;

namespace GardenConquest.Records {
	[XmlType("CP")]
	public class ControlPoint {
		[XmlElement("Name")]
		public String Name { get; set; }
		[XmlElement("Position")]
		public VRageMath.Vector3D Position { get; set; }
		[XmlElement("Radius")]
		public int Radius { get; set; }
		[XmlElement("Reward")]
		public int TokensPerPeriod { get; set; }

		public void serialize(VRage.ByteStream stream) {
			stream.addLong((long)Position.X);
			stream.addLong((long)Position.Y);
			stream.addLong((long)Position.Z);
			stream.addString(Name);
			stream.addLong(Radius);
			stream.addLong(TokensPerPeriod);
		}

		public static ControlPoint deserialize(VRage.ByteStream stream) {
			ControlPoint result = new ControlPoint();

			long x, y, z;
			x = stream.getLong();
			y = stream.getLong();
			z = stream.getLong();
			result.Position = new VRageMath.Vector3D(x, y, z);

			result.Name = stream.getString();
			result.Radius = (int)stream.getLong();
			result.TokensPerPeriod = (int)stream.getLong();

			return result;
		}
	}
}

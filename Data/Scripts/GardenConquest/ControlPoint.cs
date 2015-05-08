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
using VRage.Library.Utils;

namespace GardenConquest {
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
	}

}

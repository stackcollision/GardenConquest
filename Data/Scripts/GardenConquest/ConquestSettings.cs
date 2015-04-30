using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace GardenConquest {
	[XmlType("GardenConquestSettings")]
	public class ConquestSettings {
		public List<ControlPoint> ControlPoints { get; set; }
		public int Period { get; set; }

		public ConquestSettings() {
			ControlPoints = new List<ControlPoint>();
		}
	}
}

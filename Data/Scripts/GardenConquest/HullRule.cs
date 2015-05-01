using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace GardenConquest {
	public class HullRule {
		public int MaxBlocks { get; set; }

		public HullRule() {
			MaxBlocks = 1;
		}
	}
}

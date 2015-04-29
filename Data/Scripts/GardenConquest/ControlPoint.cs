using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Library.Utils;

namespace GardenConquest {

	class ControlPoint {
		public String Name { get; set; }
		public VRageMath.Vector3D Position { get; set; }
		public int Radius { get; set; }
		public int TokensPerPeriod { get; set; }
	}

}

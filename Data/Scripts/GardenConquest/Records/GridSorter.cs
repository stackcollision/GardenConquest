using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.ModAPI;

namespace GardenConquest.Records {
	public struct FACGRID {
		public IMyCubeGrid grid;
		public long blockCount;
		public Utility.GRIDTYPE gtype;
	}

	public class GridSorter : IComparer<FACGRID> {
		/// <summary>
		/// Compares by grid size and then block count
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		public int Compare(FACGRID a, FACGRID b) {
			if ((int)a.gtype < (int)b.gtype) {
				return -1;
			} else if ((int)a.gtype > (int)b.gtype) {
				return 1;
			} else {
				if (a.blockCount > b.blockCount)
					return -1;
				else if (a.blockCount < b.blockCount)
					return 1;
				else
					return 0;
			}
		}
	}
}

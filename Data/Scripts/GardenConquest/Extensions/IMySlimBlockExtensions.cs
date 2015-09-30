using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.ModAPI;
using InGame = Sandbox.ModAPI.Ingame;
using Interfaces = Sandbox.ModAPI.Interfaces;

namespace GardenConquest.Extensions
{
	/// <summary>
	/// Helper functions for IMySlimBlocks
	/// </summary>
	public static class IMySlimBlockExtensions {

		/// <summary>
		/// Is this block a hull classifier?
		/// </summary>
		/// <param name="block"></param>
		/// <returns></returns>
		public static bool isClassifierBlock(this IMySlimBlock block) {
			if (block.FatBlock == null) {
				return false;
			}

			String subTypeName = block.FatBlock.BlockDefinition.SubtypeName;
			if (subTypeName.Contains(Blocks.HullClassifier.SHARED_SUBTYPE)) {
				return true ;
			}

			return false;
		}
	}
}

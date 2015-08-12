using System;
using System.Collections.Generic;

using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

using GardenConquest.Records;
using GardenConquest.Core;

namespace GardenConquest.Blocks {

	/// <summary>
	/// Helper methods for Classifier Blocks
	/// </summary>
	public class HullClassifier {

		#region Static

		public const String SHARED_SUBTYPE = "HullClassifier";
		public readonly static String[] SUBTYPES_IN_CLASS_ORDER = {
			"Unclassified",
			"Unlicensed",
			"Worker", "Foundry",
			"Scout", "Fighter", "Gunship",
			"Corvette",
			"Frigate",
			"Destroyer", "Cruiser",
			"HeavyCruiser", "Battleship",
			"Outpost", "Installation", "Fortress"
		};
		private static Logger s_Logger;

		/// <summary>
		/// If we recognize the subtype as belonging to a specific classifier, return its CLASS
		/// Otherwise, return Unclassified
		/// </summary>
		private static HullClass.CLASS HullClassFromSubTypeString(String subTypeString) {
			int longestMatchIndex = -1;
			String subtype;
			for (int i = 0; i < SUBTYPES_IN_CLASS_ORDER.Length; i++) {
				subtype = SUBTYPES_IN_CLASS_ORDER[i];
				if (subTypeString.Contains(subtype)) {
					if (longestMatchIndex == -1) {
						longestMatchIndex = i;
					} else if (subtype.Length > SUBTYPES_IN_CLASS_ORDER[longestMatchIndex].Length) {
							longestMatchIndex = i;
					}
				}
			}

			if (longestMatchIndex > -1) {
				return (HullClass.CLASS)longestMatchIndex;
			}

			// subtype not recognized, this shouldn't happen
			log("Classifier Subtype not recognized, defaulting to Unclassified", 
				"IDFromSubTypeString", Logger.severity.ERROR);
			return HullClass.CLASS.UNCLASSIFIED;
		}

		/// <summary>
		/// If we recognize the block's subtype as belonging to a classifier, return true
		/// </summary>
		public static bool isClassifierBlock(IMySlimBlock block) {
			IMyCubeBlock fatblock = block.FatBlock;
			if (fatblock != null && fatblock is Ingame.IMyBeacon) {
				String subTypeName = fatblock.BlockDefinition.SubtypeName;
				if (subTypeName.Contains(SHARED_SUBTYPE))
					return true;
			}

			return false;
		}

		private static void log(String message, String method = null, 
			Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger == null)
				s_Logger = new Logger("Static", "HullClassifier");

			s_Logger.log(level, method, message);
		}

		#endregion
		#region instance

		String m_SubTypeName;

		public IMySlimBlock SlimBlock { get; private set; }
		public IMyCubeBlock FatBlock { get; private set; }
		public HullClass.CLASS Class { get; private set; }

		public HullClassifier(IMySlimBlock block) {
			SlimBlock = block;
			FatBlock = block.FatBlock;
			m_SubTypeName = FatBlock.BlockDefinition.SubtypeName;
			Class = HullClassFromSubTypeString(m_SubTypeName);
		}

		#endregion
	}
}

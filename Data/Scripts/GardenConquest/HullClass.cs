using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GardenConquest {
	public static class HullClass {
		public enum CLASS {
			UNCLASSIFIED = 0,
			FIGHTER = 1,
			CORVETTE = 2,
			FRIGATE = 3,
			DESTROYER = 4,
			CRUISER = 5,
			BATTLESHIP = 6,
			CARRIER = 7,
			UTILITY = 8
		}

		public static String[] ClassStrings = {
										   "Unclassified",
										   "Fighter",
										   "Corvette",
										   "Frigate",
										   "Destroyer",
										   "Cruiser",
										   "Battleship",
										   "Carrier",
										   "Utility"
									   };

		public static CLASS hullClassFromString(String subtype) {
			if (subtype.Contains("Fighter")) {
				return CLASS.FIGHTER;
			} else if (subtype.Contains("Corvette")) {
				return CLASS.CORVETTE;
			} else if (subtype.Contains("Frigate")) {
				return CLASS.FRIGATE;
			} else if (subtype.Contains("Destroyer")) {
				return CLASS.DESTROYER;
			} else if (subtype.Contains("Cruiser")) {
				return CLASS.CRUISER;
			} else if (subtype.Contains("Battleship")) {
				return CLASS.BATTLESHIP;
			} else if (subtype.Contains("Carrier")) {
				return CLASS.CARRIER;
			} else if (subtype.Contains("Utility")) {
				return CLASS.UTILITY;
			} else {
				return CLASS.UNCLASSIFIED;
			}
		}
	}
}

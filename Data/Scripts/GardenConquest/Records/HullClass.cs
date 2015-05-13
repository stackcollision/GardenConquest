using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GardenConquest.Records {
	public static class HullClass {
		public enum CLASS {
			UNCLASSIFIED = 0,
			// Unlicensed
			UNLICENSED = 1,
			// Utility
			WORKER = 2,
			FOUNDRY = 3,
			// Strikecraft
			SCOUT = 4,
			FIGHTER = 5,
			GUNSHIP = 6,
			// Capital ships
			CORVETTE = 7,
			FRIGATE = 8,
			DESTROYER = 9,
			CRUISER = 10,
			HEAVYCRUISER = 11,
			BATTLESHIP = 12,
			// Stations
			OUTPOST = 13,
			INSTALLATION = 14,
			FORTRESS = 15
		}

		public static int[] captureMultiplier = {
													0, //UNCLASSIFIED
													1, //UNLICENSED
													1, //WORKER
													1, //FOUNDRY
													1, //SCOUT
													1, //FIGHTER
													2, //GUNSHIP
													2, //CORVETTE
													3, //FRIGATE
													4, //DESTROYER
													5, //CRUISER
													6, //HEAVYCRUISER
													7, //BATTLESHIP
													2, //OUTPOST
													4, //INSTALLATION
													6, //FORTRESS
												};

		public static String[] ClassStrings = {
										   "Unclassified",
										   "Unlicensed",
										   "Worker",
										   "Foundry",
										   "Scout",
										   "Fighter",
										   "Gunship",
										   "Corvette",
										   "Frigate",
										   "Destroyer",
										   "Cruiser",
										   "Heavy Cruiser",
										   "Battleship",
										   "Outpost",
										   "Installation",
										   "Fortress"
									   };

		public static CLASS hullClassFromString(String subtype) {
			if (subtype.Contains("Unlicensed")) {
				return CLASS.UNLICENSED;
			} else if (subtype.Contains("Utility")) {
				return CLASS.WORKER;
			} else if (subtype.Contains("Foundry")) {
				return CLASS.FOUNDRY;
			} else if (subtype.Contains("Scout")) {
				return CLASS.SCOUT;
			} else if (subtype.Contains("Fighter")) {
				return CLASS.FIGHTER;
			} else if (subtype.Contains("Gunship")) {
				return CLASS.GUNSHIP;
			} else if (subtype.Contains("Corvette")) {
				return CLASS.CORVETTE;
			} else if (subtype.Contains("Frigate")) {
				return CLASS.FRIGATE;
			} else if (subtype.Contains("Destroyer")) {
				return CLASS.DESTROYER;
			} else if (subtype.Contains("Cruiser")) {
				return CLASS.CRUISER;
			} else if (subtype.Contains("HeavyCruiser")) {
				return CLASS.HEAVYCRUISER;
			} else if (subtype.Contains("Battleship")) {
				return CLASS.BATTLESHIP;
			} else if (subtype.Contains("Outpost")) {
				return CLASS.OUTPOST;
			} else if (subtype.Contains("Installation")) {
				return CLASS.INSTALLATION;
			} else if (subtype.Contains("Fortress")) {
				return CLASS.FORTRESS;
			} else {
				return CLASS.UNCLASSIFIED;
			}
		}
	}
}

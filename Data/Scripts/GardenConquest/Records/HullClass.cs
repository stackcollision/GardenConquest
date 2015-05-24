namespace GardenConquest.Records {

	/// <summary>
	/// Represents one of the Static classes we know about on init,
	/// each one corresponds to a HullClassifier block
	/// </summary>
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

	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.IO;

using Sandbox.Common;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;

using GardenConquest.Records;

namespace GardenConquest.Core {

	/// <summary>
	/// Singleton class to store settings for the server.
	/// </summary>
	public class ConquestSettings {

		#region Static

		[XmlType("GardenConquestSettings")]
		public struct SETTINGS {
			public List<ControlPoint> ControlPoints { get; set; }
			public int CPPeriod { get; set; }
			public int CleanupPeriod { get; set; }
			public BlockType[] BlockTypes { get; set; }
			[XmlElement("Classes")]
			public HullRuleSet[] HullRules { get; set; }
		}

		private static readonly int DEFAULT_CP_PERIOD = 900; // 15 minutes
		private static readonly int DEFAULT_CLEANUP_PERIOD = 1800; // 30 minutes
		private static Logger s_Logger;
		private static ConquestSettings s_Instance;

		/// <summary>
		/// Get singleton instance
		/// </summary>
		public static ConquestSettings getInstance() {
			if (s_Instance == null)
				s_Instance = new ConquestSettings();
			return s_Instance;
		}

		#endregion
		#region Properties

		private SETTINGS m_Settings;

		public List<ControlPoint> ControlPoints {
			get { return m_Settings.ControlPoints; }
		}
		public int CPPeriod { 
			get { return m_Settings.CPPeriod; }
		}
		public int CleanupPeriod {
			get { return m_Settings.CleanupPeriod; }
		}
		public BlockType[] BlockTypes { 
			get { return m_Settings.BlockTypes; }
		}
		public HullRuleSet[] HullRules {
			get { return m_Settings.HullRules; }
		}
		public SETTINGS Settings {
			get { return m_Settings; }
		}
		public bool WriteFailed { get; private set; }

		#endregion
		#region Lifecycle

		private ConquestSettings() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "ConquestSettings");

			if (!loadSettings())
				loadDefaults();

			log("Settings initialized", "ctor");
			log("Period: " + CPPeriod + " seconds.", "start");
			log("DerelictCountdown: " + CleanupPeriod + " seconds.", "start");
		}

		#endregion
		#region Methods

		/// <summary>
		/// Loads settings from the configuration XML.
		/// </summary>
		/// <returns>False if no file exists</returns>
		private bool loadSettings() {
			log("Attempting to load settings from file", "loadSettings");

			try {
				// file missing?
				if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(
					Constants.ConfigFileName, typeof(SETTINGS))) {
					log("No config file found", "loadSettings");
					return false;
				}

				// load file
				TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(
					Constants.ConfigFileName, typeof(SETTINGS));
				m_Settings = MyAPIGateway.Utilities.SerializeFromXML<SETTINGS>(
					reader.ReadToEnd());
			}
			catch (Exception e) {
				log("Loading settings before MyAPIGateway is initialized. " +
					"Using defaults instead. Error: " + e,
					"loadSettings", Logger.severity.ERROR);

				return false;
			}

			// Fill missing Settings with defaults
			bool saveAfterLoad = false;

			if (ControlPoints == null) {
				log("No ControlPoints, using default", "loadSettings");
				m_Settings.ControlPoints = defaultControlPoints();
				saveAfterLoad = true;
			}
			if (CPPeriod == 0) {
				log("No Period, using default", "loadSettings");
				m_Settings.CPPeriod = DEFAULT_CP_PERIOD;
				saveAfterLoad = true;
			}
			if (CleanupPeriod == 0) {
				log("No DerelictCountdown, using default", "loadSettings");
				m_Settings.CleanupPeriod = DEFAULT_CLEANUP_PERIOD;
				saveAfterLoad = true;
			}
			if (BlockTypes == null) {
				log("No BlockTypes, using default", "loadSettings");
				m_Settings.BlockTypes = defaultBlockTypes();
				saveAfterLoad = true;
			}
			if (HullRules == null) {
				log("No HullRules, using default", "loadSettings");
				m_Settings.HullRules = defaultHullRules();
				saveAfterLoad = true;
			}

			log("Settings loaded", "loadSettings");

			if (saveAfterLoad) {
				log("Saving some settings loaded from defaults", "loadSettings");
				writeSettings();
			}
			return true;
		}

		/// <summary>
		/// Writes the current settings to a file.
		/// Used to produce a config file when none exists
		/// </summary>
		public void writeSettings() {
			log("Writing config file", "writeSettings");
			try {
				TextWriter writer =
					MyAPIGateway.Utilities.WriteFileInLocalStorage(
					Constants.ConfigFileName, typeof(SETTINGS));

				writer.Write(MyAPIGateway.Utilities.SerializeToXML<SETTINGS>(m_Settings));
				writer.Flush();
				writer.Close();
				writer = null;
				log("Config written", "writeSettings");

				WriteFailed = false;
			}
			catch (Exception e) {
				log("Writing settings before MyAPIGateway is initialized, " +
					"They won't be saved :( Error : " + e,
					"loadSettings", Logger.severity.ERROR);

				WriteFailed = true;
			}
		}

		/// <summary>
		/// Sets default settings to be create a new config file.
		/// </summary>
		private void loadDefaults() {
			log("Loading default settings", "loadDefaults");
			m_Settings = new SETTINGS();
			m_Settings.ControlPoints = defaultControlPoints();
			m_Settings.CPPeriod = DEFAULT_CP_PERIOD;
			m_Settings.CleanupPeriod = DEFAULT_CLEANUP_PERIOD;
			m_Settings.BlockTypes  = defaultBlockTypes();
			m_Settings.HullRules = defaultHullRules();
			writeSettings();
		}

		public int blockTypeID(BlockType t) {
			//log("start", "blockTypeID(BlockType t)");
			for (int i = 0; i < BlockTypes.Length; i++) {
				//log("comparing iterated type " + BlockTypes[i].GetHashCode() + " - " + BlockTypes[i].ToString() +" - " + BlockTypes[i].DisplayName, "blockTypeID(BlockType t)");
				//log("to given type           " + t.GetHashCode() + " - " + t.ToString() + " - " + t.DisplayName, "blockTypeID(BlockType t)");
				if (t.Equals(BlockTypes[i])) {
					//log("they are equal!", "blockTypeID(BlockType t)");
					return i;
				}
			}

			log("BlockType not found! Pretending it's the first one.",
				"blockTypeID",Logger.severity.ERROR);
			return 0;
		}

		#endregion
		#region Defaults

		private List<ControlPoint> defaultControlPoints(){
			return new List<ControlPoint> {
				new ControlPoint() {
					Name = "Center",
					Position = new VRageMath.Vector3D(0, 0, 0),
					Radius = 15000,
					TokensPerPeriod = 5,
				}
			};
		}

		private BlockType[] defaultBlockTypes() {
			return new BlockType[] {
				new BlockType() {
					DisplayName = "Turrets",
					SubTypeStrings = new List<string>() { "turret" }
				},
				new BlockType() {
					DisplayName = "Static Weapons",
					SubTypeStrings = new List<string>() { "MissileLauncher",
						"GatlingGun" }
				}
			};
		}

		private HullRuleSet[] defaultHullRules() {
			// Hull Rules indexed by Class
			int totalHullClasses = Enum.GetNames(typeof(HullClass.CLASS)).Length;
			HullRuleSet[] results = new HullRuleSet[totalHullClasses];

			results[(int)HullClass.CLASS.UNCLASSIFIED] = new HullRuleSet() {
				DisplayName = "Unclassified",
				MaxPerFaction = 0,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 0,
				MaxBlocks = 25,
				BlockTypeLimits = new int[2] { 0, 0 }
			};
			results[(int)HullClass.CLASS.UNLICENSED] = new HullRuleSet() {
				DisplayName = "Unlicensed",
				MaxPerFaction = 2,
				MaxPerSoloPlayer = 2,
				CaptureMultiplier = 1,
				MaxBlocks = 100,
				BlockTypeLimits= new int[2] { 2, 2 }
			};
			results[(int)HullClass.CLASS.WORKER] = new HullRuleSet() {
				DisplayName = "Worker",
				MaxPerFaction = 12,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 1,
				MaxBlocks = 200,
				BlockTypeLimits = new int[2] { 0, 0 }
			};
			results[(int)HullClass.CLASS.FOUNDRY] = new HullRuleSet() {
				DisplayName = "Foundry",
				MaxPerFaction = 2,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 1,
				MaxBlocks = 1000,
				BlockTypeLimits = new int[2] { 0, 0 }
			};
			results[(int)HullClass.CLASS.SCOUT] = new HullRuleSet() {
				DisplayName = "Scout",
				MaxPerFaction = 16,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 1,
				MaxBlocks = 200,
				BlockTypeLimits = new int[2] { 0, 2 }
			};
			results[(int)HullClass.CLASS.FIGHTER] = new HullRuleSet() {
				DisplayName = "Fighter",
				MaxPerFaction = 32,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 1,
				MaxBlocks = 525,
				BlockTypeLimits = new int[2] { 0, 3 }
			};
			results[(int)HullClass.CLASS.GUNSHIP] = new HullRuleSet() {
				DisplayName = "Gunship",
				MaxPerFaction = 8,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 2,
				MaxBlocks = 1025,
				BlockTypeLimits = new int[2] { 1, 4 }
			};
			results[(int)HullClass.CLASS.CORVETTE] = new HullRuleSet() {
				DisplayName = "Corvette",
				MaxPerFaction = 10,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 2,
				MaxBlocks = 200,
				BlockTypeLimits = new int[2] { 2, 2 }
			};
			results[(int)HullClass.CLASS.FRIGATE] = new HullRuleSet() {
				DisplayName = "Frigate",
				MaxPerFaction = 5,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 3,
				MaxBlocks = 600,
				BlockTypeLimits = new int[2] { 4, 4 }
			};

			results[(int)HullClass.CLASS.DESTROYER] = new HullRuleSet() {
				DisplayName = "Destroyer",
				MaxPerFaction = 3,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 4,
				MaxBlocks = 1800,
				BlockTypeLimits = new int[2] { 4, 4 }
			};
			results[(int)HullClass.CLASS.CRUISER] = new HullRuleSet() {
				DisplayName = "Cruiser",
				MaxPerFaction = 2,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 5,
				MaxBlocks = 2700,
				BlockTypeLimits = new int[2] { 6, 6 }
			};
			results[(int)HullClass.CLASS.HEAVYCRUISER] = new HullRuleSet() {
				DisplayName = "Heavy Cruiser",
				MaxPerFaction = 1,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 6,
				MaxBlocks = 4050,
				BlockTypeLimits = new int[2] { 8, 6 }
			};
			results[(int)HullClass.CLASS.BATTLESHIP] = new HullRuleSet() {
				DisplayName = "Battleship",
				MaxPerFaction = 0,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 7,
				MaxBlocks = 6075,
				BlockTypeLimits = new int[2] { 10, 6 }
			};
			results[(int)HullClass.CLASS.OUTPOST] = new HullRuleSet() {
				DisplayName = "Outpost",
				MaxPerFaction = 5,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 2,
				MaxBlocks = 600,
				BlockTypeLimits = new int[2] { 2, 0 },
				ShouldBeStation = true
			};
			results[(int)HullClass.CLASS.INSTALLATION] = new HullRuleSet() {
				DisplayName = "Installation",
				MaxPerFaction = 2,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 4,
				MaxBlocks = 1800,
				BlockTypeLimits = new int[2] { 4, 0 },
				ShouldBeStation = true
			};
			results[(int)HullClass.CLASS.FORTRESS] = new HullRuleSet() {
				DisplayName = "Fortress",
				MaxPerFaction = 1,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 6,
				MaxBlocks = 2700,
				BlockTypeLimits = new int[2] { 6, 0 },
				ShouldBeStation = true
			};

			return results;
		}
			
		#endregion

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

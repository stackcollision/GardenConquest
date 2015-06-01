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

		private static readonly int DEFAULT_CP_PERIOD = 300; // 5 minutes
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
					Name = "CP Sajuuk",
					Position = new VRageMath.Vector3D(0, 0, 0),
					Radius = 15000,
					TokensPerPeriod = 5,
				},
				new ControlPoint() {
					Name = "CP Higaraa",
					Position = new VRageMath.Vector3D(100000, 0, 0),
					Radius = 15000,
					TokensPerPeriod = 2,
				},
				new ControlPoint() {
					Name = "CP Kadesh",
					Position = new VRageMath.Vector3D(-100000, 0, 0),
					Radius = 15000,
					TokensPerPeriod = 2,
				},
			};
		}

		private BlockType[] defaultBlockTypes() {
			return new BlockType[] {
				/*
				new BlockType() {
					DisplayName = "Conveyor Blocks",
					SubTypeStrings = new List<string>() { "Conveyor" }
				},
				new BlockType() {
					DisplayName = "Gravity Generators",
					SubTypeStrings = new List<string>() { "Gravity" }
				},
				 * */
				new BlockType() {
					DisplayName = "Industry",
					SubTypeStrings = new List<string>() {
						"Assembler",
						"Refinery",
						//"OxygenGenerator",
						//"OxygenFarm",
					}
				},
				/*
				new BlockType() {
					DisplayName = "Logic Blocks",
					SubTypeStrings = new List<string>() {
						"Programmable",
						"Sensor",
						"Timer"
					}
				},
				 * */
				new BlockType() {
					DisplayName = "Projectors",
					SubTypeStrings = new List<string>() { "Projector" }
				},
				new BlockType() {
					DisplayName = "Spotlights",
					SubTypeStrings = new List<string>() { "ReflectorLight"}
				},
				new BlockType() {
					DisplayName = "Solar Panels",
					SubTypeStrings = new List<string>() { "SolarPanel" }
				},
				new BlockType() {
					DisplayName = "Static Weapons",
					SubTypeStrings = new List<string>() { "MissileLauncher",
						"GatlingGun" }
				},
				/*
				new BlockType() {
					DisplayName = "Thrusters",
					SubTypeStrings = new List<string>() { "Thrust" }
				},
				 * */
				new BlockType() {
					DisplayName = "Tools",
					SubTypeStrings = new List<string>() {
						"Drill",
						"Grinder",
						"Welder"
					}
				},
				new BlockType() {
					DisplayName = "Turrets",
					SubTypeStrings = new List<string>() { "turret" }
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
				BlockTypeLimits = new int[7] { 
					//-1,  // Conveyors
					//0,   // Gravity
					0,     // Industrial
					//0,   // Logic
					0,     // Projector
					0,     // Spotlight
					0,     // Solar
					0,     // Static W
					//-1,  // Thrusters
					0,     // Tools
					0      // Turrets
				}
			};
			results[(int)HullClass.CLASS.UNLICENSED] = new HullRuleSet() {
				DisplayName = "Unlicensed",
				MaxPerFaction = 3,
				MaxPerSoloPlayer = 3,
				CaptureMultiplier = 1,
				MaxBlocks = 125,
				BlockTypeLimits = new int[7] {
					//10,  // Conveyors
					//1,   // Gravity
					3,     // Industrial
					//0,   // Logic
					0,     // Projector
					1,     // Spotlight
					0,     // Solar
					2,     // Static W
					//14,  // Thrusters
					1,     // Tools
					1      // Turrets
				}
			};
			results[(int)HullClass.CLASS.WORKER] = new HullRuleSet() {
				DisplayName = "Worker",
				MaxPerFaction = 8,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 1,
				MaxBlocks = 350,
				BlockTypeLimits = new int[7] {
					//30,  // Conveyors
					//0,   // Gravity
					0,     // Industrial
					//2,   // Logic
					1,     // Projector
					1,     // Spotlight
					0,     // Solar
					0,     // Static W
					//24,  // Thrusters
					3,     // Tools
					0      // Turrets
				}
			};
			results[(int)HullClass.CLASS.FOUNDRY] = new HullRuleSet() {
				DisplayName = "Foundry",
				MaxPerFaction = 1,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 3,
				MaxBlocks = 1000,
				BlockTypeLimits = new int[7] {
					//60,  // Conveyors
					//1,   // Gravity
					6,     // Industrial
					//4,   // Logic
					0,     // Projector
					1,     // Spotlight
					0,     // Solar
					0,     // Static W
					//12,  // Thrusters
					0,     // Tools
					2      // Turrets
				}
			};
			results[(int)HullClass.CLASS.SCOUT] = new HullRuleSet() {
				DisplayName = "Scout",
				MaxPerFaction = 5,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 1,
				MaxBlocks = 225,
				BlockTypeLimits = new int[7] {
					//20,  // Conveyors
					//0,   // Gravity
					0,     // Industrial
					//2,   // Logic
					0,     // Projector
					0,     // Spotlight
					0,     // Solar
					2,     // Static W
					//24,  // Thrusters
					0,     // Tools
					0      // Turrets
				}
			};
			results[(int)HullClass.CLASS.FIGHTER] = new HullRuleSet() {
				DisplayName = "Fighter",
				MaxPerFaction = 16,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 2,
				MaxBlocks = 550,
				BlockTypeLimits = new int[7] {
					//40,  // Conveyors
					//0,   // Gravity
					0,     // Industrial
					//0,   // Logic
					0,     // Projector
					0,     // Spotlight
					0,     // Solar
					4,     // Static W
					//24,  // Thrusters
					0,     // Tools
					0      // Turrets
				}
			};
			results[(int)HullClass.CLASS.GUNSHIP] = new HullRuleSet() {
				DisplayName = "Gunship",
				MaxPerFaction = 3,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 3,
				MaxBlocks = 1300,
				BlockTypeLimits = new int[7] {
					//60,  // Conveyors
					//0,   // Gravity
					0,     // Industrial
					//1,   // Logic
					0,     // Projector
					1,     // Spotlight
					0,     // Solar
					8,     // Static W
					//30,  // Thrusters
					0,     // Tools
					1      // Turrets
				}
			};
			results[(int)HullClass.CLASS.CORVETTE] = new HullRuleSet() {
				DisplayName = "Corvette",
				MaxPerFaction = 6,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 4,
				MaxBlocks = 200,
				BlockTypeLimits = new int[7] {
					//10,  // Conveyors
					//0,   // Gravity
					0,     // Industrial
					//0,   // Logic
					0,     // Projector
					0,     // Spotlight
					0,     // Solar
					2,     // Static W
					//24,  // Thrusters
					0,     // Tools
					2      // Turrets
				}
			};
			results[(int)HullClass.CLASS.FRIGATE] = new HullRuleSet() {
				DisplayName = "Frigate",
				MaxPerFaction = 4,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 6,
				MaxBlocks = 600,
				BlockTypeLimits = new int[7] {
					//40,  // Conveyors
					//1,   // Gravity
					0,     // Industrial
					//2,   // Logic
					0,     // Projector
					0,     // Spotlight
					0,     // Solar
					4,     // Static W
					//30,  // Thrusters
					0,     // Tools
					3      // Turrets
				}
			};

			results[(int)HullClass.CLASS.DESTROYER] = new HullRuleSet() {
				DisplayName = "Destroyer",
				MaxPerFaction = 2,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 10,
				MaxBlocks = 1800, 
				BlockTypeLimits = new int[7] {
					//60,  // Conveyors
					//2,   // Gravity
					0,     // Industrial
					//4,   // Logic
					0,     // Projector
					0,     // Spotlight
					0,     // Solar
					6,     // Static W
					//36,  // Thrusters
					0,     // Tools
					4      // Turrets
				}

			};
			results[(int)HullClass.CLASS.CRUISER] = new HullRuleSet() {
				DisplayName = "Cruiser",
				MaxPerFaction = 1,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 15,
				MaxBlocks = 2700,
				BlockTypeLimits = new int[7] {
					//80,  // Conveyors
					//4,   // Gravity
					0,     // Industrial
					//6,   // Logic
					0,     // Projector
					1,     // Spotlight
					0,     // Solar
					8,     // Static W
					//42,  // Thrusters
					0,     // Tools
					6      // Turrets
				}
			};
			results[(int)HullClass.CLASS.HEAVYCRUISER] = new HullRuleSet() {
				DisplayName = "Heavy Cruiser",
				MaxPerFaction = 0,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 20,
				MaxBlocks = 4050, 
				BlockTypeLimits = new int[7] {
					//80,  // Conveyors
					//4,   // Gravity
					0,     // Industrial
					//6,   // Logic
					0,     // Projector
					1,     // Spotlight
					0,     // Solar
					10,     // Static W
					//42,  // Thrusters
					0,     // Tools
					8      // Turrets
				}
			};
			results[(int)HullClass.CLASS.BATTLESHIP] = new HullRuleSet() {
				DisplayName = "Battleship",
				MaxPerFaction = 0,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 30,
				MaxBlocks = 6075,
				BlockTypeLimits = new int[7] {
					//80,  // Conveyors
					//4,   // Gravity
					0,     // Industrial
					//6,   // Logic
					0,     // Projector
					1,     // Spotlight
					0,     // Solar
					12,     // Static W
					//42,  // Thrusters
					0,     // Tools
					10      // Turrets
				}
			};
			results[(int)HullClass.CLASS.OUTPOST] = new HullRuleSet() {
				DisplayName = "Outpost",
				MaxPerFaction = 3,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 4,
				MaxBlocks = 600,
                BlockTypeLimits = new int[7] {
					//40,  // Conveyors
					//1,   // Gravity
					5,     // Industrial
					//2,   // Logic
					0,     // Projector
					0,     // Spotlight
					6,     // Solar
					0,     // Static W
					//2,  // Thrusters
					0,     // Tools
					2      // Turrets
				},
				ShouldBeStation = true
			};
			results[(int)HullClass.CLASS.INSTALLATION] = new HullRuleSet() {
				DisplayName = "Installation",
				MaxPerFaction = 2,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 8,
				MaxBlocks = 1800,
				BlockTypeLimits = new int[7] {
					//60,  // Conveyors
					//2,   // Gravity
					7,     // Industrial
					//4,   // Logic
					0,     // Projector
					0,     // Spotlight
					12,     // Solar
					0,     // Static W
					//2,  // Thrusters
					2,     // Tools
					4      // Turrets
				},
				ShouldBeStation = true
			};
			results[(int)HullClass.CLASS.FORTRESS] = new HullRuleSet() {
				DisplayName = "Fortress",
				MaxPerFaction = 1,
				MaxPerSoloPlayer = 0,
				CaptureMultiplier = 12,
				MaxBlocks = 2700,
				BlockTypeLimits = new int[7] {
					//80,  // Conveyors
					//3,   // Gravity
					10,     // Industrial
					//6,   // Logic
					0,     // Projector
					1,     // Spotlight
					18,     // Solar
					0,     // Static W
					//2,  // Thrusters
					4,     // Tools
					6      // Turrets
				},
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

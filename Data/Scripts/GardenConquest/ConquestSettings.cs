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

namespace GardenConquest {
	
	public class ConquestSettings {

		[XmlType("GardenConquestSettings")]
		private struct SETTINGS {
			public List<ControlPoint> ControlPoints { get; set; }
			public int Period { get; set; }
		}

		private static Logger s_Logger = null;
		private const String m_ConfigFileName = "GCConfig.xml";
		private SETTINGS m_Settings;

		public List<ControlPoint> ControlPoints { get { return m_Settings.ControlPoints; } }
		public int Period { get { return m_Settings.Period; } set { m_Settings.Period = value; } }
		//public HullRule[] HullRules { get; set; }

		private static ConquestSettings s_Instance = null;

		private ConquestSettings() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "ConquestSettings");
			log("Settings initialized", "ctor");

			m_Settings = new SETTINGS();
			m_Settings.ControlPoints = new List<ControlPoint>();
			//HullRules = new HullRule[9];
		}

		public bool loadSettings() {
			log("Attempting to load settings from file", "loadSettings");
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(m_ConfigFileName, typeof(SETTINGS))) {
				TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(
					m_ConfigFileName, typeof(SETTINGS));
				m_Settings =
					MyAPIGateway.Utilities.SerializeFromXML<SETTINGS>(reader.ReadToEnd());
				log("Config file successfully loaded", "loadSettings");
				return true;
			} else {
				log("No config file found", "loadSettings");
				return false;
			}
		}

		// Only for use when creating the original config
		public void writeSettings() {
			log("Writing config file", "writeSettings");
			TextWriter writer =
				MyAPIGateway.Utilities.WriteFileInLocalStorage(
				m_ConfigFileName, typeof(SETTINGS));
			writer.Write(MyAPIGateway.Utilities.SerializeToXML<SETTINGS>(m_Settings));
			writer.Flush();
			log("Config written", "writeSettings");
		}

		public void loadDefaults() {
			log("Loading default settings", "loadDefaults");

			ControlPoint cp = new ControlPoint();
			cp.Name = "Center";
			cp.Position = new VRageMath.Vector3D(0, 0, 0);
			cp.Radius = 15000;
			cp.TokensPerPeriod = 5;
			ControlPoints.Add(cp);

			// Default period 900 seconds (15 minutes)
			Period = 900;

			writeSettings();
		}

		public static ConquestSettings getInstance() {
			if (s_Instance == null)
				s_Instance = new ConquestSettings();
			return s_Instance;
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

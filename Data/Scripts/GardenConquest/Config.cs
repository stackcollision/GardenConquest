using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.IO;

using Sandbox.Common;
using Sandbox.Definitions;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Serializer;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;

namespace GardenConquest {

	public class Config {
	
		private const String m_ConfigFileName = "GCConfig.xml";
		private static Logger s_Logger = null;

		public ConquestSettings Settings { get; private set; }

		public Config() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Config");

			try {
				if (MyAPIGateway.Utilities.FileExistsInLocalStorage(m_ConfigFileName, typeof(Config))) {
					log("Found config file", "Config");

					// Slurp the file and deserialize
					TextReader reader =
						MyAPIGateway.Utilities.ReadFileInLocalStorage(m_ConfigFileName, typeof(Config));
					Settings = 
						MyAPIGateway.Utilities.SerializeFromXML<ConquestSettings>(reader.ReadToEnd());

					log("Loading settings from file.  Number of CPs: " 
						+ Settings.ControlPoints.Count, "Config");

				} else {
					log("No config file found.", "Config");
					loadDefaults();
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "Config", Logger.severity.ERROR);
			}

		}

		private void loadDefaults() {
			log("Loading default settings", "loadDefaults");

			Settings = new ConquestSettings();

			ControlPoint cp = new ControlPoint();
			cp.Name = "Center";
			cp.Position = new VRageMath.Vector3D(0, 0, 0);
			cp.Radius = 15000;
			cp.TokensPerPeriod = 5;
			Settings.ControlPoints.Add(cp);

			// Default period 900 seconds (15 minutes)
			Settings.Period = 900;

			writeXML();
		}

		// Only for use to create original config
		private void writeXML() {
			log("Writing config to XML", "writeXML");
			TextWriter textWriter = 
				MyAPIGateway.Utilities.WriteFileInLocalStorage(
				m_ConfigFileName, typeof(ConquestSettings));
			textWriter.Write(MyAPIGateway.Utilities.SerializeToXML<ConquestSettings>(Settings));
			textWriter.Flush();
			log("Config written", "writeXML");
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

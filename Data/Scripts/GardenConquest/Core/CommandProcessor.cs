using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Library.Utils;
using Interfaces = Sandbox.ModAPI.Interfaces;
using InGame = Sandbox.ModAPI.Ingame;

namespace GardenConquest.Core {

	/// <summary>
	/// Hooks into chat commands and sends requests to server.
	/// </summary>
	class CommandProcessor {

		private static Logger s_Logger = null;

		public CommandProcessor() {
			if(s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Command Processor");
		}

		public void initialize() {
			MyAPIGateway.Utilities.MessageEntered += handleChatCommand;
		}

		public void shutdown() {
			MyAPIGateway.Utilities.MessageEntered -= handleChatCommand;
		}

		public void handleChatCommand(string messageText, ref bool sendToOthers) {
			try {
				if (messageText[0] != '/')
					return;

				string[] cmd =
					messageText.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
				if (cmd[0].ToLower() != "/gc")
					return;

				int numCommands = cmd.Length - 1;
				if (numCommands == 1) {
					if (cmd[1].ToLower() == "about") {
						// TODO
					}
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "handleChatCommand", Logger.severity.ERROR);
			}
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

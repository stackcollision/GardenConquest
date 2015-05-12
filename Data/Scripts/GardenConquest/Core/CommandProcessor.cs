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

		// TODO: rest of this
		private static string s_AboutText =
			"Welcome to Garden Conquest\n\n" +
			"In GC each grid must have a Hull Classifier ... MORE";

		private static string s_HelpText =
			"Chat commands:\n\n" +
			"/gc about - Show basic mod info\n" +
			"/gc help - Show this screen\n" +
			"/gc help [category] - Show help for a category.  Categories are:\n" +
			"    limits - Information on grid class limitations\n";

		private static string s_HelpLimitsText =
			"";

		public CommandProcessor() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Command Processor");
			log("Started", "CommandProcessor");
		}

		public void initialize() {
			MyAPIGateway.Utilities.MessageEntered += handleChatCommand;
			log("Chat handler registered", "initialized");
		}

		public void shutdown() {
			MyAPIGateway.Utilities.MessageEntered -= handleChatCommand;
		}

		public void handleChatCommand(string messageText, ref bool sendToOthers) {
			try {
				if (messageText[0] != '/')
					return;

				log("Checking command", "handleChatCommand");

				string[] cmd =
					messageText.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
				if (cmd[0].ToLower() != "/gc")
					return;

				sendToOthers = false;
				int numCommands = cmd.Length - 1;
				if (numCommands == 1) {
					if (cmd[1].ToLower() == "about") {
						Utility.showDialog("About", s_AboutText, "Close");
					} else if (cmd[1].ToLower() == "help") {
						if (cmd.Length > 1) {
							if(cmd[2].ToLower() == "limits")
								Utility.showDialog("Help - ", s_HelpLimitsText, "Close");
						} else {
							Utility.showDialog("Help", s_HelpText, "Close");
						}
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

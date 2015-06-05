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

using GardenConquest.Messaging;

namespace GardenConquest.Core {

	/// <summary>
	/// Hooks into chat commands and sends requests to server.
	/// </summary>
	class CommandProcessor {

		private static Logger s_Logger = null;

		private static string s_HelpText =
			"Garden Conquest is a new, open source, Conquest-type mod for " +
			"Space Engineers that introduces area-control dynamics for " +
			"combat-focused survival servers.\n\n" +
			"If you're new to GC, check out the help subtopics below.\n\n" +
			"Chat Commands:\n" +
			"/gc help - Show this screen\n" +
			"/gc help [category] - Show help for a category.  Categories:\n" +
			"        classes     - Ship Classes and their limits\n" +
			"        classifiers - Hull Classifier blocks\n" +
			"        cps            - Control Points\n" +
			"        licenses    - Ship License components\n" +
			"/gc fleet - Information on your faction's fleet \n" +
			//"/gc fleet remove \"Ship Name\"- Disown a ship";
			"/gc violations - Your fleet's current rule violations, if any";

		private static string s_HelpClassText =
			"Classes:\n\n" +
			"'5 L' means the Classifier costs 5 Ship Licenses\n" +
			"'Small' and 'Large' below refer to Ship sizes.\n" +
			"Block, turret, etc counts are maximums.\n\n";

		private static string s_HelpClassifiersText =
			"Hull Classifiers are new blocks that each correspond to one " +
			"of the various classes.\n\n" +
			"Classifiers must be fully built and powered to designate your " +
			"ship as a member of its class.\n\n" +
			"Every Ship/Station must be classified, even the \"Unlicensed\" " +
			"ones. Unlicensed classifiers do not require Ship Licenses " +
			"to build, but they have relatively low block limits.\n\n" +
			"If a ship goes unclassified for 2 hours, it will become a " +
			"Derelict and slowly deteriorate until it either is reclassified " +
			"by a player or disappears entirely.\n\n" + 
			"For more info on Classes, type \"/gc help classes\"";

		private static string s_HelpCPsText1 =
			"Control Points are areas of the map you can hold and control. " +
			"Each CP has a Radius, Reward, and Round Time. On this server, " +
			"these are:\n\n";

		private static string s_HelpCPsText2 =
			"\n\nIn order to win a 15 minute round, you or your faction must " +
			"have the most ships in that area at the time that:\n" +
			"* have a powered, broadcasting Hull Classifier beacon\n" +
			"* have a broadcast range on the classifier at least as large " +
			" as the ship's distance to the CP. This is to prevent players " +
			"from hiding while capturing a CP. If someone stands in the " +
			"center of a CP, they will see broadcasts from every " +
			"Conquesting ship.\n\n" +
			"Bigger ships count more towards your total. In the event of a " +
			"tie, no one wins.\n\n" +
			"The winner of a round will receive the Licenses directly into " +
			"the first open inventory in their largest ship. If they have no " +
			"open inventory, they won't receive the Licenses.";

		private static string s_HelpLicensesText =
			"Ship Licences are a new type of building component introduced by " +
			"Garden Conquest. You can acquire Ship Licences by holding " +
			"Control Points. \n\n" +
			"You use Ship Licenses to build Hull Classifier " +
			"blocks, which determine the class of your Ship/Station.\n\n" +
			"For more info, see:\n" +
			"    /gc help classifiers\n" +
			"    /gc help cps\n";

		private static string s_FleetText1 =
			"Your faction currently controls the follow ships:\n\n";

		private ResponseProcessor m_MailMan;

		public CommandProcessor(ResponseProcessor mailMan) {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Command Processor");
			log("Started", "CommandProcessor");
			m_MailMan = mailMan;
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

				string[] cmd =
					messageText.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);

				log("Handling commands " + String.Join(" , ", cmd), "handleChatCommand");
				if (cmd[0].ToLower() != "/gc")
					return;

				sendToOthers = false;
				int numCommands = cmd.Length - 1;
				log("numCommands " + numCommands, "handleChatCommand");
				if (numCommands > 0) {
					switch (cmd[1].ToLower()) {
						case "about":
						case "help":
							if (numCommands == 1)
								Utility.showDialog("Help", s_HelpText, "Close");
							else
							{
								switch (cmd[2].ToLower())
								{
									case "classes":
										Utility.showDialog("Help - Classes", s_HelpClassesText(), "Close");
										break;
									case "classifiers":
										Utility.showDialog("Help - Classifiers", s_HelpClassifiersText, "Close");
										break;
									case "cps":
										Utility.showDialog("Help - Control Points", s_HelpCPsText(), "Close");
										break;
									case "licenses":
										Utility.showDialog("Help - Licenses", s_HelpLicensesText, "Close");
										break;
								}
							}
							break;

						case "fleet":
							if (numCommands == 1) {
								m_MailMan.requestFleet();
							} else {
								switch (cmd[2].ToLower()) {
									case "disown":
										String entityID = "";
										if (numCommands > 2)
											entityID = cmd[3];
										//m_MailMan.requestDisown(cmd[3]);
										break;
								}
							}
							break;

						case "violations":
							m_MailMan.requestViolations();
							break;

						case "admin":
							// admin fleet listing
							break;
					}
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "handleChatCommand", Logger.severity.ERROR);
			}
		}

		// todo - fill this with info from the Server's config
		private String s_HelpClassesText() {
			String result = s_HelpClassText;

			//foreach class group
				// result += symbol (or letter eventually), group name
				// i.e.  ● Unclassified
				// foreach class
					// += symbol, tier, name, license cost, max per faction
					// i.e. ▲ III Gunship - 55 L, 3 per faction
					// += grid size, block max
					// i.e. Large or Station, 500 blocks
						// foreach block limit
						// += num, category
						// i.e. 1 turret
						// 2 static weapons
						// 3 production

			return result;
		}

		// todo - fill this with info from the Server's config
		private String s_HelpCPsText() {
			String result = s_HelpCPsText1;

			//foreach CP setting add a descriptive line
			// i.e. result += Name - location - radius - reward - round time

			return result + s_HelpCPsText2;
		}

		// todo - fill this with info from the Server's fleet list
		private String s_FleetText() {
			String result = s_FleetText1;

			//foreach class group
				// result += symbol (or letter eventually), group name
				// i.e.  ● Unclassified
				// foreach class
					// += symbol, tier, name, current count, max
					// i.e. ▲ III Gunship - 2/10

			return result;
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

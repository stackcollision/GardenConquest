using System;
using System.Collections.Generic;
using System.Xml.Serialization;

using Sandbox.ModAPI;

using GardenConquest.Extensions;

namespace GardenConquest.Records {

	/// <summary>
	/// A named group of partial Block SubType names,
	/// loaded from Config at runtime
	/// Mapped to HullRuleSets via their BlockTypeLimits
	/// </summary>
	[XmlType("BlockType")]
	public class BlockType {

		private static Logger s_Logger;

		/// <summary>
		/// Display Name is used in Notifications and Dialogs
		/// </summary>
		[XmlElement("DisplayName")]
		public String DisplayName { get; set; }

		/// <summary>
		/// Partial SubType names to match against
		/// </summary>
		[XmlArray("SubTypes")]
		[XmlArrayItem("PartialName")]
		public List<string> SubTypeStrings { get; set; }

		private void init() {
			if (s_Logger == null)
				s_Logger = new Logger("Static", "BlockTypeGroup");
		}

		/// <summary>
		/// Does the passed block belong to this group?
		/// </summary>
		public bool appliesToBlock(IMySlimBlock block) {

			// IMySlimBlock.ToString() does:
			// FatBlock != null ? FatBlock.ToString() : BlockDefinition.DisplayNameText.ToString();
			// which is nice since we're not allowed access to BlockDefinition of a SlimBlock
			String blockString = block.ToString().ToLower();

			log("Does " + blockString + " belong in group " + SubTypeStrings.ToString() + " ? ",
				"appliedToBlock", Logger.severity.TRACE);

			foreach (String subType in SubTypeStrings) {
				if (blockString.Contains(subType.ToLower())) {
					log("It does!", "appliedToBlock", Logger.severity.TRACE);
					return true;
				}
			}
			log("It doesn't", "appliedToBlock", Logger.severity.TRACE);
			return false;
		}

		public void serialize(VRage.ByteStream stream) {
			stream.addString(DisplayName);

			stream.addUShort((ushort)SubTypeStrings.Count);
			foreach (String subTypeString in SubTypeStrings) {
				stream.addString(subTypeString);
			}
		}

		public static BlockType deserialize(VRage.ByteStream stream) {
			BlockType result = new BlockType();

			result.DisplayName = stream.getString();

			ushort subTypeStringsCount = stream.getUShort();
			result.SubTypeStrings = new List<string>();
			for (ushort i = 0; i < subTypeStringsCount; ++i) {
				result.SubTypeStrings.Add(stream.getString());
			}

			return result;
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

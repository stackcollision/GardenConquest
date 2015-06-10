using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

using GardenConquest.Extensions;

namespace GardenConquest.Records {

	/// <summary>
	/// Holds the rules for a specific Class
	/// </summary>
	[XmlType("Class")]
	public class HullRuleSet {

		// We include XML descriptors here, even though the names are the same,
		// to help maintain backwards compatibility with saved settings

		/// <summary>
		/// Display name helps Admins know which class they're editing,
		/// but is also used to represent the class in Notifications and Dialogs
		/// </summary>
		[XmlElement("DisplayName")]
		public string DisplayName { get; set; }
		[XmlElement("MaxPerFaction")]
		public int MaxPerFaction { get; set; }
		[XmlElement("MaxPerSoloPlayer")]
		public int MaxPerSoloPlayer { get; set; }
		[XmlElement("CaptureMultiplier")]
		public int CaptureMultiplier { get; set; }
		[XmlElement("MaxBlocks")]
		public int MaxBlocks { get; set; }
		[XmlArray("BlockTypeLimits")]
		[XmlArrayItem("Limit")]
		public int[] BlockTypeLimits { get; set; }

		public void serialize(VRage.ByteStream stream) {
			stream.addString(DisplayName);
			stream.addUShort((ushort)MaxPerFaction);
			stream.addUShort((ushort)MaxPerSoloPlayer);
			stream.addUShort((ushort)CaptureMultiplier);
			stream.addLong(MaxBlocks);

			stream.addUShort((ushort)BlockTypeLimits.Length);
			foreach (int limit in BlockTypeLimits) {
				stream.addUShort((ushort)limit);
			}

		}

		public static HullRuleSet deserialize(VRage.ByteStream stream) {
			HullRuleSet result = new HullRuleSet();
			result.DisplayName = stream.getString();
			result.MaxPerFaction = stream.getUShort();
			result.MaxPerSoloPlayer = stream.getUShort();
			result.CaptureMultiplier = stream.getUShort();
			result.MaxBlocks = (int)stream.getLong();

			ushort blockTypeLimitsCount = stream.getUShort();
			result.BlockTypeLimits = new int[blockTypeLimitsCount];
			for (ushort i = 0; i < blockTypeLimitsCount; ++i) {
				result.BlockTypeLimits[i] = stream.getUShort();
			}

			return result;
		}
	}
}

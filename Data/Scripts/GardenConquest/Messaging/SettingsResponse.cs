using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GardenConquest.Extensions;
using GardenConquest.Core;

namespace GardenConquest.Messaging {

	/// <summary>
	/// Contains a list of all GPS coordinates for CPs.
	/// </summary>
	public class SettingsResponse : BaseResponse {

		public ConquestSettings.SETTINGS Settings;

		private const int BaseSize = HeaderSize;
		private static Logger s_Logger;

		public SettingsResponse()
			: base(BaseResponse.TYPE.SETTINGS) {
		}

		public override byte[] serialize() {
			VRage.ByteStream bs = new VRage.ByteStream(BaseSize, true);

			byte[] bmessage = base.serialize();
			bs.Write(bmessage, 0, bmessage.Length);

			// Control Points
			bs.addUShort((ushort)Settings.ControlPoints.Count);
			foreach (Records.ControlPoint cp in Settings.ControlPoints) {
				cp.serialize(bs);
			}

			// CP Period
			bs.addLong(Settings.CPPeriod);

			// Cleanup Period
			bs.addLong(Settings.CleanupPeriod);

			// Block Types
			bs.addUShort((ushort)Settings.BlockTypes.Length);
			foreach (Records.BlockType bt in Settings.BlockTypes) {
				bt.serialize(bs);
			}

			// Hull Rules
			bs.addUShort((ushort)Settings.HullRules.Length);
			foreach (Records.HullRuleSet hrs in Settings.HullRules) {
				hrs.serialize(bs);
			}

			return bs.Data;
		}

		public override void deserialize(VRage.ByteStream stream) {
			base.deserialize(stream);

			Settings = new ConquestSettings.SETTINGS();

			// Control Points
			ushort cpCount = stream.getUShort();
			Settings.ControlPoints = new List<Records.ControlPoint>();
			for (ushort i = 0; i < cpCount; ++i) {
				Settings.ControlPoints.Add(
					Records.ControlPoint.deserialize(stream)
				);
			}

			// CP Period
			Settings.CPPeriod = (int)stream.getLong();

			// Cleanup Period
			Settings.CleanupPeriod = (int)stream.getLong();

			// Block Types
			ushort blockTypesLength = stream.getUShort();
			Settings.BlockTypes = new Records.BlockType[blockTypesLength];
			for (ushort i = 0; i < blockTypesLength; ++i) {
				Settings.BlockTypes[i] = Records.BlockType.deserialize(stream);
			}

			// Hull Rules
			ushort hullRulesLength = stream.getUShort();
			Settings.HullRules = new Records.HullRuleSet[hullRulesLength];
			for (ushort i = 0; i < hullRulesLength; ++i) {
				Settings.HullRules[i] = Records.HullRuleSet.deserialize(stream);
			}
		}

		private static void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger == null)
				s_Logger = new Logger("Static", "SettingsResponse");

			s_Logger.log(level, method, message);
		}
	}
}

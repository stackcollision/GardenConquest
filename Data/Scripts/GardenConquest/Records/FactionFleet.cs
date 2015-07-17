using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using GardenConquest.Blocks;
using GardenConquest.Extensions;
using GardenConquest.Core;

namespace GardenConquest.Records {

	/// <summary>
	/// Records the current classified fleet for a faction
	/// </summary>
	public class FactionFleet {
		private long m_FactionId;
		private uint[] m_Counts = null;
		private uint[] m_Maximums = null;
		private uint m_TotalCount;
		private GridOwner.OWNER_TYPE m_OwnerType = GridOwner.OWNER_TYPE.UNOWNED;

		[XmlIgnore]
		private Dictionary<long, GridEnforcer>[] m_SupportedGrids;
		[XmlIgnore]
		private Dictionary<long, GridEnforcer>[] m_UnsupportedGrids;

		private HullRuleSet[] s_Rules;
		private static Logger s_Logger = null;

		public uint TotalCount { get { return m_TotalCount; } }

		public FactionFleet(long facId, GridOwner.OWNER_TYPE ownerType) {
			//if (s_Settings == null) {
			//    s_Settings = ConquestSettings.getInstance();
			//}
			if (s_Rules == null) {
				s_Rules = ConquestSettings.getInstance().HullRules;
			}
			if (s_Logger == null) {
				s_Logger = new Logger("Static", "FactionFleet");
			}
			log("start", "ctr", Logger.severity.TRACE);

			m_FactionId = facId;
			m_OwnerType = ownerType;

			// = init count holders
			m_TotalCount = 0;

			int classCount = Enum.GetValues(typeof(HullClass.CLASS)).Length;
			m_Counts = new uint[classCount];

			m_SupportedGrids = new Dictionary<long, GridEnforcer>[classCount];
			for (int i = 0; i < classCount; i++) {
				m_SupportedGrids[i] = new Dictionary<long, GridEnforcer>();
			}

			m_UnsupportedGrids = new Dictionary<long, GridEnforcer>[classCount];
			for (int i = 0; i < classCount; i++) {
				m_UnsupportedGrids[i] = new Dictionary<long, GridEnforcer>();
			}

			m_Maximums = new uint[classCount];
			if (ownerType == GridOwner.OWNER_TYPE.FACTION) {
				for (int i = 0; i < classCount; i++) {
					m_Maximums[i] = (uint)s_Rules[i].MaxPerFaction;
				}
			} else if (ownerType == GridOwner.OWNER_TYPE.PLAYER) {
				for (int i = 0; i < classCount; i++) {
					m_Maximums[i] = (uint)s_Rules[i].MaxPerSoloPlayer;
				}
			} else {
				for (int i = 0; i < classCount; i++) {
					m_Maximums[i] = 0;
				}
			}

		}

		/// <summary>
		/// Increments the class count for a given class
		/// </summary>
		/// <param name="c">Class to increment</param>
		public void add(HullClass.CLASS c, GridEnforcer ge) {
			log("adding class " + ge.Class, "add", Logger.severity.TRACE);

			int classID = (int)ge.Class;
			//log("m_Counts[classID] is " + m_Counts[classID], "add", Logger.severity.TRACE);

			updateSupportAdded(ge);
			m_Counts[classID] += 1;
			m_TotalCount++;
			//log("m_Counts[classID] is " + m_Counts[classID], "add", Logger.severity.TRACE);

			//debugPrint("add");
		}

		/// <summary>
		/// Decrements the class count for a given class
		/// </summary>
		/// <param name="c">Class to decrement</param>
		public void remove(HullClass.CLASS c, GridEnforcer ge) {
			int classID = (int)c;
			if (m_Counts[classID] > 0) {
				m_Counts[classID] -= 1;
				m_TotalCount--;
			} else {
				log("Error: Decrementing class " + classID + " count, but already 0",
					"removeClass", Logger.severity.ERROR);
			}

			updateSupportRemoved(classID, ge);
			//debugPrint("remove");
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="c">Class to check</param>
		/// <returns>Count of c</returns>
		public uint countClass(HullClass.CLASS c) {
			return m_Counts[(int)c];
		}

		public uint maxClass(HullClass.CLASS c) {
			return m_Maximums[(int)c];
		}

		public bool canSupportAnother(HullClass.CLASS c) {
			return (countClass(c) < maxClass(c)) || (maxClass(c) < 0);
		}

		/// <summary>
		/// Determines whether this fleet is allowed to support this class
		/// Alerts the grid and updates tracking
		/// Returns true if it was supported
		/// </summary>
		private bool updateSupportAdded(GridEnforcer ge) {
			HullClass.CLASS hc = ge.Class;
			uint c = (uint)hc;
			long eID = ge.Container.Entity.EntityId;
			//log("adding " + eID + " as " + c, "updateSupportAdded");

			// if we have enough room, support it
			if (canSupportAnother(hc)) {
				log("we have enough room, supporting", "updateSupportAdded");
				m_SupportedGrids[c][eID] = ge;
				ge.markSupported(m_FactionId);
				return true;
			}

			// if we don't, see if it's bigger than one of the supported ones
			foreach (KeyValuePair<long, GridEnforcer> pair in m_SupportedGrids[c]) {
				GridEnforcer supported = pair.Value;

				// it is!
				if (ge.BlockCount > supported.BlockCount) {
					log("it's larger than one of our supported, supporting", "updateSupportAdded");

					// remove support from the old supported one
					log("removing support from " + pair.Key, "updateSupportAdded");
					m_SupportedGrids[c].Remove(pair.Key);
					m_UnsupportedGrids[c][pair.Key] = supported;
					supported.markUnsupported(m_FactionId);

					// add support to the new
					log("supporting " + eID, "updateSupportAdded");
					m_SupportedGrids[c][eID] = ge;
					ge.markSupported(m_FactionId);

					return true;
				}

			}

			// if not, mark as unsupported
			log("can't support, marking grid as unsupported", "updateSupportAdded");
			m_UnsupportedGrids[c][eID] = ge;
			ge.markUnsupported(m_FactionId);
			return false;
		}

		/// <summary>
		/// Removes support from a grid given the class it was stored with
		/// </summary>
		private void updateSupportRemoved(int classID, GridEnforcer ge) {
			//log("start", "updateSupportRemoved", Logger.severity.TRACE);

			uint c = (uint)classID;
			long eID = ge.Container.Entity.EntityId;
			//log("checking where to remove it from", "updateSupportRemoved", Logger.severity.TRACE);
			if (m_SupportedGrids[c].ContainsKey(eID)) {
				m_SupportedGrids[c].Remove(eID);
				log(String.Format("Removing {0} from supported grids, count now {1}", 
					eID, m_SupportedGrids[c].Count),
					"updateSupportRemoved", Logger.severity.TRACE);

				// See if there's an unsupported grid. If there's more than 1, select the
				// grid with the highest block count
				if (m_UnsupportedGrids[c].Count > 0) {
					int highestBlockCount = 0;
					long highestBlockCountID = 0;
					foreach (KeyValuePair<long, GridEnforcer> pair in m_UnsupportedGrids[c]) {
						long gridID = pair.Key;
						GridEnforcer grid = pair.Value;
						if (grid.BlockCount > highestBlockCount) {
							highestBlockCount = grid.BlockCount;
							highestBlockCountID = grid.Container.Entity.EntityId;
						}
					}
					m_SupportedGrids[c][highestBlockCountID] = m_UnsupportedGrids[c][highestBlockCountID];
					m_SupportedGrids[c][highestBlockCountID].markSupported(m_FactionId);
					m_UnsupportedGrids[c].Remove(highestBlockCountID);
				}
			}
			else if (m_UnsupportedGrids[c].ContainsKey(eID)) {
				m_UnsupportedGrids[c].Remove(eID);
				log(String.Format("Removing {0} from unsupported grids, count now {1}",
					eID, m_UnsupportedGrids[c].Count),
					"updateSupportRemoved", Logger.severity.TRACE);
			}

		}

		/// <summary>
		/// Returns true if the support has changed
		/// </summary>
		/// <remarks>
		/// We should eventually call this every so often after adding blocks,
		/// perhaps before a GE Violations check, since competition for support is
		/// dependent on block count. Right now it's not called at all.
		/// </remarks>
		/// <param name="ge"></param>
		/// <returns></returns>
		public bool updateSupport(GridEnforcer ge) {
			uint c = (uint)ge.Class;
			long eID = ge.Container.Entity.EntityId;
			Dictionary<long, GridEnforcer> supportedGrids = m_SupportedGrids[c];
			Dictionary<long, GridEnforcer> unsupportedGrids = m_UnsupportedGrids[c];

			// if it's a supported
			if (supportedGrids.ContainsKey(eID)) {

				// if we're out of room and there's unsupported grids
				if ((m_Counts[c] > m_Maximums[c]) && unsupportedGrids.Count > 0) {

					// for any of the unsupported
					foreach (long unsupportedID in unsupportedGrids.Keys) {
						GridEnforcer unsupported = unsupportedGrids[unsupportedID];

						// see it this is smaller
						if (ge.BlockCount < unsupported.BlockCount) {

							// change unsupported to supported
							unsupportedGrids.Remove(unsupportedID);
							supportedGrids[unsupportedID] = unsupported;
							unsupported.markSupported(m_FactionId);

							// change this to unsupported
							supportedGrids.Remove(eID);
							unsupportedGrids[eID] = ge;
							ge.markUnsupported(m_FactionId);

							return true;
						}
					}

				// if it's a unsupported
				} else if (unsupportedGrids.ContainsKey(eID)) {

					// if we have enough room, support it
					if (m_Counts[c] < m_Maximums[c]) {
						unsupportedGrids.Remove(eID);
						supportedGrids[eID] = ge;
						ge.markSupported(m_FactionId);
						return true;
					}

					// for any of the unsupported
					foreach (long supportedID in supportedGrids.Keys) {
						GridEnforcer supported = supportedGrids[supportedID];

						// see if it's bigger
						if (ge.BlockCount > supported.BlockCount) {

							// remove support from the old
							supportedGrids.Remove(supportedID);
							unsupportedGrids[supportedID] = supported;
							supported.markUnsupported(m_FactionId);

							// add support to the existing
							unsupportedGrids.Remove(eID);
							supportedGrids[eID] = ge;
							ge.markSupported(m_FactionId);

							return true;
						}
					}
				}
			}

			return false;
		}

		private void debugPrint(String callingFunc = "") {
			log("Fleet " + m_FactionId + " count(sup+unsup)/max : ", callingFunc);
			for (int i = 0; i < m_Counts.Length; i++) {
				if (m_Counts[i] > 0) {
					log("  Class " + (HullClass.CLASS)i + " - " + m_Counts[i] + " / " + m_Maximums[i], callingFunc);
						if (m_SupportedGrids[i].Count > 0) {
							log("  Supported: ", callingFunc);

							if (m_SupportedGrids[i] == null) {
								log("  Supported grid entry for " + (HullClass.CLASS)i + " was null!",
									callingFunc, Logger.severity.ERROR);
							}
							else {
								foreach (KeyValuePair<long, GridEnforcer> entry in m_SupportedGrids[i]) {
									try {
										log("    " + entry.Key + " - " + entry.Value.Grid.DisplayName, callingFunc);
									}
									catch (Exception e) {
										log("Error: " + e, "debugPrint", Logger.severity.ERROR);
									}
								}
							}
						}

						if (m_UnsupportedGrids[i].Count > 0) {
							log("  Unsupported: ", callingFunc);
							foreach (KeyValuePair<long, GridEnforcer> entry in m_UnsupportedGrids[i]) {
								try {
									log("    " + entry.Key + " - " + entry.Value.Grid.DisplayName, callingFunc);
								} catch (Exception e) {
									log("Error: " + e, "debugPrint", Logger.severity.ERROR);
								}
							}
						}

				}
			}
		}

		public String classesToString() {
			String result = "";
			GridEnforcer ge;

			for (int i = 0; i < m_Counts.Length; i++) {
				if (m_Counts[i] > 0) {
					result += (HullClass.CLASS)i + ": " + m_Counts[i] + " / " + m_Maximums[i] + "\n";
					if (m_SupportedGrids[i].Count > 0) {
						foreach (KeyValuePair<long, GridEnforcer> entry in m_SupportedGrids[i]) {
							ge = entry.Value;
							result += "  * " + ge.Grid.DisplayName + " - " + ge.BlockCount + " blocks\n";
						}
					}
					if (m_UnsupportedGrids[i].Count > 0) {
						result += "\n  Unsupported:\n";
						foreach (KeyValuePair<long, GridEnforcer> entry in m_UnsupportedGrids[i]) {
							ge = entry.Value;
							result += "     * " + ge.Grid.DisplayName + " - " + ge.BlockCount + " blocks\n";
						}
					}
					result += "\n";
				}
			}
			return result;
		}

		public void serialize(VRage.ByteStream stream) {
			stream.addUShort((ushort)TotalCount);
			for (int i = 0; i < m_Counts.Length; ++i) {
				if (m_Counts[i] > 0) {
					if (m_SupportedGrids[i].Count > 0) {
						foreach (KeyValuePair<long, GridEnforcer> entry in m_SupportedGrids[i])
							entry.Value.serialize(stream);
					}
					if (m_UnsupportedGrids[i].Count > 0) {
						foreach (KeyValuePair<long, GridEnforcer> entry in m_UnsupportedGrids[i])
							entry.Value.serialize(stream);
					}
				}
			}
		}

		public static List<GridEnforcer.GridData> deserialize(VRage.ByteStream stream) {
			List<GridEnforcer.GridData> result = new List<GridEnforcer.GridData>();

			ushort count = stream.getUShort();

			for (int i = 0; i < count; ++i) {
				GridEnforcer.GridData incomingData = GridEnforcer.deserialize(stream);
				result.Add(incomingData);
			}
			return result;
		}

		public String violationsToString() {
			log("", "violationsToString");
			String results = "";
			String classResults = "";
			String supportedResults = "";
			String unsupportedResults = "";
			GridEnforcer ge;
			List<GridEnforcer.VIOLATION> violations;

			for (int i = 0; i < m_Counts.Length; i++) {
				if (m_Counts[i] > 0) {
					classResults = "";

					// supported
					if (m_SupportedGrids[i].Count > 0) {
						supportedResults = "";

						foreach (KeyValuePair<long, GridEnforcer> entry in m_SupportedGrids[i]) {
							ge = entry.Value;
							violations = ge.Violations;

							if (violations.Count > 0) {
								supportedResults += "  * " + ge.Grid.DisplayName + " - Cleanup in " +
									Utility.prettySeconds(ge.TimeUntilCleanup) + " for violating:\n";

								//log("supportedResults +=" + supportedResults, "violationsToString");
								foreach (GridEnforcer.VIOLATION v in violations) {
									if (v.Type == GridEnforcer.VIOLATION_TYPE.SHOULD_BE_STATIC) {
										supportedResults += "        " + v.Name + "\n";
									} else {
										supportedResults += "        " + v.Name + ": " + v.Count + "/" + v.Limit + "\n";
									}
								}

							}
						}

						if (supportedResults != "") {
							//log("classResults +=" + supportedResults, "violationsToString");
							classResults += supportedResults;
						}
					}

					// unsupported
					if (m_UnsupportedGrids[i].Count > 0) {
						unsupportedResults = "";

						foreach (KeyValuePair<long, GridEnforcer> entry in m_UnsupportedGrids[i]) {
							ge = entry.Value;
							violations = ge.Violations;

							if (violations.Count > 0) {
								unsupportedResults += "  * " + ge.Grid.DisplayName + " - Cleanup in " +
									Utility.prettySeconds(ge.TimeUntilCleanup) + " for violating:\n";
									//log("unsupportedResults +=" + unsupportedResults, "violationsToString");
								foreach (GridEnforcer.VIOLATION v in violations) {
									unsupportedResults += "        " + v.Name + ": " + v.Count + "/" + v.Limit + "\n";
									//log("unsupportedResults +=" + unsupportedResults, "violationsToString");
								}
							}
						}

						if (unsupportedResults != "") {
							//log("classResults +=" + unsupportedResults, "violationsToString");
							classResults += "Unsupported:\n" + unsupportedResults;
						}
					}

					if (classResults != "") {
						results += (HullClass.CLASS)i + ": " + m_Counts[i] + " / " + m_Maximums[i] + "\n" + classResults;
					}
				}
			}

			return results;
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

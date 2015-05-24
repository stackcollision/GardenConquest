using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using GardenConquest.Blocks;
using GardenConquest.Core;

namespace GardenConquest.Records {

	/// <summary>
	/// Records the current classified fleet for a faction
	/// </summary>
	public class FactionFleet {
		private long m_FactionId;
		private uint[] m_Counts = null;
		private uint[] m_Maximums = null;
		private GridOwner.OWNER_TYPE m_OwnerType = GridOwner.OWNER_TYPE.UNOWNED;

		[XmlIgnore]
		private Dictionary<long, GridEnforcer>[] m_SupportedGrids;
		[XmlIgnore]
		private Dictionary<long, GridEnforcer>[] m_UnsupportedGrids;


		private HullRuleSet[] s_Rules;
		private static Logger s_Logger = null;

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
			log("adding ge of class" + ge.Class, "add", Logger.severity.TRACE);

			int classID = (int)ge.Class;
			log("m_Counts[classID] is " + m_Counts[classID], "add", Logger.severity.TRACE);

			updateSupportAdded(ge);
			m_Counts[classID] += 1;
			log("m_Counts[classID] is " + m_Counts[classID], "add", Logger.severity.TRACE);

			debugPrint("add");
		}

		/// <summary>
		/// Decrements the class count for a given class
		/// </summary>
		/// <param name="c">Class to decrement</param>
		public void remove(HullClass.CLASS c, GridEnforcer ge) {
			int classID = (int)c;
			if (m_Counts[classID] > 0)
				m_Counts[classID] -= 1;
			else {
				// This happens by default when we're creating a new fleets ? 
				log("Class " + classID + " is already 0", "removeClass", Logger.severity.ERROR);
			}

			updateSupportRemoved(classID, ge);
			debugPrint("remove");
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

		/// <summary>
		/// Determines whether this fleet is allowed to support this class
		/// Alerts the grid and updates tracking
		/// Returns true if it was supported
		/// </summary>
		private bool updateSupportAdded(GridEnforcer ge) {
			uint c = (uint)ge.Class;
			long eID = ge.Entity.EntityId;
			log("adding " + eID + " as " + c, "updateSupportAdded");

			// if we have enough room, support it
			if (m_Counts[c] < m_Maximums[c]) {
				log("we have enough room, supporting", "updateSupportAdded");
				m_SupportedGrids[c][eID] = ge;
				ge.markSupported(m_FactionId);
				return true;
			}

			// if we don't, see if it's bigger than one of the supported ones
			foreach (long supportedID in m_SupportedGrids[c].Keys) {
				GridEnforcer supported = m_SupportedGrids[c][supportedID];

				// it is!
				if (ge.BlockCount > supported.BlockCount) {
					log("it's larger than one of our supporting, supporting", "updateSupportAdded");
					m_SupportedGrids[c].Remove(supportedID);
					m_UnsupportedGrids[c][supportedID] = supported;
					supported.markUnsupported(m_FactionId);

					// add support to the new
					log("supporting", "updateSupportAdded");
					m_SupportedGrids[c][eID] = ge;
					ge.markSupported(m_FactionId);

					return true;
				}

			}

			// if not, mark as unsupported
			log("can't support", "updateSupportAdded");
			m_UnsupportedGrids[c][eID] = ge;
			ge.markUnsupported(m_FactionId);
			return false;
		}

		/// <summary>
		/// Removes support from a grid given the class it was stored with
		/// </summary>
		private void updateSupportRemoved(int classID, GridEnforcer ge) {
			log("start", "updateSupportRemoved", Logger.severity.TRACE);

			uint c = (uint)classID;
			long eID = ge.Entity.EntityId;
			log("checking where to remove it from", "updateSupportRemoved", Logger.severity.TRACE);
			if (m_SupportedGrids[c].ContainsKey(eID)) {
				m_SupportedGrids[c].Remove(eID);
				log("supportedGrids.count " + m_SupportedGrids[c].Count, 
					"updateSupportRemoved", Logger.severity.TRACE);
			}
			else if (m_UnsupportedGrids[c].ContainsKey(eID)) {
				m_UnsupportedGrids[c].Remove(eID);
				log("unsupportedGrids.count " + m_UnsupportedGrids[c].Count, 
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
			long eID = ge.Entity.EntityId;
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

					try {
						if (m_SupportedGrids[i].Count > 0) {
							log("  Supported: ", callingFunc);

							foreach (KeyValuePair<long, GridEnforcer> entry in m_SupportedGrids[i]) {
								if (entry.Value == null)
									log("    " + entry.Key + " - null", callingFunc);
								else
									log("    " + entry.Key + " - " + entry.Value.Grid.DisplayName, callingFunc);
							}
						}
						if (m_UnsupportedGrids[i].Count > 0) {
							log("  Unsupported: ", callingFunc);
							foreach (KeyValuePair<long, GridEnforcer> entry in m_UnsupportedGrids[i]) {
								if (entry.Value == null)
									log("    " + entry.Key + " - null", callingFunc);
								else
									log("    " + entry.Key + " - " + entry.Value.Grid.DisplayName, callingFunc);
							}
						}

					}
					catch (Exception e) {
						log("Error: " + e, "debugPrint", Logger.severity.ERROR);
					}

				}
			}
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (s_Logger != null)
				s_Logger.log(level, method, message);
		}
	}
}

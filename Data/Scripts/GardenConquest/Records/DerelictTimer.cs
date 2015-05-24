using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Library.Utils;

using GardenConquest.Core;

namespace GardenConquest.Records {

	/// <summary>
	/// Keeps track of when a grid should have a dereliction phase run
	/// </summary>
	public class DerelictTimer {

		public enum COMPLETION {
			ELAPSED,
			CANCELLED
		}

		[XmlType("ActiveDerelictTimer")]
		public class DT_INFO {
			/// <summary>
			/// What stage of dereliction is this grid in?
			/// </summary>
			public enum PHASE {
				NONE,
				INITIAL // The grid is not yet a derelict but is counting down to become one
			}

			public long GridID;
			public int MillisRemaining;
			public PHASE Phase;

			[XmlIgnore]
			public DateTime StartTime;
			[XmlIgnore]
			public int StartingMillisRemaining;
		}

		private DT_INFO m_TimerInfo = null;
		private MyTimer m_Timer = null;
		private IMyCubeGrid m_Grid = null;

		public bool TimerExpired { get; private set; }
		public DT_INFO.PHASE CompletedPhase { get; private set; }

		private Logger m_Logger = null;

		public long ID { get { return m_TimerInfo.GridID; } }

		public DerelictTimer(IMyCubeGrid grid) {
			m_Grid = grid;

			m_TimerInfo = null;
			m_Timer = null;

			TimerExpired = true;
			CompletedPhase = DT_INFO.PHASE.NONE;

			m_Logger = new Logger(m_Grid.EntityId.ToString(), "DerelictTimer");
		}

		/// <summary>
		/// Starts or resumes the derelict timer
		/// </summary>
		/// <returns>Returns false if dereliction is disabled</returns>
		public bool start() {
			int seconds = ConquestSettings.getInstance().DerelictCountdown;
			if (seconds < 0) {
				log("Dereliction timers disabled.  No timer started.", "start");
				return false;
			}

			// Check if there is a timer to resume for this entity
			DT_INFO existing = StateTracker.getInstance().findActiveDerelictTimer(m_Grid.EntityId);
			if (existing != null) {
				// Resuming an existing timer
				log("Resuming existing timer", "start");

				m_TimerInfo = existing;

				m_Timer = new MyTimer(m_TimerInfo.MillisRemaining, timerExpired);
				m_Timer.Start();
				log("Timer resumed with " + m_TimerInfo.MillisRemaining + "ms", "start");
			} else {
				// Starting a new timer
				log("Starting new timer", "start");

				m_TimerInfo = new DT_INFO();
				m_TimerInfo.GridID = m_Grid.EntityId;
				m_TimerInfo.Phase = DT_INFO.PHASE.INITIAL;
				m_TimerInfo.MillisRemaining = seconds * 1000;

				m_TimerInfo.StartTime = DateTime.UtcNow;
				m_TimerInfo.StartingMillisRemaining = m_TimerInfo.MillisRemaining;

				TimerExpired = false;

				m_Timer = new MyTimer(m_TimerInfo.MillisRemaining, timerExpired);
				m_Timer.Start();
				log("Timer started with " + m_TimerInfo.MillisRemaining + "ms", "start");

				StateTracker.getInstance().addNewDerelictTimer(m_TimerInfo);
			}

			return true;
		}

		/// <summary>
		/// Advanced to the next timer stage
		/// </summary>
		/// <returns>True when all stages have been completed</returns>
		public bool advance() {
			// TODO
			return true;
		}

		/// <summary>
		/// Cancels the dereliction timer
		/// </summary>
		/// <returns>False if there was no timer to cancel</returns>
		public bool cancel() {
			if (m_Timer == null)
				return false;

			m_Timer.Stop();
			m_Timer = null;

			m_TimerInfo = null;

			TimerExpired = false;

			log("Timer cancelled", "cancel");

			return true;
		}

		private void timerExpired() {
			if (m_Timer == null)
				return;

			log("Dereliction timer has expired", "timerExpired");

			StateTracker.getInstance().removeDerelictTimer(m_TimerInfo.GridID);

			m_Timer.Stop();
			m_Timer = null;

			CompletedPhase = m_TimerInfo.Phase;
			m_TimerInfo = null;

			TimerExpired = true;
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}
	}
}

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
			public DateTime LastUpdated;
			public int TimerLength;
			public PHASE Phase;
		}

		private DT_INFO m_TimerInfo = null;
		private MyTimer m_Timer = null;
		private IMyCubeGrid m_Grid = null;

		public bool TimerExpired { get; private set; }
		public DT_INFO.PHASE CompletedPhase { get; private set; }
		public int SecondsRemaining {
			get { return (int)(m_TimerInfo.MillisRemaining / 1000); }
		}

		private Logger m_Logger = null;

		public long ID { get { return m_TimerInfo.GridID; } }

		public DerelictTimer(IMyCubeGrid grid) {
			m_Grid = grid;

			m_TimerInfo = null;
			m_Timer = null;

			CompletedPhase = DT_INFO.PHASE.NONE;

			m_Logger = new Logger(m_Grid.EntityId.ToString(), "DerelictTimer");
		}

		/// <summary>
		/// Starts or resumes the derelict timer
		/// </summary>
		/// <returns>Returns false if dereliction is disabled</returns>
		public bool start() {
			int seconds = ConquestSettings.getInstance().CleanupPeriod;
			int settingsTimerLength = seconds * 1000;
			log("Starting timer with settings start value of " + seconds + " seconds.", 
				"start", Logger.severity.TRACE);
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

				// If the settings Timer Length has changed, update this timer accordingly
				if (m_TimerInfo.TimerLength != settingsTimerLength) {
					log("Timer length has changed from " + m_TimerInfo.TimerLength +
						"ms to " + settingsTimerLength + "ms", "start");

					int savedMillis = m_TimerInfo.MillisRemaining;
					decimal lengthRatio = (decimal)settingsTimerLength / (decimal)m_TimerInfo.TimerLength;
					int correctedMillis = (int)(savedMillis * lengthRatio);

					log("Changing this timer from " + savedMillis + "ms to " + correctedMillis +
						"ms using ratio " + lengthRatio, "start");

					m_TimerInfo.MillisRemaining = correctedMillis;
					m_TimerInfo.TimerLength = settingsTimerLength;
				}

				m_Timer = new MyTimer(m_TimerInfo.MillisRemaining, timerExpired);
				m_Timer.Start();
				log("Timer resumed with " + m_TimerInfo.MillisRemaining + "ms", "start");
			} else {
				// Starting a new timer
				log("Starting new timer", "start");

				m_TimerInfo = new DT_INFO();
				m_TimerInfo.GridID = m_Grid.EntityId;
				m_TimerInfo.Phase = DT_INFO.PHASE.INITIAL;
				m_TimerInfo.TimerLength = seconds * 1000;
				m_TimerInfo.MillisRemaining = m_TimerInfo.TimerLength;
				m_TimerInfo.LastUpdated = DateTime.UtcNow;

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

			StateTracker.getInstance().removeDerelictTimer(m_TimerInfo.GridID);

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

		public void updateTimeRemaining() {
			//log("", "updateTimeRemaining");

			if (TimerExpired) {
				return;
			}

			// Update Time Remaining
			DateTime currentTime = DateTime.UtcNow;

			int millisSinceLastUpdate = (int)(currentTime - m_TimerInfo.LastUpdated).TotalMilliseconds;

			//log(String.Format("current time {0}, last updated {1}, millis since {2}, old millis remaining {3}",
			//	currentTime, m_TimerInfo.StartTime, millisSinceLastUpdate, m_TimerInfo.MillisRemaining), "updateTimeRemaining");

			m_TimerInfo.MillisRemaining = m_TimerInfo.MillisRemaining - millisSinceLastUpdate;
			m_TimerInfo.LastUpdated = currentTime;

			//log(String.Format("new millis remaining {0}",
			//	m_TimerInfo.MillisRemaining), "updateTimeRemaining");

			// If there's negative time left, we missed an expiration
			if (m_TimerInfo.MillisRemaining <= 0) {
				if (m_Timer == null) {
					log("negative time left but unexpired, no timer stored",
						"updateTimeRemaining", Logger.severity.ERROR);
				}
				else {
					log("timer failed to trigger expire",
						"updateTimeRemaining", Logger.severity.ERROR);
				}

				// uncomment this to hotfix the issue
				log("manually expiring","updateTimeRemaining", Logger.severity.WARNING);
				timerExpired();
			}
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}
	}
}

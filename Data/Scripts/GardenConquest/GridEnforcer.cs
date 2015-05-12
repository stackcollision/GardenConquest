using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Definitions;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Serializer;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Library.Utils;
using Interfaces = Sandbox.ModAPI.Interfaces;
using InGame = Sandbox.ModAPI.Ingame;

namespace GardenConquest {

	/// <summary>
	/// Applied to every grid.  Verifies that all grids comply with the rules and enforces them.
	/// Only attaches on the server.  Removes itself on the client.
	/// </summary>
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid))]
	public class GridEnforcer : MyGameLogicComponent {

		#region Structs and Enums

		public enum VIOLATION_TYPE {
			NONE,
			BLOCK,
			TURRET
		}

		#endregion
		#region Class Members

		private IMyCubeGrid m_Grid = null;
		private InGame.IMyBeacon m_Classifier = null;
		private IMyFaction m_OwningFaction = null;

		// There are two different members for classification
		// The first is the class of the placed grid classifier (m_Classifier)
		// But if that block is incomplete/offline/damaged then the rules are
		// still applied as if it was UNCLASSIFIED
		private HullClass.CLASS m_ReservedClass = HullClass.CLASS.UNCLASSIFIED;
		private HullClass.CLASS m_ActualClass = HullClass.CLASS.UNCLASSIFIED;

		private bool m_IsServer = false;
		private int m_BlockCount = 0;
		private int m_TurretCount = 0;
		private bool m_BeyondFirst100 = false;
		private bool m_Merging = false;

		private ActiveDerelictTimer m_DerelictTimer = null;
		private bool m_IsDerelict = false;

		private Logger m_Logger = null;

		public IMyFaction Faction { get { return m_OwningFaction; } }
		public IMyCubeGrid Grid { get { return m_Grid; } }
		public InGame.IMyBeacon Classifier { get { return m_Classifier; } }
		public HullClass.CLASS ActualClass { get { return m_ActualClass; } }

		#endregion
		#region Events

		private static Action<GridEnforcer, VIOLATION_TYPE> eventOnViolation;
		public static event Action<GridEnforcer, VIOLATION_TYPE> OnViolation {
			add { eventOnViolation += value; }
			remove { eventOnViolation -= value; }
		}

		private static Action<ActiveDerelictTimer> eventOnDerelictStart;
		public static event Action<ActiveDerelictTimer> OnDerelictStart {
			add { eventOnDerelictStart += value; }
			remove { eventOnDerelictStart -= value; }
		}

		private static Action<ActiveDerelictTimer, ActiveDerelictTimer.COMPLETION> eventOnDerelictEnd;
		public static event Action<ActiveDerelictTimer, ActiveDerelictTimer.COMPLETION> OnDerelictEnd {
			add { eventOnDerelictEnd += value; }
			remove { eventOnDerelictEnd -= value; }
		}

		private static Action<GridEnforcer, HullClass.CLASS> eventOnClassProhibited;
		public static event Action<GridEnforcer, HullClass.CLASS> OnClassProhibited {
			add { eventOnClassProhibited += value; }
			remove { eventOnClassProhibited -= value; }
		}

		#endregion
		#region Class Lifecycle

		public GridEnforcer() {
			m_Classifier = null;
			m_OwningFaction = null;
		}

		public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
			base.Init(objectBuilder);
			m_Grid = Entity as IMyCubeGrid;
			m_Classifier = null;
			m_OwningFaction = null;

			m_Logger = new Logger(m_Grid.EntityId.ToString(), "GridEnforcer");
			log("Loaded into new grid");

			// If this is not the server we don't need this class.
			// When we modify the grid on the server the changes should be
			// sent to all clients
			m_IsServer = Utility.isServer();
			if (!m_IsServer) {
				// No cleverness allowed :[
				log("Disabled.  Not server.", "Init");
				return;
			}

			// We need to only turn on our rule checking after startup. Otherwise, if
			// a beacon is destroyed and then the server restarts, all but the first
			// 25 blocks will be deleted on startup.
			m_Grid.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

			m_BlockCount = 0;

			m_Grid.OnBlockAdded += blockAdded;
			m_Grid.OnBlockRemoved += blockRemoved;
			m_Grid.OnBlockOwnershipChanged += blockOwnerChanged;
		}

		public override void Close() {
			log("Grid closed", "Close");

			m_Grid.OnBlockAdded -= blockAdded;
			m_Grid.OnBlockRemoved -= blockRemoved;
			m_Grid.OnBlockOwnershipChanged -= blockOwnerChanged;

			if (m_Classifier != null) {
				(m_Classifier as IMyCubeBlock).IsWorkingChanged -= classifierWorkingChanged;
				m_Classifier = null;
			}

			if (m_DerelictTimer != null) {
				m_DerelictTimer.Timer.Stop();
				m_DerelictTimer.Timer = null;
				m_DerelictTimer = null;
			}

			m_Grid = null;
			m_Logger = null;
		}

		#endregion

		public override void UpdateBeforeSimulation100() {
			if (!m_IsServer)
				return;

			// NOTE: Can't turn off this update, because other scripts might also want it
			if (!m_BeyondFirst100) {
				m_BeyondFirst100 = true;

				// Once the server has loaded all block for this grid, check if we are
				// classified.  If not, warn the owner about the timer
				if (m_ActualClass == HullClass.CLASS.UNCLASSIFIED) {
					// Since the game is just starting, we need to check if we're supposed
					// to resume this timer or start a brand new one
					ActiveDerelictTimer dt = StateTracker.getInstance().findActiveDerelictTimer(
						m_Grid.EntityId);
					if (dt == null)
						startDerelictionTimer();
					else
						resumeDerelictionTimer(dt);
				}
			}

			// If we just completed a merge check if this grid is violating rules
			if (m_Merging) {
				if (checkRules() != VIOLATION_TYPE.NONE)
					startDerelictionTimer();
				m_Merging = false;
			}
		}

		/// <summary>
		/// Marks the grid to skip rule enforcement for the next few frames because of a grid merge.
		/// </summary>
		public void markForMerge() {
			log("This grid is having another merged into it", "markForMerge");
			m_Merging = true;
		}

		/// <summary>
		/// Called when a block is added to the grid.
		/// Decides whether or not to allow the block to be placed.
		/// Increments counts and checks for classification.
		/// </summary>
		/// <param name="added"></param>
		private void blockAdded(IMySlimBlock added) {
			m_BlockCount++;
			log("Block added to grid.  Count now: " + m_BlockCount, "blockAdded");
			if (added.FatBlock != null && (
					added.FatBlock is InGame.IMyLargeGatlingTurret ||
					added.FatBlock is InGame.IMyLargeMissileTurret
			)) {
				m_TurretCount++;
				log("Turret count now: " + m_TurretCount, "blockAdded");
			}

			// Check if its a class beacon
			if (added.FatBlock != null &&
				added.FatBlock is InGame.IMyBeacon &&
				added.FatBlock.BlockDefinition.SubtypeName.Contains("HullClassifier")
			) {
				// Reserve the grid class
				if (!reserveClass(added))
					removeBlock(added); // Class could not be reserved, so remove block

				// Return after classification
				// It is recommended that the distance between the unclassified block limit and 
				// the fighter/corvette block limits be substantial to prevent an issue
				// where as soon as the hull is classified it's over the limit.
				return;
			}

			// If this grid is currently being merged do not check rules
			if (m_Merging)
				return;

			// Check if we are violating class rules
			VIOLATION_TYPE check = checkRules();
			if (check != VIOLATION_TYPE.NONE) {
				eventOnViolation(this, check);
				removeBlock(added);
			}
		}

		/// <summary>
		/// Checks if the grid complies with the rules.  Returns true if any rule is violated.
		/// </summary>
		/// <returns></returns>
		private VIOLATION_TYPE checkRules() {
			// Don't apply rules until server startup is completed
			if (!m_BeyondFirst100)
				return VIOLATION_TYPE.NONE;

			// Test rules based on actual class, so they only get the block limit
			// if the classifier beacon is complete
			HullRule r = ConquestSettings.getInstance().HullRules[(int)m_ActualClass];
			log("Count: " + m_BlockCount + " Limit: " + r.MaxBlocks, "checkRules");

			// Check general block count limit
			if (m_BlockCount > r.MaxBlocks) {
				log("Grid has violated block limit for class", "checkRules");
				return VIOLATION_TYPE.BLOCK;
			}

			// Check number of turrets
			if (m_TurretCount > r.MaxTurrets) {
				log("Grid has violated turret limit for class", "checkRules");
				return VIOLATION_TYPE.TURRET;
			}

			return VIOLATION_TYPE.NONE;
		}

		/// <summary>
		/// Certain classes have count limitations.  Check if this faction can have any more
		/// of this class.
		/// </summary>
		/// <param name="c">Class to check</param>
		/// <returns></returns>
		private bool checkClassAllowed(HullClass.CLASS c) {
			// QUESTION: maybe players not in factions shouldn't be able to classify?
			if (m_OwningFaction == null)
				return true;

			// If attempting to classify as Unlicensed, check that the faction has room
			if (c == HullClass.CLASS.UNLICENSED) {
				FactionFleet fleet = StateTracker.getInstance().getFleet(m_OwningFaction.FactionId);
				if (fleet.countClass(c) >= ConquestSettings.getInstance().UnlicensedPerFaction) {
					eventOnClassProhibited(this, c);
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Called when a block is removed.  Decrements counts and checks for declassification.
		/// </summary>
		/// <param name="removed"></param>
		private void blockRemoved(IMySlimBlock removed) {
			m_BlockCount--;
			log("Block removed from grid.  Count now: " + m_BlockCount, "blockRemoved");

			// Check if the removed block was the class beacon
			if (removed.FatBlock != null &&
				removed.FatBlock is InGame.IMyBeacon &&
				removed.FatBlock.BlockDefinition.SubtypeName.Contains("HullClassifier")
			) {
				if (removed.FatBlock == m_Classifier)
					removeClass();
			} else if (removed.FatBlock != null && (
					removed.FatBlock is InGame.IMyLargeGatlingTurret ||
					removed.FatBlock is InGame.IMyLargeMissileTurret
			)) {
				m_TurretCount--;
			}
		}

		/// <summary>
		/// Called when ownership on the grid changes.
		/// </summary>
		/// <remarks>
		/// Testing indicates this is only called once per grid, even if you change 50 blocks at once
		/// </remarks>
		/// <param name="changed"></param>
		private void blockOwnerChanged(IMyCubeGrid changed) {
			log("Ownership changed", "blockOwnerChanged");
			reevaluateOwningFaction();
		}

		/// <summary>
		/// Forces a block to be removed, usually in the case of a rule violation.
		/// </summary>
		/// <param name="b"></param>
		private void removeBlock(IMySlimBlock b) {
			// TODO: spawn materials in place so they're not lost
			m_Grid.RemoveBlock(b);
		}

		/// <summary>
		/// Reserve the class for this grid.  The block rule will not be applied until the
		/// beacon is complete"
		/// </summary>
		/// <param name="added"></param>
		/// <returns>False if the class cannot be reserved</returns>
		private bool reserveClass(IMySlimBlock added) {
			// Two classifiers not allowed
			if (m_Classifier != null) {
				log("Grid already has a classifier.  Removing this new one.", "reserveClass");
				return false;
			} else {
				m_Classifier = added.FatBlock as InGame.IMyBeacon;
				(m_Classifier as IMyCubeBlock).IsWorkingChanged += classifierWorkingChanged;

				HullClass.CLASS c = HullClass.hullClassFromString(
					added.FatBlock.BlockDefinition.SubtypeName);
				if (checkClassAllowed(c)) {
					// If the grid can be this class, set only the reserved class
					// The actual class will be set when the thing powers on
					m_ReservedClass = c;
					onClassChange(HullClass.CLASS.UNCLASSIFIED, m_ReservedClass);

					log("Hull class reserved as " +
						HullClass.ClassStrings[(int)m_ReservedClass], "reserveClass");

					// Do not cancel the dereliction timer yet, only do that when powered on
					return true;
				} else {
					log("Class reservation as " + HullClass.ClassStrings[(int)c] +
						" not permitted.", "reserveClass");
					return false;
				}
			}
		}

		/// <summary>
		/// Promotes the grid's actual class to its reserved class so the rule is applied
		/// </summary>
		private void promoteClass() {
			m_ActualClass = m_ReservedClass;

			log("Actual class promoted to " + m_ActualClass, "promoteClass");

			// Cancel dereliction timer if we have one
			// But only if the grid is within this new class's limits
			if (checkRules() == VIOLATION_TYPE.NONE) {
				if (m_DerelictTimer != null)
					cancelDerelictionTimer();
			} else {
				log("This grid is still in violation of its new class.  Timer NOT stopped",
					"promoteClass");
			}
		}

		/// <summary>
		/// Sets the actual class back to unclassified and starts the dereliction timer
		/// A beacon must be working to have its rules applied
		/// </summary>
		private void demoteClass() {
			m_ActualClass = HullClass.CLASS.UNCLASSIFIED;

			log("Actual class returned to " + m_ActualClass, "demoteClass");

			// Start dereliction timer
			startDerelictionTimer();
		}

		/// <summary>
		/// Removes the class reservation, the grid goes back to unclassified
		/// </summary>
		private void removeClass() {
			onClassChange(m_ReservedClass, HullClass.CLASS.UNCLASSIFIED);

			m_ReservedClass = HullClass.CLASS.UNCLASSIFIED;
			m_ActualClass = HullClass.CLASS.UNCLASSIFIED;

			(m_Classifier as IMyCubeBlock).IsWorkingChanged -= classifierWorkingChanged;
			m_Classifier = null;

			log("Hull classification removed", "removeClass");

			// Start dereliction timer
			startDerelictionTimer();
		}

		/// <summary>
		/// Figures out which faction owns this grid, if any
		/// </summary>
		/// <returns>Whether or not the owning faction changed.</returns>
		private bool reevaluateOwningFaction() {
			IMyFaction fac = null;
			bool changed = false;

			// Do we have any owner at all?
			if (m_Grid.BigOwners.Count == 0) {
				// If there is no owner, was there one before?
				if (m_OwningFaction != null) {
					fac = null;
					changed = true;
				} else {
					fac = null;
					changed = false;
				}
			} else {
				// NOTE: Hopefully this is sorted by number of blocks owned?
				long biggestOwner = m_Grid.BigOwners[0];
				fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(biggestOwner);
				if (m_OwningFaction != fac)
					changed = true;
			}

			if (changed) {
				onFactionChange(m_OwningFaction, fac);
				m_OwningFaction = fac;
			}

			return changed;
		}

		/// <summary>
		/// When the owning faction changes we need to decrement this class's count on the old
		/// faction and increment it on the new one.
		/// </summary>
		/// <param name="oldFac"></param>
		/// <param name="newFac"></param>
		private void onFactionChange(IMyFaction oldFac, IMyFaction newFac) {
			if (oldFac == newFac)
				return;

			log("Faction has changed", "onFactionChange");

			FactionFleet oldFleet = oldFac == null ? null :
				StateTracker.getInstance().getFleet(oldFac.FactionId);
			FactionFleet newFleet = newFac == null ? null :
				StateTracker.getInstance().getFleet(newFac.FactionId);

			// Subtract one from the old fleet, if there was one
			if (oldFleet != null) {
				oldFleet.removeClass(m_ReservedClass);
			}

			// Add one to the new fleet, if there is one
			if (newFleet != null) {
				newFleet.addClass(m_ReservedClass);
			}
		}

		/// <summary>
		/// When the class changes decrement the old class count and increment the new one
		/// for the owning faction.
		/// </summary>
		/// <param name="oldClass"></param>
		/// <param name="newClass"></param>
		private void onClassChange(HullClass.CLASS oldClass, HullClass.CLASS newClass) {
			if (oldClass == newClass || m_OwningFaction == null)
				return;

			log("Class has changed", "onClassChange");

			FactionFleet fleet = StateTracker.getInstance().getFleet(m_OwningFaction.FactionId);

			fleet.removeClass(oldClass);
			fleet.addClass(newClass);
		}

		/// <summary>
		/// Starts the timer and alerts the player that their grid will become a derelict
		/// after x time
		/// </summary>
		private void startDerelictionTimer() {
			// Create timer record
			m_DerelictTimer = new ActiveDerelictTimer();
			m_DerelictTimer.Grid = m_Grid;
			m_DerelictTimer.GridID = m_Grid.EntityId;
			m_DerelictTimer.Phase = ActiveDerelictTimer.PHASE.INITIAL;
			m_DerelictTimer.MillisRemaining =
				ConquestSettings.getInstance().DerelictCountdown * 1000;
			m_DerelictTimer.Timer = new MyTimer(m_DerelictTimer.MillisRemaining, makeDerelict);
			m_DerelictTimer.Timer.Start();

			m_DerelictTimer.StartingMillisRemaining = m_DerelictTimer.MillisRemaining;
			m_DerelictTimer.StartTime = DateTime.UtcNow;

			// Add to state
			StateTracker.getInstance().addNewDerelictTimer(m_DerelictTimer);
			eventOnDerelictStart(m_DerelictTimer);

			log("Dereliction timer started", "startDerelictionTimer");
		}

		/// <summary>
		/// Picks up where a saved-state timer left off
		/// </summary>
		/// <param name="dt">Timer to resume</param>
		private void resumeDerelictionTimer(ActiveDerelictTimer dt) {
			if (dt == null)
				return;

			m_DerelictTimer = dt;
			m_DerelictTimer.Grid = m_Grid;
			m_DerelictTimer.Timer = new MyTimer(m_DerelictTimer.MillisRemaining, makeDerelict);
			m_DerelictTimer.Timer.Start();

			log("Dereliction timer resumed with " + m_DerelictTimer.MillisRemaining,
				"resumeDerelictionTimer");
		}

		/// <summary>
		/// If the rules are met before the timer experies this cancels the timer
		/// </summary>
		private void cancelDerelictionTimer() {
			if (m_DerelictTimer != null) {
				StateTracker.getInstance().removeDerelictTimer(m_DerelictTimer);
				eventOnDerelictEnd(m_DerelictTimer, ActiveDerelictTimer.COMPLETION.CANCELLED);

				m_DerelictTimer.Timer.Stop();
				m_DerelictTimer.Timer = null;
				m_DerelictTimer = null;

				log("Dereliction timer cancelled", "cancelDerelictionTimer");
			}
		}

		/// <summary>
		/// When the dereliction timer expires this turns the grid into a derelict.
		/// Destroys functional blocks and stops the grid.
		/// </summary>
		private void makeDerelict() {
			// How did we get here without a timer?
			if (m_DerelictTimer == null)
				return;

			// Get a list of all functional blocks
			List<IMySlimBlock> funcBlocks = new List<IMySlimBlock>();
			m_Grid.GetBlocks(funcBlocks,
				x => x.FatBlock != null && x.FatBlock is IMyFunctionalBlock);

			// Go through the list and destroy them
			// TODO: Do this in phases with damage instead of just poofing them
			foreach (IMySlimBlock block in funcBlocks) {
				// Use the grid pointer from the block itself, because if one of the
				// previously removed blocks caused the grid to split, we can't use the
				// stored grid to remove it
				block.CubeGrid.RemoveBlock(block);
			}

			StateTracker.getInstance().removeDerelictTimer(m_DerelictTimer);
			eventOnDerelictEnd(m_DerelictTimer, ActiveDerelictTimer.COMPLETION.ELAPSED);

			// Get rid of the timer
			m_DerelictTimer.Timer.Stop();
			m_DerelictTimer.Timer = null;
			m_DerelictTimer = null;

			log("Timer expired.  Grid turned into a derelict.", "makeDerelict");
		}

		private void classifierWorkingChanged(IMyCubeBlock b) {
			log("Working: " + b.IsWorking, "classifierWorkingChanged");

			if (b.IsWorking) {
				// If the block is working, apply the class
				promoteClass();
			} else {
				// If the block has stopped working, demote
				demoteClass();
			}
		}

		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false) {
			return Entity.GetObjectBuilder();
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}
	}
}

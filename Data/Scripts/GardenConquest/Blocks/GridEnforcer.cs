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

using GardenConquest.Records;
using GardenConquest.Core;
using GardenConquest.Extensions;

namespace GardenConquest.Blocks {

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
			TURRET,
			FIXED
		}

		#endregion
		#region Class Members

		private IMyCubeGrid m_Grid = null;
		private InGame.IMyBeacon m_Classifier = null;
		private GridOwner m_Owner = null;

		// There are two different members for classification
		// The first is the class of the placed grid classifier (m_Classifier)
		// But if that block is incomplete/offline/damaged then the rules are
		// still applied as if it was UNCLASSIFIED
		private HullClass.CLASS m_ReservedClass = HullClass.CLASS.UNCLASSIFIED;
		private HullClass.CLASS m_ActualClass = HullClass.CLASS.UNCLASSIFIED;

		private bool m_IsServer = false;
		private bool m_CheckServerLater = false;

		private int m_BlockCount = 0;
		private int m_TurretCount = 0;
		private int m_FixedCount = 0;
		private bool m_BeyondFirst100 = false;
		private bool m_Merging = false;

		private ActiveDerelictTimer m_DerelictTimer = null;
		private bool m_IsDerelict = false;

		private Logger m_Logger = null;

		public IMyFaction Faction { get { return m_Owner.getFaction(); } }
		public IMyCubeGrid Grid { get { return m_Grid; } }
		public InGame.IMyBeacon Classifier { get { return m_Classifier; } }
		public HullClass.CLASS ActualClass { get { return m_ActualClass; } }

		#endregion
		#region Events

		// OnViolation
		private static Action<GridEnforcer, VIOLATION_TYPE> eventOnViolation;
		public static event Action<GridEnforcer, VIOLATION_TYPE> OnViolation {
			add { eventOnViolation += value; }
			remove { eventOnViolation -= value; }
		}

		// OnDerelictStart
		private static Action<ActiveDerelictTimer> eventOnDerelictStart;
		public static event Action<ActiveDerelictTimer> OnDerelictStart {
			add { eventOnDerelictStart += value; }
			remove { eventOnDerelictStart -= value; }
		}

		// OnDerelictEnd
		private static Action<ActiveDerelictTimer, ActiveDerelictTimer.COMPLETION> eventOnDerelictEnd;
		public static event Action<ActiveDerelictTimer, ActiveDerelictTimer.COMPLETION> OnDerelictEnd {
			add { eventOnDerelictEnd += value; }
			remove { eventOnDerelictEnd -= value; }
		}

		// OnClassProhibited
		private static Action<GridEnforcer, HullClass.CLASS> eventOnClassProhibited;
		public static event Action<GridEnforcer, HullClass.CLASS> OnClassProhibited {
			add { eventOnClassProhibited += value; }
			remove { eventOnClassProhibited -= value; }
		}

		#endregion
		#region Class Lifecycle

		public GridEnforcer() {
			m_Classifier = null;
			m_Owner = null;
		}

		public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
			base.Init(objectBuilder);
			m_Grid = Entity as IMyCubeGrid;
			m_Classifier = null;

			m_Logger = new Logger(m_Grid.EntityId.ToString(), "GridEnforcer");
			log("Loaded into new grid", "Init");

			// If this is not the server we don't need this class.
			// When we modify the grid on the server the changes should be
			// sent to all clients
			try {
				m_IsServer = Utility.isServer();
				log("Is server: " + m_IsServer, "Init");
				if (!m_IsServer) {
					// No cleverness allowed :[
					log("Disabled.  Not server.", "Init");
					return;
				}
			} catch (NullReferenceException e) {
				log("Exception.  Multiplayer is not initialized.  Assuming server for time being: " + e,
					"Init");
				// If we get an exception because Multiplayer was null (WHY KEEN???)
				// assume we are the server for a little while and check again later
				m_IsServer = true;
				m_CheckServerLater = true;
			}

			// We need to only turn on our rule checking after startup. Otherwise, if
			// a beacon is destroyed and then the server restarts, all but the first
			// 25 blocks will be deleted on startup.
			m_Grid.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

			m_BlockCount = 0;

			m_Owner = new GridOwner(m_Grid);

			m_Grid.OnBlockAdded += blockAdded;
			m_Grid.OnBlockRemoved += blockRemoved;
			m_Grid.OnBlockOwnershipChanged += blockOwnerChanged;
		}

		public override void Close() {
			log("Grid closed", "Close");

			unServerize();

			m_Grid = null;
			m_Logger = null;
		}

		#endregion

		/// <summary>
		/// Removes subscriptions to events if we prematurely declared outselves the server
		/// </summary>
		private void unServerize() {
			if (m_IsServer) {
				m_Grid.OnBlockAdded -= blockAdded;
				m_Grid.OnBlockRemoved -= blockRemoved;
				m_Grid.OnBlockOwnershipChanged -= blockOwnerChanged;

				if (m_Classifier != null) {
					(m_Classifier as IMyCubeBlock).IsWorkingChanged -= classifierWorkingChanged;
					m_Classifier = null;
				}

				if (m_DerelictTimer != null)
					cancelDerelictionTimer(false);

				m_Owner = null;
			}

		}

		public override void UpdateBeforeSimulation100() {
			if (!m_IsServer)
				return;

			// Do we need to verify that we are the server?
			if (m_CheckServerLater && m_IsServer) {
				try {
					log("Late server check", "UpdateBeforeSimulation100");
					m_IsServer = Utility.isServer();
					log("Is server: " + m_IsServer, "Init");
					m_CheckServerLater = false;

					if (!m_IsServer)
						unServerize();
				} catch (NullReferenceException e) {
					// Continue thinking we are server for the time being
					// This shouldn't happen (lol)
					log("Exception checking if server: " + e, "UpdateBeforeSimulation100");
				}
			}

			try {

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

				if (m_IsDerelict) {
					m_IsDerelict = false;
					makeDerelict();
				}
			} catch (Exception e) {
				log("Exception occured: " + e, "UpdateBeforeSimulation100");
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
			log("Block added to grid '" + m_Grid.DisplayName + "'. Count now: " + m_BlockCount, "blockAdded");

			if (added.FatBlock != null) {
				// Class beacon
				if (
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
					// Turret weapons
				else if (
					added.FatBlock is InGame.IMyLargeGatlingTurret ||
					added.FatBlock is InGame.IMyLargeInteriorTurret ||
					added.FatBlock is InGame.IMyLargeMissileTurret
				) {
					m_TurretCount++;
					log("Turret count now: " + m_TurretCount, "blockAdded");
				}
					// Fixed weapons
				else if (
					added.FatBlock is InGame.IMySmallMissileLauncher ||
					added.FatBlock is InGame.IMySmallMissileLauncherReload ||
					added.FatBlock is InGame.IMySmallGatlingGun
				) {
					m_FixedCount++;
					log("Fixed weapon count now: " + m_FixedCount, "blockAdded");
				}
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
			log("Block count (" + m_BlockCount + "/" + r.MaxBlocks + ")", "checkRules");

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

			// Check number of fixed weapons
			if (m_FixedCount > r.MaxFixed) {
				log("Grid has violated fixed weapon limit for class", "checkRules");
				return VIOLATION_TYPE.FIXED;
			}

			return VIOLATION_TYPE.NONE;
		}

		/// <summary>
		/// Certain classes have count limitations.  Check if this faction can have any more
		/// of this class.
		/// </summary>
		/// <param name="c">Class to check</param>
		/// <returns>True if the class is allowed</returns>
		private bool checkClassAllowed(HullClass.CLASS c) {
			GridOwner.OWNER_TYPE ownerType = m_Owner.getOwnerType();

			// If there is no owner at all we can't allow the class because there's no way to track
			if (ownerType == GridOwner.OWNER_TYPE.UNOWNED)
				return false;

			// Players without a faction are permitted to have a certain number of unlicensed grids
			// only
			if (ownerType == GridOwner.OWNER_TYPE.PLAYER) {
				if (c != HullClass.CLASS.UNLICENSED)
					return false;

				int limit = ConquestSettings.getInstance().SoloPlayerLimit;
				FactionFleet fleet = m_Owner.getFleet();

				log("Private player fleet unlicensed count: (" + fleet.countClass(c)
					+ "/" + limit + ")", "checkClassAllowed");

				if (fleet.countClass(c) >= limit) {
					eventOnClassProhibited(this, c);
					return false;
				} else {
					return true;
				}
			} else if (ownerType == GridOwner.OWNER_TYPE.FACTION) {
				int limit = ConquestSettings.getInstance().FactionLimits[(int)c];
				FactionFleet fleet = m_Owner.getFleet();

				log("Faction hull limit: (" + fleet.countClass(c) + "/" + limit + ")",
					"checkClassAllowed");

				// If limit < 0, this class is unrestricted
				if (limit < 0)
					return true;

				if (fleet.countClass(c) >= limit) {
					eventOnClassProhibited(this, c);
					return false;
				}

				return true;
			}

			return false;
		}

		/// <summary>
		/// Called when a block is removed.  Decrements counts and checks for declassification.
		/// </summary>
		/// <param name="removed"></param>
		private void blockRemoved(IMySlimBlock removed) {
			m_BlockCount--;
			log("Block removed from grid '" + m_Grid.DisplayName + "'. Count now: " + m_BlockCount, "blockRemoved");
			// Check if the removed block was the class beacon
			if (removed.FatBlock != null) {
				// Class beacon
				if (
					removed.FatBlock is InGame.IMyBeacon &&
					removed.FatBlock.BlockDefinition.SubtypeName.Contains("HullClassifier")
				) {
					removeClass();
				}
					// Turret weapons
				else if (
					removed.FatBlock is InGame.IMyLargeGatlingTurret ||
					removed.FatBlock is InGame.IMyLargeInteriorTurret ||
					removed.FatBlock is InGame.IMyLargeMissileTurret
				) {
					m_TurretCount--;
				}
					// Fixed weapons
				else if (
					removed.FatBlock is InGame.IMySmallMissileLauncher ||
					removed.FatBlock is InGame.IMySmallMissileLauncherReload ||
					removed.FatBlock is InGame.IMySmallGatlingGun
				) {
					m_FixedCount--;
				}
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
			reevaluateOwnership();
		}

		/// <summary>
		/// Forces a block to be removed, usually in the case of a rule violation.
		/// </summary>
		/// <param name="b"></param>
		private void removeBlock(IMySlimBlock b) {
			// spawn materials in place so they're not lost

			// TODO: determine best inventory target
			IMyInventory inventory = null;

			// WAITING: once Keen accepts this PR
			// https://github.com/KeenSoftwareHouse/SpaceEngineers/pull/52
			// the below will spawn materials in the inventory if it exists and has space,
			// or otherwise as floating objects in space,
			//b.FullyDismount(inventory);
			//b.MoveItemsFromConstructionStockpile(inventory);
			//b.SpawnConstructionStockpile();

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
					m_Owner.setClassification(m_ReservedClass);

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
			m_ReservedClass = HullClass.CLASS.UNCLASSIFIED;
			m_ActualClass = HullClass.CLASS.UNCLASSIFIED;

			m_Owner.setClassification(m_ReservedClass);

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
		public bool reevaluateOwnership() {
			bool changed = m_Owner.reevaluateOwnership();

			// TODO: Grids without owners should have a dereliction timer started
			// even if they have a classifier
			// Likewise, grids which now have ownership which also have a classifier
			// should have their dereliction timer stopped

			return changed;
		}

		/// <summary>
		/// Starts the timer and alerts the player that their grid will become a derelict
		/// after x time
		/// </summary>
		private void startDerelictionTimer() {
			// Don't start a second timer
			if (m_DerelictTimer != null)
				return;

			int seconds = ConquestSettings.getInstance().DerelictCountdown;
			if (seconds < 0) {
				log("Dereliction timer disabled.  No timer started.", "startDerelictionTimer");
				return;
			}

			// Create timer record
			m_DerelictTimer = new ActiveDerelictTimer();
			m_DerelictTimer.Grid = m_Grid;
			m_DerelictTimer.GridID = m_Grid.EntityId;
			m_DerelictTimer.Phase = ActiveDerelictTimer.PHASE.INITIAL;
			m_DerelictTimer.MillisRemaining = seconds * 1000;
			m_DerelictTimer.Timer = new MyTimer(m_DerelictTimer.MillisRemaining,
				derelictionTimerExpired);
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
			if (ConquestSettings.getInstance().DerelictCountdown < 0) {
				log("Dereliction timer disabled.  No timer resumed.", "resumeDerelictionTimer");
				return;
			}

			m_DerelictTimer = dt;
			m_DerelictTimer.Grid = m_Grid;
			m_DerelictTimer.Timer = new MyTimer(m_DerelictTimer.MillisRemaining,
				derelictionTimerExpired);
			m_DerelictTimer.Timer.Start();

			log("Dereliction timer resumed with " + m_DerelictTimer.MillisRemaining,
				"resumeDerelictionTimer");
		}

		/// <summary>
		/// If the rules are met before the timer experies this cancels the timer
		/// </summary>
		private void cancelDerelictionTimer(bool notify = true) {
			if (m_DerelictTimer != null) {
				StateTracker.getInstance().removeDerelictTimer(m_DerelictTimer);
				if (notify)
					eventOnDerelictEnd(m_DerelictTimer, ActiveDerelictTimer.COMPLETION.CANCELLED);

				m_DerelictTimer.Timer.Stop();
				m_DerelictTimer.Timer = null;
				m_DerelictTimer = null;

				log("Dereliction timer cancelled", "cancelDerelictionTimer");
			}
		}

		private void derelictionTimerExpired() {
			// How did we get here without a timer?
			if (m_DerelictTimer == null)
				return;

			m_IsDerelict = true;

			StateTracker.getInstance().removeDerelictTimer(m_DerelictTimer);
			eventOnDerelictEnd(m_DerelictTimer, ActiveDerelictTimer.COMPLETION.ELAPSED);

			// Get rid of the timer
			m_DerelictTimer.Timer.Stop();
			m_DerelictTimer.Timer = null;
			m_DerelictTimer = null;
		}

		/// <summary>
		/// When the dereliction timer expires this turns the grid into a derelict.
		/// Destroys functional blocks and stops the grid.
		/// </summary>
		private void makeDerelict() {
			log("Timer expired.  Grid turned into a derelict.", "makeDerelict");

			// Get a list of all functional blocks
			List<IMySlimBlock> funcBlocks = new List<IMySlimBlock>();
			m_Grid.GetBlocks(funcBlocks,
				x => x.FatBlock != null && x.FatBlock is IMyFunctionalBlock);
			log(funcBlocks.Count + " blocks to remove", "makeDerelict");

			// Go through the list and destroy them
			// TODO: Do this in phases with damage instead of just poofing them
			foreach (IMySlimBlock block in funcBlocks) {
				// Use the grid pointer from the block itself, because if one of the
				// previously removed blocks caused the grid to split, we can't use the
				// stored grid to remove it
				block.CubeGrid.RemoveBlock(block);
			}
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

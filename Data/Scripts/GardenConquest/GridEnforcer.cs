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

		private IMyCubeGrid m_Grid = null;
		public InGame.IMyBeacon m_Classifier { get; private set; }
		private IMyFaction m_OwningFaction;

		private int m_BlockCount = 0;
		private int m_TurretCount = 0;
		private HullClass.CLASS m_Class = HullClass.CLASS.UNCLASSIFIED;
		private bool m_BeyondFirst100 = false;
		private bool m_DoubleClass = false;
		private bool m_Merging = false;

		private ActiveDerelictTimer m_DerelictTimer = null;
		private bool m_IsDerelict = false;

		private Logger m_Logger = null;

		public IMyFaction Faction { get { return m_OwningFaction; } }

		public GridEnforcer() {
			m_Classifier = null;
		}

		public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
			base.Init(objectBuilder);
			m_Grid = Entity as IMyCubeGrid;
			m_Classifier = null;
			m_OwningFaction = null;

			// If this is not the server we don't need this class.
			// When we modify the grid on the server the changes should be
			// sent to all clients
			// Can we remove components?  Let's find out
			if (MyAPIGateway.Multiplayer != null &&
				MyAPIGateway.Multiplayer.MultiplayerActive &&
				!MyAPIGateway.Multiplayer.IsServer
			) {
				// Need to use MyGameLogicComponent because ??
				m_Grid.Components.Remove<MyGameLogicComponent>();
				return;
			}

			m_Logger = new Logger(m_Grid.EntityId.ToString(), "GridEnforcer");
			log("Loaded into new grid");

			// We need to only turn on our rule checking after startup. Otherwise, if
			// a beacon is destroyed and then the server restarts, all but the first
			// 25 blocks will be deleted on startup.
			m_Grid.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

			// Get the initial block count
			// TODO: Is this really necessary?  Will this always be 0?
			List<IMySlimBlock> blocks = new List<IMySlimBlock>();
			m_Grid.GetBlocks(blocks);
			m_BlockCount = blocks.Count;

			m_Grid.OnBlockAdded += blockAdded;
			m_Grid.OnBlockRemoved += blockRemoved;
			m_Grid.OnBlockOwnershipChanged += blockOwnerChanged;
		}

		public override void Close() {
			log("Grid closed", "Close");

			m_Grid.OnBlockAdded -= blockAdded;
			m_Grid.OnBlockRemoved -= blockRemoved;
			m_Grid.OnBlockOwnershipChanged -= blockOwnerChanged;

			m_Grid = null;
		}

		public override void UpdateBeforeSimulation100() {
			// NOTE: Can't turn off this update, because other scripts might also want it
			if (!m_BeyondFirst100) {
				m_BeyondFirst100 = true;

				// Once the server has loaded all block for this grid, check if we are
				// classified.  If not, warn the owner about the timer
				if (m_Class == HullClass.CLASS.UNCLASSIFIED) {
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
				if (checkRules())
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
				// Is this grid already classified?
				if (m_Class != HullClass.CLASS.UNCLASSIFIED) {
					log("Grid is already classified.  Removing this new one.", "blockAdded");
					// Prevent unclassification in blockRemoved
					m_DoubleClass = true;
					removeBlock(added);
				} else {
					HullClass.CLASS c = HullClass.hullClassFromString(
						added.FatBlock.BlockDefinition.SubtypeName);
					if (checkClassAllowed(c)) {
						m_Class = c;
						m_Classifier = added.FatBlock as InGame.IMyBeacon;

						onClassChange(HullClass.CLASS.UNCLASSIFIED, m_Class);

						log("Hull has been classified as " +
							HullClass.ClassStrings[(int)m_Class], "blockAdded");
					} else {
						log("Classification as " + HullClass.ClassStrings[(int)c] + " not permitted",
							"blockAdded");
						removeBlock(added);
					}
				}

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
			if (checkRules()) {
				removeBlock(added);
			}
		}

		/// <summary>
		/// Checks if the grid complies with the rules.  Returns true if any rule is violated.
		/// </summary>
		/// <returns></returns>
		private bool checkRules() {
			// Don't apply rules until server startup is completed
			if (!m_BeyondFirst100)
				return false;

			HullRule r = ConquestSettings.getInstance().HullRules[(int)m_Class];

			// Check general block count limit
			if (m_BlockCount > r.MaxBlocks) {
				log("Grid has violated block limit for class", "blockAdded");
				// TODO: Get a message to the player who placed it
				return true;
			}

			// Check number of turrets
			if (m_TurretCount > r.MaxTurrets) {
				log("Grid has violated turret limit for class", "blockAdded");
				// TODO: Get a message to the player who placed it
				return true;
			}

			return false;
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
					// TODO: Send message
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
			log("Block removed from grid.  Count now: " + m_BlockCount, "blockAdded");

			// Check if the removed block was the class beacon
			if (removed.FatBlock != null &&
				removed.FatBlock is InGame.IMyBeacon &&
				removed.FatBlock.BlockDefinition.SubtypeName.Contains("HullClassifier")
			) {
				// This check prevents the hull from being unclassified
				if (m_DoubleClass) {
					m_DoubleClass = false;
				} else {
					// If the classifier was removed change the class and start the timer
					onClassChange(m_Class, HullClass.CLASS.UNCLASSIFIED);
					m_Class = HullClass.CLASS.UNCLASSIFIED;
					m_Classifier = null;
					startDerelictionTimer();
				}
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
				oldFleet.removeClass(m_Class);
			}
			
			// Add one to the new fleet, if there is one
			if (newFleet != null) {
				newFleet.addClass(m_Class);
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
				StateTracker.getInstance().addFinishedDerelictTimer(
					m_DerelictTimer, ActiveDerelictTimer.COMPLETION.CANCELLED);

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

			StateTracker.getInstance().addFinishedDerelictTimer(
				m_DerelictTimer, ActiveDerelictTimer.COMPLETION.ELAPSED);

			// Get rid of the timer
			m_DerelictTimer.Timer.Stop();
			m_DerelictTimer.Timer = null;
			m_DerelictTimer = null;

			log("Timer expired.  Grid turned into a derelict.", "makeDerelict");
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

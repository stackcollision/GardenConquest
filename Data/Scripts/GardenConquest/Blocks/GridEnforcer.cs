using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Definitions;
using Sandbox.Common.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using Sandbox.Common.ObjectBuilders;
using VRage.Components;
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
			TOTAL_BLOCKS,
			BLOCK_TYPE,
			TOO_MANY_CLASSIFIERS,
			TOO_MANY_OF_CLASS,
			SHOULD_BE_STATIC
		}

		public struct VIOLATION {
			public VIOLATION_TYPE Type { get; set; }
			public BlockType BlockType { get; set; }
			public String Name { get; set; }
			public int Count { get; set; }
			public int Limit { get; set; }
		}

		private static readonly bool CLEANUP_LARGE_UNCLASSIFIED_IMMEDIATELY = true;
		private static readonly int CLEANUP_LARGE_UNCLASSIFIED_BLOCKS = 100;
		private static readonly int CLEANUP_CLASS_TICKS = 1; // So 30 min with default 30 min ticks
		private static readonly int CLEANUP_STATIC_TICKS = 96; //2 days
		private static readonly int CLEANUP_NOTIFY_WAIT = 5;
		private static readonly float CLEANUP_RATE = .25f;
		private static readonly HullClass.CLASS DEFAULT_CLASS = HullClass.CLASS.UNCLASSIFIED;

		private static ConquestSettings s_Settings;

		#endregion
		#region Class Members

		// Grid and blocks
		private IMyCubeGrid m_Grid = null;
		private bool m_GridSubscribed;
		private int m_BlockCount;
		private int[] m_BlockTypeCounts;
		private Dictionary<long, InGame.IMyProjector> m_Projectors;

		// Ownership
		private GridOwner m_Owner = null;
		private bool m_Supported;

		// Class
		// There are two different members for classification
		// The first is the class of the placed grid classifier (m_Classifier)
		// But if that block is incomplete/offline/damaged then the rules are
		// still applied as if it was UNCLASSIFIED
		private HullClassifier m_Classifier;
		private Dictionary<long, HullClassifier> m_ExtraClassifiers;
		private HullClass.CLASS m_ReservedClass;
		private HullClass.CLASS m_EffectiveClass;
		private HullRuleSet m_ReservedRules;
		private HullRuleSet m_EffectiveRules;

		// Cleanup
		private DateTime m_CleanupNotifyAfter;
		private List<VIOLATION> m_CurrentViolations;
		private DerelictTimer m_CleanupTimer;
		private int m_TooManyOfClassTicks = 0;
		private int m_ShouldBeStaticTicks = 0;

		// SE update flags
		private bool m_IsServer = false;
		private bool m_CheckServerLater = false;
		private bool m_BeyondFirst100 = false;
		private bool m_StateLoaded = false;
		private bool m_Merging = false;
		private bool m_CheckCleanupNextUpdate;
		private bool m_CheckOwnerNextUpdate;
		private bool m_NotifyViolationsNextUpdate;
		private bool m_DeleteNextUpdate;
		//private bool m_FinishDeleteNextUpdate;
		private bool m_MarkedForClose;

		// Utility
		private Logger m_Logger = null;

		public GridOwner Owner { get { return m_Owner; } }
		public HullClass.CLASS Class { get { return m_EffectiveClass; } }
		private List<long> BigOwners { get {
			try { return m_Grid.BigOwners; }
			catch {
				log("BigOwners called without ownership manager",
					"BigOwners", Logger.severity.WARNING);
				return new List<long>();
			}
		}}
		public IMyCubeGrid Grid { get { return m_Grid; } }
		public HullClassifier Classifier { get { return m_Classifier; } }
		public List<VIOLATION> Violations { get { return m_CurrentViolations; } }
		public int BlockCount { get { return m_BlockCount; } }
		public int TimeUntilCleanup { get {
			if (m_CleanupTimer == null) return -1;
			return m_CleanupTimer.SecondsRemaining;
		}}
		public bool SupportedByFleet { get { return m_Supported; } }
		// This is pretty dumb - it reports true if the grid has any projector blocks that are ON
		// There needs to be a Sandbox.Common.ModAPI.IMyProjectorBlock that exposes .StartProjecting and .StopProjecting,
		// or at least expose IsProjecting from Sandbox.ModAPI.Ingame.IMyProjector
		// But really the easiest way to fix this would be to only do placement restrictions when a character is actually placing
		// the block, and we know that's a big to-do
		private bool Projecting {
			get {
				foreach (KeyValuePair<long, InGame.IMyProjector> pair in m_Projectors) {
					if (pair.Value.IsWorking) {
						return true;
					}
				}
				return false;
			}
		}
		public static StateTracker StateTracker { get; private set; }

		#endregion
		#region Events

		// OnPlacementViolation
		private static Action<GridEnforcer, VIOLATION_TYPE> eventOnPlacementViolation;
		public static event Action<GridEnforcer, VIOLATION_TYPE> OnPlacementViolation {
			add { eventOnPlacementViolation += value; }
			remove { eventOnPlacementViolation -= value; }
		}
		private void notifyPlacementViolation(VIOLATION_TYPE violation) {
			if (eventOnPlacementViolation != null)
				eventOnPlacementViolation(this, violation);
		}

		// OnCleanupViolation
		private static Action<GridEnforcer, List<VIOLATION>> eventCleanupViolation;
		public static event Action<GridEnforcer, List<VIOLATION>> OnCleanupViolation {
			add { eventCleanupViolation += value; }
			remove { eventCleanupViolation -= value; }
		}
		private void notifyCleanupViolation(List<VIOLATION> violations) {
			if (eventCleanupViolation != null)
				eventCleanupViolation(this, violations);
		}

		// OnCleanupTimerStart
		private static Action<GridEnforcer, int> eventOnCleanupTimerStart;
		public static event Action<GridEnforcer, int> OnCleanupTimerStart {
			add { eventOnCleanupTimerStart += value; }
			remove { eventOnCleanupTimerStart -= value; }
		}
		private void notifyCleanupTimerStart(int secondsRemaining) {
			if (eventOnCleanupTimerStart != null)
				eventOnCleanupTimerStart(this, secondsRemaining);
		}

		// OnCleanupTimerEnd
		// This carries notifications for both CANCELLED and ELAPSED
		private static Action<GridEnforcer, DerelictTimer.COMPLETION> eventOnCleanupTimerEnd;
		public static event Action<GridEnforcer, DerelictTimer.COMPLETION> OnCleanupTimerEnd {
			add { eventOnCleanupTimerEnd += value; }
			remove { eventOnCleanupTimerEnd -= value; }
		}
		private void notifyCleanupTimerEnd(DerelictTimer.COMPLETION completionState) {
			if (eventOnCleanupTimerEnd != null)
				eventOnCleanupTimerEnd(this, completionState);
		}

		#endregion
		#region Class Lifecycle

		public GridEnforcer() {
			if (s_Settings == null) {
				s_Settings = ConquestSettings.getInstance();
			}
		}

		public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
			base.Init(objectBuilder);
			m_Grid = Container.Entity as IMyCubeGrid;

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
					m_Logger = null;
					m_Grid = null;
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
			m_BlockTypeCounts = new int[s_Settings.BlockTypes.Length];
			m_Owner = new GridOwner(this);
			m_ExtraClassifiers = new Dictionary<long, HullClassifier>();
			m_Projectors = new Dictionary<long, InGame.IMyProjector>();

			setReservedToDefault();
			setEffectiveToDefault();
			//log("setClassification" + m_IsServer, "Init");
			m_Owner.setClassification(m_EffectiveClass);
			//log("end setClassification" + m_IsServer, "Init");

			m_Grid.OnBlockAdded += blockAdded;
			m_Grid.OnBlockRemoved += blockRemoved;
			m_Grid.OnBlockOwnershipChanged += blockOwnerChanged;
			m_GridSubscribed = true;
		}

		public override void Close() {
			log("Grid closed", "Close");
			unServerize();
		}

		/// <summary>
		/// Removes hooks and references
		/// </summary>
		private void unServerize() {
			detatchGrid();
			detatchOwner();
			detatchClassifier(false);
			m_ExtraClassifiers = null;
			detatchCleanupTimer();
			m_Owner = null;
			m_Logger = null;
		}

		private void detatchGrid() {
			if (m_Grid != null) {
				if (m_GridSubscribed) {
					m_Grid.OnBlockAdded -= blockAdded;
					m_Grid.OnBlockRemoved -= blockRemoved;
					m_Grid.OnBlockOwnershipChanged -= blockOwnerChanged;
				}
				m_Grid = null;
			}
		}

		#endregion
		#region SE Hooks - Simulation

		public override void UpdateBeforeSimulation100() {
			// Must be server, not be closing
			// Must not be transparent - aka a new grid not yet placed
			if (!m_IsServer || m_MarkedForClose || m_Grid.MarkedForClose || m_Grid.Transparent)
				return;

			// Do we need to verify that we are the server?
			if (m_CheckServerLater && m_IsServer) {
				try {
					log("Late server check", "UpdateBeforeSimulation100");
					m_IsServer = Utility.isServer();
					log("Is server: " + m_IsServer, "Init");
					m_CheckServerLater = false;

					if (!m_IsServer) {
						unServerize();
						return;
					}
				} catch (NullReferenceException e) {
					// Continue thinking we are server for the time being
					// This shouldn't happen (lol)
					log("Exception checking if server: " + e, "UpdateBeforeSimulation100");
				}
			}

			// = Main update logic
			try {
				// if cleanup previously marked this grid for deletion, do it and get us out of here
				if (m_DeleteNextUpdate) {
					m_DeleteNextUpdate = false;
					log("deleting all blocks and closing grid", "UpdateBeforeSimulation100");
					removeAllBlocks();
					m_MarkedForClose = true;
					return;
				}

				// clear flags not used in updates
				// when initing or merging, if any blocks are added they will flag m_CheckCleanup,
				// but blockAdded uses these flags to know it must allow any block through temporarily
				if (!m_BeyondFirst100) {
					m_BeyondFirst100 = true;
				}
				if (m_Merging) {
					m_Merging = false;
				}

				// If we're missing State data, try to get it
				if (!m_StateLoaded) {
					StateTracker = StateTracker.getInstance();

					if (StateTracker != null) {
						// Load state-dependent things
						m_StateLoaded = true;
					}
				}

				// check for failed derelict timers
				if (m_CleanupTimer != null) {
					m_CleanupTimer.updateTimeRemaining();
				}

				// Update ownership
				if (m_CheckOwnerNextUpdate || m_CheckCleanupNextUpdate) {
					m_CheckOwnerNextUpdate = false;

					reevaluateOwnership();
				}

				// Update cleanup state - violations & timers
				if (m_CheckCleanupNextUpdate) {
					log("checking cleanup state due to flag", "UpdateBeforeSimulation100");
					m_CheckCleanupNextUpdate = false;

					updateViolations();
					updateCleanupTimers();
				}

				// Do cleanup if needed
				if (m_CleanupTimer != null && m_CleanupTimer.TimerExpired) {
					log("timer expired, running cleanup", "UpdateBeforeSimulation100");

					doCleanupPhase();
					if (m_DeleteNextUpdate) return;
					m_CheckCleanupNextUpdate = true;
				}

			} catch (Exception e) {
				log("Exception occured: " + e, "UpdateBeforeSimulation100", Logger.severity.ERROR);
			}
		}

		/// <summary>
		/// Marks the grid to skip rule enforcement for the next few frames because of a grid merge.
		/// </summary>
		public void markForMerge() {
			log("This grid is having another merged into it", "markForMerge");
			m_Merging = true;
		}

		#endregion
		#region SE Hooks - Block Added

		/// <summary>
		/// Called when a block is added to the grid.
		/// Decides whether or not to allow the block to be placed.
		/// Increments counts, checks for classification, and sets
		/// a flag to refresh cleanup status
		/// </summary>
		/// <param name="added">block that was added to the grid</param>
		private void blockAdded(IMySlimBlock added) {
			//log(added.ToString() + " added to grid " + m_Grid.DisplayName, "blockAdded");

			try {
				// = update block counts and grid state, get violations
				//
				// Block counts must be updated whether we're removing the block or not,
				// because blockRemoved has no way of knowing if we touched counts for it

				// update classification first since this influences limits
				bool classified;
				VIOLATION_TYPE classifierViolation = updateClassificationWith(added, out classified);

				// update total count
				VIOLATION_TYPE totalBlocksViolation = incrementTotalBlocks(classified);

				// update type counts
				List<BlockType> violatedTypes = updateBlockTypeCountsWith(added);

				// update projectors
				// track these outside of block types, because those are currently user-configurable
				InGame.IMyProjector projector = added.FatBlock as InGame.IMyProjector;
				if (projector != null) {
					log("Added a projector", "blockAdded");
					m_Projectors.Add(projector.EntityId, projector);
				}

				// = If there are violations, remove the block and notify the appropriate parties

				// we skip violation checks for blocks being added that aren't actually placed by a user,
				// i.e. during World Load or a Merge
				// we clean those up over time with Cleanup
				if (classified || m_Merging || !m_BeyondFirst100 || Projecting) {
					log("Currently merging, initing, classifying, or projecting. Must allow.", "blockAdded");
					m_CheckCleanupNextUpdate = true;
					goto Allowed;
				}

				// if violations, notify and deny
				if (classifierViolation != VIOLATION_TYPE.NONE) {
					log("classifierViolation", "blockAdded");
					notifyPlacementViolation(classifierViolation);
					goto Denied;
				}
				else if (totalBlocksViolation != VIOLATION_TYPE.NONE) {
					if (providesNeededPower(added)) {
						// note this is not perfect because it allows people to stack
						// infintely many reactors in this specific situation, but that's not
						// really prone to exploitation...
						log("too many blocks but provides needed power, must allow", "blockAdded");
						goto Allowed;
					}
					log("totalBlocksViolation", "blockAdded");
					notifyPlacementViolation(totalBlocksViolation);
					goto Denied;
				}
				else if (violatedTypes.Count > 0) {
					log("block type violation", "blockAdded");
					notifyPlacementViolation(VIOLATION_TYPE.BLOCK_TYPE);
					goto Denied;
				}
			}
			catch (Exception e) {
				log("Error: " + e, "blockAdded");
			}

		Allowed:
			log(added.ToString() + " added to grid '" + m_Grid.DisplayName +
				"'. Total Count now: " + m_BlockCount, "blockAdded");
			m_CheckOwnerNextUpdate = true;
			m_CheckCleanupNextUpdate = true; // temporarily doing this on block add too, let's see if it's ok
			return;

		Denied:
			log(added.ToString() + " denied for grid '" + m_Grid.DisplayName +
				"'. Total Count now: " + m_BlockCount, "blockAdded");

			removeBlock(added);
		}

		/// <summary>
		/// Applies the new class if block is a Classifier and we can reserve the Class
		/// Returns true if this was a happy update,
		/// false if the block needs to be removed
		/// </summary>
		/// <param name="block"></param>
		private VIOLATION_TYPE updateClassificationWith(IMySlimBlock block, out bool applied) {
			//log("", "updateClassificationWith");
			try {
				// If it's not a classifier, we don't care about it
				if (!HullClassifier.isClassifierBlock(block)) {
					applied = false;
					return VIOLATION_TYPE.NONE;
				}

				// load up the classifier helper object
				HullClassifier classifier = new HullClassifier(block);
				HullClass.CLASS classID = classifier.Class;
				log("Adding a classifier for class " + classID + " - " +
					s_Settings.HullRules[(int)classID].DisplayName,
					"updateClassificationWith");

				// if we're initializing or merging this grid, the block must be placed,
				// so determine which classifier to use. The others will be cleaned up later
				if ((!m_BeyondFirst100 || m_Merging)) {
					log("init/merge, must add it", "updateClassificationWith");

					if (m_Classifier == null) {
						log("new classifier", "updateClassificationWith");
						goto Reserve;
					}

					if (classID > m_ReservedClass) {
						log("better than what we have", "updateClassificationWith");
						m_ExtraClassifiers.Add(m_Classifier.FatBlock.EntityId, m_Classifier);
						unsetClassifier();
						goto Reserve;
					}

					log("extra classifier", "updateClassificationWith");
					m_ExtraClassifiers.Add(classifier.FatBlock.EntityId, classifier);
					applied = false;
					return VIOLATION_TYPE.NONE;
				}

				// Ensure it's the right type for this grid
				if (s_Settings.HullRules[(int)classifier.Class].ShouldBeStation && !m_Grid.IsStatic) {
					applied = false;
					return VIOLATION_TYPE.SHOULD_BE_STATIC;
				}

				// Two classifiers not allowed
				//log("Existing classifier? " + (m_Classifier != null), "updateClassificationWith");
				if (m_Classifier != null) {
					log("Too many classifiers", "updateClassificationWith");
					applied = false;
					return VIOLATION_TYPE.TOO_MANY_CLASSIFIERS;
				}

				// Too many per Player/Faction not allowed
				bool fleetAllows = checkClassAllowed(classID);
				//log("Too many per Player/Faction? " + !fleetAllows, "updateClassificationWith");
				if (!fleetAllows) {
					log("Too many of this class for this owner", "updateClassificationWith");
					applied = false;
					return VIOLATION_TYPE.TOO_MANY_OF_CLASS;
				}

			Reserve:
				log("Applying classifier", "updateClassificationWith");
				setClassifier(classifier);

				// let block enforcement know to let this thing through
				applied = true;

				// the rules of the new class might be broken by existing blocks,
				// check next update
				m_CheckCleanupNextUpdate = true;
			}
			catch (Exception e) {
				log("Error: " + e, "updateClassificationWith", Logger.severity.ERROR);
				applied = false;
			}

			return VIOLATION_TYPE.NONE;
		}

		/// <summary>
		/// Adds 1 to total block count
		/// returns a violation if over the limit
		/// </summary>
		private VIOLATION_TYPE incrementTotalBlocks(bool classified) {
			m_BlockCount++;
			//log("new count " + m_BlockCount, "incrementTotalBlocks");
			//log("m_EffectiveRules.DisplayName " + m_EffectiveRules.DisplayName, "incrementTotalBlocks");
			//log("m_EffectiveRules.MaxBlocks " + m_EffectiveRules.MaxBlocks, "incrementTotalBlocks");

			if (m_BlockCount > m_EffectiveRules.MaxBlocks) {

				// let applied classifiers through this limit
				if (classified) {
					return VIOLATION_TYPE.NONE;
				}
				return VIOLATION_TYPE.TOTAL_BLOCKS;
			}

			return VIOLATION_TYPE.NONE;
		}

		/// <summary>
		/// Increments the BlockType counts for a given new block
		/// Returns the violated limits if any
		/// </summary>
		private List<BlockType> updateBlockTypeCountsWith(IMySlimBlock block) {
			List<BlockType> violated_types = new List<BlockType>();

			BlockType[] types = s_Settings.BlockTypes;
			for (int typeID = 0; typeID < types.Length; typeID++) {
				BlockType type = types[typeID];

				if (type.appliesToBlock(block)) {
					//log("incrementing type count " + m_BlockTypeCounts[typeID], "updateBlockCountsWith");
					m_BlockTypeCounts[typeID] = m_BlockTypeCounts[typeID] + 1;
					int count = m_BlockTypeCounts[typeID];
					int limit = m_EffectiveRules.BlockTypeLimits[typeID];
					log(type.DisplayName + " count now: " + count, "updateBlockCountsWith");

					if (count > limit && limit >= 0)
						violated_types.Add(type);
				}
			}

			return violated_types;
		}

		/// <summary>
		/// Certain classes have count limitations.  Check if this faction can have any more
		/// of this class.
		/// </summary>
		/// <param name="c">Class to check</param>
		/// <returns>True if the class is allowed</returns>
		private bool checkClassAllowed(HullClass.CLASS c) {
			log("checking if fleet can support more of " + c, "checkClassAllowed");
			// update ownership/fleet now to get the player the most up-to-date info
			reevaluateOwnership();
			if (m_Owner.OwnerType == GridOwner.OWNER_TYPE.UNOWNED) {
				// people find it really annoying that they have to have an owned block before placing a classifier.
				// Fleet Support will still take care of this for us via cleanup down the line.
				// Players would need to look at their fleet and notice this isn't there to know it's still under cleanup,
				// because it wouldn't be listed in their violations
				log("No owner, thus no way to tell if too many of class, but allowing for convenience",
					"checkClassAllowed");
				notifyPlacementViolation(VIOLATION_TYPE.TOO_MANY_OF_CLASS);
				return true;
			}

			return m_Owner.Fleet.canSupportAnother(c);
		}

		private bool providesNeededPower(IMySlimBlock block) {
			if (m_EffectiveClass == HullClass.CLASS.UNCLASSIFIED &&
				m_ReservedClass != HullClass.CLASS.UNCLASSIFIED) {
				IMyReactor reactor = block.FatBlock as IMyReactor;
				return (reactor != null);
			}
			return false;
		}

		#endregion
		#region SE Hooks - Block Removed

		/// <summary>
		/// Called when a block is removed.  Decrements counts and checks for declassification.
		/// </summary>
		/// <param name="removed"></param>
		private void blockRemoved(IMySlimBlock removed) {
			m_BlockCount--;
			log(removed.ToString() + " removed from grid '" + m_Grid.DisplayName +
				"'. Total Count now: " + m_BlockCount, "blockRemoved");

			updateClassificationWithout(removed);
			updateBlockTypeCountsWithout(removed);

			// update projectors
			// track these outside of block types, because those are currently user-configurable
			InGame.IMyProjector projector = removed.FatBlock as InGame.IMyProjector;
			if (projector != null) {
				log("Removed a projector", "blockRemoved");
				m_Projectors.Remove(projector.EntityId);
			}

			m_CheckCleanupNextUpdate = true;
		}

		/// <summary>
		/// Removes the classifier and reserved class if block is a Classifier
		/// </summary>
		private void updateClassificationWithout(IMySlimBlock block) {
			// Not a classifier?
			if (!HullClassifier.isClassifierBlock(block))
				return;

			log("removing classifier", "updateClassificationWithout");

			// Not in use?
			if (m_Classifier == null || !m_Classifier.SlimBlock.Equals(block)) {
				log("wasn't in use, removing from extra classifiers list", "updateClassificationWithout");
				m_ExtraClassifiers.Remove(block.FatBlock.EntityId);
				return;
			}

			log("in use, unsetting", "updateClassificationWithout");
			//demoteClass(); // would have happened before
			unsetClassifier();

			//log("looking for existing alternatives", "updateClassificationWithout");
			long bestClassifierID = 0;
			HullClass.CLASS hc = HullClass.CLASS.UNCLASSIFIED;
			HullClass.CLASS bestClassAvailable = HullClass.CLASS.UNCLASSIFIED;
			foreach (KeyValuePair<long, HullClassifier> entry in m_ExtraClassifiers) {
				hc = entry.Value.Class;
				if (hc > bestClassAvailable) {
					bestClassifierID = entry.Key;
					bestClassAvailable = hc;
				}
			}

			if (bestClassAvailable > HullClass.CLASS.UNCLASSIFIED) {
				log("found an alternative existing classifier", "updateClassificationWithout");
				setClassifier(m_ExtraClassifiers[bestClassifierID]);
				m_ExtraClassifiers.Remove(bestClassifierID);
				// existing classifiers might already be Working
				classifierWorkingChanged(m_Classifier.FatBlock);
			}

			m_CheckCleanupNextUpdate = true;
		}

		/// <summary>
		/// Decrements the BlockType counts for a given removed block
		/// </summary>
		private void updateBlockTypeCountsWithout(IMySlimBlock block) {
			BlockType[] types = s_Settings.BlockTypes;
			for (int i = 0; i < types.Length; i++) {
				BlockType type = types[i];
				if (type.appliesToBlock(block)) {
					m_BlockTypeCounts[i]--;
					log(type.DisplayName + " count now: " + m_BlockTypeCounts[i], "updateBlockCountsWithout");
					return;
				}
			}
		}

		#endregion
		#region SE Hooks - Block Owner Changed

		/// <summary>
		/// Called when someone adds or removes any FatBlock
		/// or if the ownership on a Fatblock changes
		/// </summary>
		/// <remarks>
		/// Testing indicates this is only called once per grid, even if you change 50 blocks at once
		/// </remarks>
		/// <param name="changed"></param>
		private void blockOwnerChanged(IMyCubeGrid changed) {
			log("flagging for ownership check next update",
				"blockOwnerChanged", Logger.severity.TRACE);
			m_CheckOwnerNextUpdate = true;
		}

		#endregion
		#region Utility - RemoveBlock

		/// <summary>
		/// Forces a block to be removed, usually in the case of a rule violation.
		/// </summary>
		/// <param name="b"></param>
		private void removeBlock(IMySlimBlock b) {
			// spawn materials in place so they're not lost

			// TODO: determine best inventory target
			//IMyInventory inventory = null;

			// WAITING: once Keen accepts this PR
			// https://github.com/KeenSoftwareHouse/SpaceEngineers/pull/52
			// the below will spawn materials in the inventory if it exists and has space,
			// or otherwise as floating objects in space,
			//b.FullyDismount(inventory);
			//b.MoveItemsFromConstructionStockpile(inventory);
			//b.SpawnConstructionStockpile();

			m_Grid.RemoveBlock(b, true);
		}

		private void removeAllBlocks() {
			log("trying...", "removeAllBlocks");
			try {
				List<IMySlimBlock> allBlocks = new List<IMySlimBlock>();
				m_Grid.GetBlocks(allBlocks);
				foreach (IMySlimBlock block in allBlocks) {
					removeBlock(block);
				}
			}
			catch (Exception e) {
				log("Exception " + e, "removeAllBlocks", Logger.severity.ERROR);
			}
			log("finished", "removeAllBlocks");
		}
		#endregion
		#region Class

		private void setReservedToDefault() {
			m_ReservedClass = DEFAULT_CLASS;
			m_ReservedRules = s_Settings.HullRules[(int)DEFAULT_CLASS];
		}

		private void setReservedToClassifier() {
			m_ReservedClass = m_Classifier.Class;
			m_ReservedRules = s_Settings.HullRules[(int)m_ReservedClass];
			log("Reserved class changed to" + m_ReservedRules.DisplayName, "setReservedToClassifier");
		}

		private void setEffectiveToDefault() {
			m_EffectiveClass = DEFAULT_CLASS;
			m_EffectiveRules = s_Settings.HullRules[(int)DEFAULT_CLASS];
		}

		private void setEffectiveToReserved() {
			m_EffectiveClass = m_ReservedClass;
			m_EffectiveRules = s_Settings.HullRules[(int)m_EffectiveClass];
		}

		#endregion
		#region Classifier

		private void attachClassifier(HullClassifier classifier) {
			m_Classifier = classifier;
			m_Classifier.FatBlock.IsWorkingChanged += classifierWorkingChanged;
		}

		private void detatchClassifier(bool expectSet = true) {
			//log("start", "detatchClassifier", Logger.severity.TRACE);
			if (m_Classifier != null) {
				log("detaching", "detatchClassifier");
				m_Classifier.FatBlock.IsWorkingChanged -= classifierWorkingChanged;
				m_Classifier = null;
				//log("m_Classifier == null " + (m_Classifier = null), "detatchClassifier", Logger.severity.TRACE);
			}
			else if (expectSet) {
				log("failed to detatch, wasn't stored", "detatchClassifier", Logger.severity.ERROR);
			}
			//log("end", "detatchClassifier", Logger.severity.TRACE);
		}

		private void setBestClassifier() {
			if (m_Classifier != null) {
				log("m_Classifier is still set! Aborting.",
					"setBestClassifie", Logger.severity.ERROR);
				return;
			}

			// find the best classifier on grid
			HullClassifier bestClassifier = null;
			HullClassifier classifier;
			List<IMySlimBlock> allBlocks = new List<IMySlimBlock>();
			m_Grid.GetBlocks(allBlocks);
			foreach (IMySlimBlock block in allBlocks) {
				if (HullClassifier.isClassifierBlock(block)) {
					classifier = new HullClassifier(block);
					if (bestClassifier == null || classifier.Class > bestClassifier.Class) {
						bestClassifier = classifier;
					}
				}
			}

			// use it
			if (bestClassifier != null) {
				setClassifier(bestClassifier);
			}
		}

		private void removeExtraClassifiers(int removeCount = 100) {
			// Supposedly we've only added the best classifier up to now,
			// so all we need to check for is unused classifiers

			if (m_Classifier == null) {
				log("m_Classifier is not set! Aborting.",
					"removeExtraClassifiers", Logger.severity.ERROR);
				return;
			}

			List<IMySlimBlock> allBlocks = new List<IMySlimBlock>();
			m_Grid.GetBlocks(allBlocks);
			foreach (IMySlimBlock block in allBlocks) {
				if (removeCount > 0) {
					if (HullClassifier.isClassifierBlock(block) &&
						!block.Equals(Classifier.SlimBlock)) {
						removeBlock(block);
						removeCount--;
					}
				} else {
					return;
				}
			}
		}

		private void setClassifier(HullClassifier classifier) {
			if (m_Classifier != null) {
				log(" existing classifier is still set, skipping",
					"unsetClassifier", Logger.severity.ERROR);
				return;
			}

			attachClassifier(classifier);
			setReservedToClassifier();
			// promotion will be taken care of when the block starts working
			// Working Blocks added on init have IsWorkingChanged triggered after
			// Working blocks added during a merge don't, so go check now
			if (m_Merging) {
				classifierWorkingChanged(m_Classifier.FatBlock);
			}
			m_CheckCleanupNextUpdate = true;
		}

		private void unsetClassifier() {
			log("Removing reserved " + m_ReservedRules.DisplayName + " classifier", "unsetClassifier");
			if (m_Classifier == null) {
				log("m_Classifier is null",
					"unsetClassifier", Logger.severity.WARNING);
				return;
			}

			setReservedToDefault();
			demoteClass();
			detatchClassifier();
		}

		/// <summary>
		/// Promotes the grid's effective class to its reserved class
		/// </summary>
		private void promoteClass() {
			log("Promoting Effective class to " + m_ReservedClass + " from " + m_EffectiveClass, "promoteClass");
			setEffectiveToReserved();
			m_Owner.setClassification(m_EffectiveClass);
			m_CheckCleanupNextUpdate = true;
		}

		/// <summary>
		/// Sets the effective class back to the default
		/// A beacon must be working to have its rules applied
		/// </summary>
		private void demoteClass() {
			log("Returning Effective class to " + DEFAULT_CLASS + " from " + m_EffectiveClass, "demoteClass");
			setEffectiveToDefault();
			m_Owner.setClassification(m_EffectiveClass);
			m_CheckCleanupNextUpdate = true;
		}

		#endregion
		#region Ownership & Fleet

		/// <summary>
		/// Figures out who owns this grid and updates their fleet
		/// There's no way for us to hook into Player faction changes,
		/// so we call this before anything important runs that relies
		/// on fleet data, and after someone adds/removes a block.
		/// </summary>
		/// <returns>Whether or not the ownership changed.</returns>
		public bool reevaluateOwnership() {
			//log("", "reevaluateOwnership", Logger.severity.TRACE);

			bool changed = m_Owner.reevaluateOwnership(BigOwners);

			if (changed) {
				m_CheckCleanupNextUpdate = true;
				log("owner changed", "reevaluateOwnership");
			}
			else {
				//log("no change", "reevaluateOwnership");
			}

			return changed;
		}

		private void detatchOwner() {
			if (m_Owner != null) {
				log("detatching owner", "detatchOwner");
				m_Owner.Close();
				m_Owner = null;
			}
		}

		public void markSupported(long fleetOwnerID) {
			// we may want this later if we need to lookup a fleet ?
			//m_FleetOwnerID = fleetOwnerID;
			m_Supported = true;
		}

		public void markUnsupported(long fleetOwnerID) {
			//m_FleetOwnerID = fleetOwnerID;
			m_Supported = false;
		}

		#endregion
		#region Cleanup

		/// <summary>
		/// Checks if the grid complies with the rules.
		/// Returns all violated rules
		/// </summary>
		private List<VIOLATION> currentViolations() {
			//log("", "currentViolations", Logger.severity.TRACE);
			List<VIOLATION> violations = new List<VIOLATION>();
			//log("starting", "checkRules");

			if (m_EffectiveRules == null) {
				log("m_EffectiveRules not set!", "currentViolations");
				return violations;
			}

			// class count violations are handled by the fleet
			if (!m_Supported) {
				violations.Add(new VIOLATION() {
					Type = VIOLATION_TYPE.TOO_MANY_OF_CLASS,
					Name = "Total of this Class",
					Count = (int)Owner.Fleet.countClass(m_EffectiveClass),
					Limit = (int)Owner.Fleet.maxClass(m_EffectiveClass),
				});
			}

			// total block violations
			if (m_BlockCount > m_EffectiveRules.MaxBlocks)
				violations.Add(new VIOLATION() {
					Type = VIOLATION_TYPE.TOTAL_BLOCKS,
					Name = "Total Blocks",
					Count = m_BlockCount,
					Limit = m_EffectiveRules.MaxBlocks,
					//Diff = m_BlockCount - m_EffectiveRules.MaxBlocks,
				});

			// block type violations
			BlockType[] types = s_Settings.BlockTypes;
			int[] limits = m_EffectiveRules.BlockTypeLimits;
			int[] counts = m_BlockTypeCounts;
			for (int typeID = 0; typeID < types.Length; typeID++) {
				BlockType type = types[typeID];
				int count = counts[typeID];
				int limit = limits[typeID];

				if (count > limit && limit >= 0) {
					violations.Add(new VIOLATION() {
						Type = VIOLATION_TYPE.BLOCK_TYPE,
						BlockType = type,
						Name = type.DisplayName,
						Count = count,
						Limit = limit,
						//Diff = count - limit,
					});
				}
			}

			// too many classifiers violation
			int extraClassifiersCount = m_ExtraClassifiers.Count;
			if (extraClassifiersCount > 0) {
				violations.Add(new VIOLATION() {
					Type = VIOLATION_TYPE.TOO_MANY_CLASSIFIERS,
					Name = "Hull Classifiers",
					Count = extraClassifiersCount,
					Limit = 1
				});
			}

			// not-a-station violation
			// This is only detected on block add/remove, it would be nice if we could listen
			// for grid.IsStatic changes, but that needs to be pr'd into core
			if (m_EffectiveRules.ShouldBeStation && !m_Grid.IsStatic) {
				violations.Add(new VIOLATION() {
					Type = VIOLATION_TYPE.SHOULD_BE_STATIC,
					Name = "Should be a Station"
				});
			}

			// log violations
			String violation_log_descr;
			foreach (VIOLATION v in violations) {
				violation_log_descr = v.Name;
				if (v.Type == VIOLATION_TYPE.BLOCK_TYPE)
					violation_log_descr += " " + v.BlockType.DisplayName;

				log(String.Format("found violation {0} - {1}/{2}",
					violation_log_descr, v.Count, v.Limit),
					"currentViolations");
			}

			return violations;
		}

		private void updateViolations() {
			log("", "updateViolations", Logger.severity.TRACE);
			//reevaluateOwnership(); // done in simulation
			m_CurrentViolations = currentViolations();
		}

		/// <summary>
		/// When the timer expires this does a cleanup pass on the grid
		/// Removes a portion of its offending blocks and restarts the timer if
		/// violations remain
		/// </summary>
		private void doCleanupPhase() {
			log("start", "doCleanupPhase");

			if (m_CleanupTimer == null) {
				log("m_CleanupTimer not set!",
					"doCleanupPhase", Logger.severity.ERROR);
				return;
			}
			if (m_CurrentViolations == null) {
				log("m_EffectiveRules not set!",
					"doCleanupPhase", Logger.severity.ERROR);
				return;

			}

			// = decompose existing violation data from list
			// we have to go through these in a set order
			// todo - maybe we should store these as separate data-points on the object?
			//   the violations list format is mainly useful for passing to the notification, maybe should do there instead
			//   could still have a helper-property to gather them all together into a list
			//VIOLATION totalViolation;
			int totalToRemove = 0;
			//VIOLATION tooManyClassifiersViolation;
			int classifierCountToRemove = 0;
			//VIOLATION tooManyOfClassViolation;
			bool removeClass = false;
			//VIOLATION shouldBeStaticViolation;
			bool shouldBeStatic = false;

			BlockType[] types = s_Settings.BlockTypes;
			List<VIOLATION> typeViolations = new List<VIOLATION>();
			int[] typeRemoveCounts = new int[types.Length];

			foreach (VIOLATION v in m_CurrentViolations) {
				switch (v.Type) {
					case VIOLATION_TYPE.BLOCK_TYPE:
						int typeID = s_Settings.blockTypeID(v.BlockType);
						typeRemoveCounts[typeID] = phasedRemoveCount(v);
						break;
					case VIOLATION_TYPE.TOO_MANY_CLASSIFIERS:
						classifierCountToRemove = phasedRemoveCount(v);
						break;
					case VIOLATION_TYPE.SHOULD_BE_STATIC:
						shouldBeStatic = phasedRemoveCount(v) == 1;
						break;
					case VIOLATION_TYPE.TOO_MANY_OF_CLASS:
						removeClass = phasedRemoveCount(v) == 1;
						break;
					case VIOLATION_TYPE.TOTAL_BLOCKS:
						totalToRemove = phasedRemoveCount(v);
						break;
				}
			}


			// unsupported classifier
			if (removeClass) {
				log("class is unsupported, decrement it to any other existing classifiers or remove entirely",
					"doCleanupPhase", Logger.severity.TRACE);
				// if it's unclassified, it wouldn't have one. We just need to delete instead
				if (m_ReservedClass == DEFAULT_CLASS) {
					// if this class isn't allowed, we must remove it
					log("class is " + m_ReservedClass + ", remove it", "doCleanupPhase", Logger.severity.TRACE);

					// dumb detection for exploration ships, remove immediately
					if (CLEANUP_LARGE_UNCLASSIFIED_IMMEDIATELY &&
						m_BlockCount > CLEANUP_LARGE_UNCLASSIFIED_BLOCKS) {
							totalToRemove = m_BlockCount;
					}
					else {
						// pretend block limit is 0 and use that cleanup
						// having a Unsupported Unclassified grid is equivalent to having one with zero allowed blocks
						totalToRemove = phasedRemoveCount(new VIOLATION {
							Type = VIOLATION_TYPE.TOTAL_BLOCKS,
							Count = m_BlockCount,
							Limit = 0
						});
					}

				} else {
					log("class is set, try to use a lower classifier", "doCleanupPhase", Logger.severity.TRACE);
					IMySlimBlock classifierBlock = m_Classifier.SlimBlock;
					m_ExtraClassifiers.Add(m_Classifier.FatBlock.EntityId, m_Classifier);
					unsetClassifier();
					removeBlock(classifierBlock);
					setBestClassifier();
				}

				// too many classifiers, only done if we didn't do the above b/c it affects the cached limit
			} else if (classifierCountToRemove > 0) {
				log("too many classifiers, remove " + classifierCountToRemove, "doCleanupPhase", Logger.severity.TRACE);
				removeExtraClassifiers(classifierCountToRemove);
			}

			// should be static
			if (shouldBeStatic) {
				// there's no way to make it static via existing function (or even via control panel)
				// we could spawn a station next to it with a merge block and hope we can get them to merge,
				// that would make it static. But sounds hard, let's just remove blocks for now and make the user
				// take care of this
				log("should be static, remove it", "doCleanupPhase", Logger.severity.TRACE);

				totalToRemove = phasedRemoveCount(new VIOLATION {
					Type = VIOLATION_TYPE.TOTAL_BLOCKS,
					Count = m_BlockCount,
					Limit = 0
				});
			}

			// type violations
			log("go through all blocks and remove type violations", "doCleanupPhase", Logger.severity.TRACE);
			List<IMySlimBlock> allBlocks = new List<IMySlimBlock>();
			m_Grid.GetBlocks(allBlocks);
			allBlocks.Reverse(); // remove most recently added blocks
			int numToRemove;
			foreach (IMySlimBlock block in allBlocks) {
				for (int typeID = 0; typeID < types.Length; typeID++) {
					numToRemove = typeRemoveCounts[typeID];
					if (numToRemove > 0) {
						BlockType type = types[typeID];
						if (type.appliesToBlock(block)) {
							removeBlock(block);
							typeRemoveCounts[typeID]--;
							totalToRemove--;
						}
					}
				}
			}

			// if we still have too many blocks, remove what's left
			// todo - remove blocks on the extremities of the grid first to avoid
			// breaking it into multiple grids, target things that take CPU but aren't
			// valuable first
			// note: special treatment for the last block, we need to close the grid
			allBlocks.Clear();
			log("go through all blocks and remove total violations", "doCleanupPhase", Logger.severity.TRACE);
			if (totalToRemove > 0) {
				m_Grid.GetBlocks(allBlocks);
				int blocksRemaining = allBlocks.Count;
				allBlocks.Reverse(); // remove most recently added blocks

				log("removing " + totalToRemove + " of " + blocksRemaining + " blocks",
					"doCleanupPhase");

				if (blocksRemaining > 1) {
					foreach (IMySlimBlock block in allBlocks) {
						if (totalToRemove > 0 && blocksRemaining > 1) {
							removeBlock(block);
							totalToRemove--;
							blocksRemaining--;
						}
						else {
							break;
						}
					}
				}

				if (totalToRemove > 0) {
					log("marking grid for deletion", "doCleanupPhase", Logger.severity.TRACE);
					m_DeleteNextUpdate = true;
				}
			}

			elapseCleanupTimer();

			m_CheckCleanupNextUpdate = true;
			log("done", "doCleanupPhase", Logger.severity.TRACE);
		}

		private int phasedRemoveCount(VIOLATION v) {
			VIOLATION_TYPE type = v.Type;
			if (type == VIOLATION_TYPE.BLOCK_TYPE ||
				type == VIOLATION_TYPE.TOTAL_BLOCKS ||
				type == VIOLATION_TYPE.TOO_MANY_CLASSIFIERS) {
				return (int)Math.Ceiling((float)(v.Count - v.Limit) * CLEANUP_RATE);

			} else if (type == VIOLATION_TYPE.TOO_MANY_OF_CLASS) {
				m_TooManyOfClassTicks++;
				if (m_TooManyOfClassTicks >= CLEANUP_CLASS_TICKS) {
					m_TooManyOfClassTicks = 0;
					return 1;
				}
				return 0;
			} else if (type == VIOLATION_TYPE.SHOULD_BE_STATIC) {
				m_ShouldBeStaticTicks++;
				if (m_ShouldBeStaticTicks >= CLEANUP_STATIC_TICKS) {
					m_ShouldBeStaticTicks = 0;
					return 1;
				}
				return 0;
			}

			return 0;
		}

		/// <summary>
		/// </summary>
		private void notifyViolations() {
			//log("start", "notifyViolations");

			if (m_CurrentViolations.Count > 0) {
				DateTime now = DateTime.Now;
				if (now > m_CleanupNotifyAfter) {
					log("sending notification", "notifyViolations");
					notifyCleanupViolation(m_CurrentViolations);
					m_CleanupNotifyAfter = now.AddSeconds(CLEANUP_NOTIFY_WAIT);
				}
			}
		}

		#endregion
		#region Timers

		/// <summary>
		/// Ensure Cleanup is running if we're violating rules
		/// Ensure it's stopped if we're not
		/// </summary>
		private void updateCleanupTimers(bool notifyStarted = true) {
			//log("start", "updateCleanup");

			if (m_CurrentViolations.Count > 0) {
				if (m_CleanupTimer == null) {
					log("starting timer", "updateCleanup");
					startCleanupTimer(notifyStarted);
				}
			}
			else {
				if (m_CleanupTimer != null) {
					log("no violations, cancelling timer", "updateCleanup");
					cancelCleanupTimer();
				}
			}
		}

		/// <summary>
		/// Starts the timer and alerts the player
		/// </summary>
		private void startCleanupTimer(bool notify = true) {
			//log("", "startCleanupTimer");
			// Don't start a second timer
			if (m_CleanupTimer != null) {
				log("already running", "startCleanupTimer");
				return;
			}

			m_CleanupTimer = new DerelictTimer(m_Grid);
			log("new timer", "startCleanupTimer");
			if (m_CleanupTimer.start() && notify) {
				log("notifying", "startCleanupTimer");
				notifyCleanupTimerStart(m_CleanupTimer.SecondsRemaining);
			}
		}

		/// <summary>
		/// If the rules are met before the timer expires this cancels the timer
		/// </summary>
		private void cancelCleanupTimer(bool notify = true) {
			log("start", "cancelCleanupTimer", Logger.severity.TRACE);
			if (m_CleanupTimer != null) {
				log("not null, cancelling", "cancelCleanupTimer", Logger.severity.TRACE);
				if (m_CleanupTimer.cancel()) {
					log("successuflly cancelled", "cancelCleanupTimer", Logger.severity.TRACE);

					if (notify) {
						notifyCleanupTimerEnd(DerelictTimer.COMPLETION.CANCELLED);
						log("notify complete", "cancelCleanupTimer", Logger.severity.TRACE);
					}

				}
				m_CleanupTimer = null;

			}
			log("done", "cancelCleanupTimer", Logger.severity.TRACE);
		}

		private void elapseCleanupTimer() {
			m_CleanupTimer = null;
			log("cleanup timer set to null", "doCleanupPhase");
			log("notifyCleanupTimerEnd", "doCleanupPhase");
			notifyCleanupTimerEnd(DerelictTimer.COMPLETION.ELAPSED);
		}

		private void detatchCleanupTimer() {
			if (m_CleanupTimer != null) {
				cancelCleanupTimer(false);
				m_CleanupTimer = null;
			}
		}

		#endregion
		#region SE Hooks - Classifier Working

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

		#endregion
		#region Utility - General

		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false) {
			return Container.Entity.GetObjectBuilder();
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}

		#endregion
	}
}

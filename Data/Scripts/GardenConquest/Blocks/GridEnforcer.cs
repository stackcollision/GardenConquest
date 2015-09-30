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
		public struct GridData {
			public bool supported { get; set; }
			public long shipID { get; set; }
			public HullClass.CLASS shipClass { get; set; }
			public string shipName { get; set; }
			public int blockCount { get; set; }
			public bool displayPos { get; set; }
			public VRageMath.Vector3D shipPosition { get; set; }
		}

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

		private static readonly bool CLEANUP_PREFABS_IMMEDIATELY = false;
		private static readonly int PREFAB_BLOCK_THRESHOLD = 200;
		private static readonly int CLEANUP_CLASS_TICKS = 1; // So 30 min with default 30 min ticks
		private static readonly int CLEANUP_STATIC_TICKS = 96; // 2 days
		private static readonly int CLEANUP_NOTIFY_WAIT = 180; // 3 minutes
		private static readonly float CLEANUP_RATE = .15f;
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
		private bool m_ClassifierWorking;
		private Dictionary<long, HullClassifier> m_Classifiers;
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
		private bool m_CheckClassifierNextUpdate;
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
		public int CaptureMultiplier { get { return m_EffectiveRules.CaptureMultiplier; } }
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
			// a classifer is destroyed and then the server restarts, all but the first
			// 25 blocks will be deleted on startup.
			m_Grid.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

			m_BlockCount = 0;
			m_BlockTypeCounts = new int[s_Settings.BlockTypes.Length];
			m_Owner = new GridOwner(this);
			m_Classifiers = new Dictionary<long, HullClassifier>();
			m_Projectors = new Dictionary<long, InGame.IMyProjector>();
			m_ReservedClass = DEFAULT_CLASS;
			m_ReservedRules = s_Settings.HullRules[(int)DEFAULT_CLASS];
			m_EffectiveClass = DEFAULT_CLASS;
			m_EffectiveRules = s_Settings.HullRules[(int)DEFAULT_CLASS];
			reserveEffectiveClassFromOwner();

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
			detatchClassifiers();
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

				// check for existing cleanup, fix failed timers and let owner know
				// if cleanup is ongoing
				if (m_CleanupTimer != null) {
					m_CleanupTimer.updateTimeRemaining();
					notifyViolations();
				}

				// Update ownership
				if (m_CheckOwnerNextUpdate || m_CheckCleanupNextUpdate || m_CheckClassifierNextUpdate) {
					log("checking owner due to flag", "UpdateBeforeSimulation100");
					m_CheckOwnerNextUpdate = false;

					reevaluateOwnership();
				}

				// Update classification
				if (m_CheckClassifierNextUpdate) {
					log("checking classifier due to flag", "UpdateBeforeSimulation100");
					m_CheckClassifierNextUpdate = false;

					reevaluateClassification();
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
				// Counts and Caches must be updated whether we're removing the block or not,
				// because blockRemoved has no way of knowing if we touched counts for it
				log("Update block lists and counts", "blockAdded");
				VIOLATION_TYPE classifierViolation = updateClassifiersWith(added);
				VIOLATION_TYPE totalBlocksViolation = incrementBlockCount();
				List<BlockType> violatedTypes = updateBlockTypeCountsWith(added);
				updateProjectorsWith(added);

				// we skip violation checks:
				// for blocks that aren't actually placed by a user, i.e. during World Load or a Merge
				log("checking enforcement skip conditions", "blockAdded");

				if (m_Merging || !m_BeyondFirst100) {
					log("Currently merging or initing, don't do placement enforcment",
						"blockAdded");
					goto Allowed;
				}
				// if we're projecting
				if (Projecting) {
					log("We are projecting, don't do placement enforcment",
						"blockAdded");
					goto Allowed;
				}

				// If there are violations, disallow placement
				// Everything will eventually be cleaned up over time, but we do this
				// enforcment on placement to help keep people in line
				log("check placement violations", "blockAdded");

				// classifier violations come first
				if (classifierViolation != VIOLATION_TYPE.NONE) {

					// people find it really annoying that they have to have an
					// owned block before placing a classifier.
					if (classifierViolation == VIOLATION_TYPE.TOO_MANY_OF_CLASS &&
						isUnowned()) {
						log("Too many of class but unowned, don't do placement enforcment",
							"checkClassAllowed");
						goto Allowed;
					}

					log("classifierViolation enforce", "blockAdded");
					notifyPlacementViolation(classifierViolation);
					goto Denied;
				}

				// then total blocks
				else if (totalBlocksViolation != VIOLATION_TYPE.NONE) {

					// people also find it annoying to have to delete all the blocks
					// on a ship that just became unclassified before they can build
					// another classifier or reactor to bring it in line
					if (helpsClassifyUnclassified(added)) {
						log("provides needed classification, don't do placement enforcment",
							"blockAdded");
						goto Allowed;
					}

					log("totalBlocksViolation enforce", "blockAdded");
					notifyPlacementViolation(totalBlocksViolation);
					goto Denied;
				}
				else if (violatedTypes.Count > 0) {
					notifyPlacementViolation(VIOLATION_TYPE.BLOCK_TYPE);
					log("block type violation enforced", "blockAdded");
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
			m_CheckCleanupNextUpdate = true;
			return;

		Denied:
			log(added.ToString() + " denied for grid '" + m_Grid.DisplayName +
				"'. Total Count now: " + m_BlockCount, "blockAdded");

			removeBlock(added);
		}


		private void updateProjectorsWith(IMySlimBlock block) {
			log("", "updateProjectorsWith");

			InGame.IMyProjector projector = block.FatBlock as InGame.IMyProjector;
			if (projector != null) {
				log("Added a projector", "blockAdded");
				m_Projectors.Add(projector.EntityId, projector);
			}
		}

		/// <summary>
		/// Applies the new class if block is a Classifier and we can reserve the Class
		/// Returns true if this was a happy update,
		/// false if the block needs to be removed
		/// </summary>
		/// <param name="block"></param>
		private VIOLATION_TYPE updateClassifiersWith(IMySlimBlock block) {
			log("", "updateClassificationWith");
			try {
				// If it's not a classifier, we don't care about it
				if (!block.isClassifierBlock()) {
					return VIOLATION_TYPE.NONE;
				}

				// load up the classifier helper object
				HullClassifier classifier = new HullClassifier(block);
				HullClass.CLASS classID = classifier.Class;
				log("Adding a classifier for class " + classID + " - " +
					s_Settings.HullRules[(int)classID].DisplayName,
					"updateClassificationWith");

				addClassifier(classifier);

				// Ensure it's the right type for this grid
				if (s_Settings.HullRules[(int)classifier.Class].ShouldBeStation && !m_Grid.IsStatic) {
					return VIOLATION_TYPE.SHOULD_BE_STATIC;
				}

				// Two classifiers not allowed
				if (m_Classifiers.Count > 1) {
					return VIOLATION_TYPE.TOO_MANY_CLASSIFIERS;
				}

				// Too many per Player/Faction not allowed
				if (!checkClassAllowed(classID)) {
					return VIOLATION_TYPE.TOO_MANY_OF_CLASS;
				}
			}
			catch (Exception e) {
				log("Error: " + e, "updateClassificationWith", Logger.severity.ERROR);
			}

			return VIOLATION_TYPE.NONE;
		}

		/// <summary>
		/// Adds 1 to total block count
		/// returns a violation if over the limit
		/// </summary>
		private VIOLATION_TYPE incrementBlockCount() {
			log("", "incrementTotalBlocks");
			m_BlockCount++;
			//log("new count " + m_BlockCount, "incrementTotalBlocks");
			//log("m_EffectiveRules: ", "incrementTotalBlocks");
			//log("m_EffectiveRules.DisplayName " + m_EffectiveRules.DisplayName, "incrementTotalBlocks");
			//log("m_EffectiveRules.MaxBlocks " + m_EffectiveRules.MaxBlocks, "incrementTotalBlocks");

			if (m_BlockCount > m_EffectiveRules.MaxBlocks) {
				return VIOLATION_TYPE.TOTAL_BLOCKS;
			}

			return VIOLATION_TYPE.NONE;
		}

		private bool helpsClassifyUnclassified(IMySlimBlock block) {
			if (m_EffectiveClass != DEFAULT_CLASS) {
				return false;
			}

			if (m_ReservedClass == DEFAULT_CLASS) {
				return block.isClassifierBlock();
			}
			else {
				IMyReactor reactor = block.FatBlock as IMyReactor;
				return (reactor != null);
			}
		}

		/// <summary>
		/// Increments the BlockType counts for a given new block
		/// Returns the violated limits if any
		/// </summary>
		private List<BlockType> updateBlockTypeCountsWith(IMySlimBlock block) {
			log("", "updateBlockTypeCountsWith");
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


		#endregion
		#region SE Hooks - Block Removed

		/// <summary>
		/// Called when a block is removed.  Decrements counts and checks for declassification.
		/// </summary>
		/// <param name="removed"></param>
		private void blockRemoved(IMySlimBlock removed) {
			try {
				updateClassificationWithout(removed);
				decrementBlockCount();
				updateBlockTypeCountsWithout(removed);
				updateProjectorsWithout(removed);

				log(removed.ToString() + " removed from grid '" + m_Grid.DisplayName +
					"'. Total Count now: " + m_BlockCount, "blockRemoved");

				m_CheckCleanupNextUpdate = true;

			} catch (Exception e) {
				log("Error: " + e, "blockRemoved");
			}
		}

		private void decrementBlockCount() {
			m_BlockCount--;
		}

		/// <summary>
		/// Removes the classifier and reserved class if block is a Classifier
		/// </summary>
		private void updateClassificationWithout(IMySlimBlock block) {
			// classifier?
			if (!block.isClassifierBlock())
				return;

			log("removing classifier", "updateClassificationWithout");
			removeClassifier(block.FatBlock.EntityId);
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

		private void updateProjectorsWithout(IMySlimBlock block) {
			// update projectors
			// track these outside of block types, because those are currently user-configurable
			InGame.IMyProjector projector = block.FatBlock as InGame.IMyProjector;
			if (projector != null) {
				log("Removed a projector", "blockRemoved");
				m_Projectors.Remove(projector.EntityId);
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
			try {
				log("flagging for ownership check next update", "blockOwnerChanged");
				m_CheckOwnerNextUpdate = true;
			} catch (Exception e) {
				log("Error: " + e, "blockOwnerChanged");
			}
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

		/// <summary>
		/// Check if the owner can have any more of this class.
		/// </summary>
		/// <param name="c">Class to check</param>
		/// <returns>True if the class is allowed</returns>
		private bool checkClassAllowed(HullClass.CLASS c) {
			log("checking if fleet can support more of " + c, "checkClassAllowed");
			return m_Owner.Fleet.canSupportAnother(c);
		}

		private void setReservedToClassifier() {
			if (m_Classifier == null) {
				log("No classifier available", "setReservedToClassifier");
				setReservedTo(DEFAULT_CLASS);
			}
			else {
				log("Using classifier of class " + m_Classifier.Class, "setReservedToClassifier");
				setReservedTo(m_Classifier.Class);
			}
		}

		private void setReservedTo(HullClass.CLASS newClass) {
			if (m_ReservedClass == newClass) {
				log("No change, ReservedClass remains " + m_ReservedClass, "setReservedTo");
			}
			else {
				log("Changing ReservedClass from " + m_ReservedClass + " to " + newClass,
					"setReservedTo");
				m_ReservedClass = newClass;
				m_ReservedRules = s_Settings.HullRules[(int)newClass];
			}
		}

		private void setEffectiveToClassifier() {
			if (m_Classifier != null && m_Classifier.FatBlock.IsWorking) {
				log("Have working classifier", "setReservedToClassifier");
				setEffectiveTo(m_ReservedClass);
			}
			else {
				log("No working classifier", "setReservedToClassifier");
				setEffectiveTo(DEFAULT_CLASS);
			}
		}

		private void setEffectiveTo(HullClass.CLASS newClass) {
			if (m_EffectiveClass == newClass) {
				log("No changed, EffectiveClass remains " + m_EffectiveClass, "setEffectiveTo");
			}
			else {
				log("Changing EffectiveClass from " + m_EffectiveClass + " to " + newClass,
					"setEffectiveTo");
				m_EffectiveClass = newClass;
				m_EffectiveRules = s_Settings.HullRules[(int)newClass];
				reserveEffectiveClassFromOwner();
				m_CheckCleanupNextUpdate = true;
			}
		}

		#endregion
		#region Classifier

		// <summary>
		/// Returns true if the added classifier is used
		/// </summary>
		/// <param name="classifier"></param>
		/// <returns></returns>
		private void addClassifier(HullClassifier classifier) {
			if (classifier == null || classifier.FatBlock == null) {
				log("Null classifier or Fatblock",
					"addClassifier", Logger.severity.ERROR);
				return;
			}

			log("adding classifier with ID " + classifier.FatBlock.EntityId,
				"addClassifier");

			m_Classifiers.Add(classifier.FatBlock.EntityId, classifier);
			classifier.FatBlock.IsWorkingChanged += classifierWorkingChanged;
			m_CheckClassifierNextUpdate = true;
		}

		private void removeClassifier(long entityID) {
			log("remove classifier with ID " + entityID,
				"removeClassifier");

			HullClassifier storedClassifier;
			m_Classifiers.TryGetValue(entityID, out storedClassifier);
			if (storedClassifier == null) {
				log("Classifier with id " + entityID + " was not stored",
					"removeClassifier", Logger.severity.ERROR);
			} else {
				log("Removing from Classifiers",  "removeClassifier");
				m_Classifiers.Remove(entityID);
				log("storedClassifier.FatBlock.IsWorkingChanged -= classifierWorkingChanged", "removeClassifier");
				storedClassifier.FatBlock.IsWorkingChanged -= classifierWorkingChanged;
			}

			if (m_Classifier == null) {
				log("Removing a classifier but none was set as main yet", "removeClassifier", Logger.severity.WARNING);
				// This could happen if the classifier was added and then removed before reevaluateClassification
				// has a chance to run through and store it as the new classifier
			} else if (m_Classifier.Equals(storedClassifier)) {
				log("Was the main Classifier, flagging for update", "removeClassifier");
				m_CheckClassifierNextUpdate = true;
			}
		}

		private void reevaluateClassification() {
			log("Reevaluate classifier", "reevaluateClassification");

			HullClassifier bestClassifier = findBestClassifier();

			if (m_Classifier != bestClassifier) {
				log("Classifier changed", "reevaluateClassification");
				m_Classifier = bestClassifier;
				setReservedToClassifier();
				setEffectiveToClassifier();
			} else {
				log("Classifier unchanged", "reevaluateClassification");
				bool workingNow = (m_Classifier == null) ? false : m_Classifier.FatBlock.IsWorking;
				if (m_ClassifierWorking != workingNow) {
					log("Working changed", "reevaluateClassification");
					m_ClassifierWorking = workingNow;
					setEffectiveToClassifier();
				}
			}
		}

		private HullClassifier findBestClassifier() {
			HullClassifier bestFound = null;
			bool bestFoundIsWorking = false;
			bool bestFoundIsSupported = false;
			HullClass.CLASS bestFoundClass = HullClass.CLASS.UNCLASSIFIED;

			HullClassifier current;
			bool currentIsWorking;
			bool currentIsSupported;
			HullClass.CLASS currentClass;

			foreach (KeyValuePair<long, HullClassifier> pair in m_Classifiers) {
				current = pair.Value;
				currentIsWorking = current.FatBlock.IsWorking;
				currentClass = current.Class;
				currentIsSupported = checkClassAllowed(currentClass);

				if (bestFound == null) {
					goto better;
				}

				// Always prefer a working classifier
				if (!bestFoundIsWorking && currentIsWorking) {
					goto better;
				}
				else if (bestFoundIsWorking && !currentIsWorking) {
					goto worse;
				}

				// Then prefer a supported classifier
				if (!bestFoundIsSupported && currentIsSupported) {
					goto better;
				}
				else if (bestFoundIsSupported && !currentIsSupported) {
					goto worse;
				}

				// Then prefer a higher level class
				if (bestFoundClass < currentClass) {
					goto better;
				}
				else if (bestFoundClass > currentClass) {
					goto worse;
				}

				// Tie
				log("Tie", "reevaluateBestClassifier");
				goto worse;

			better:
				bestFound = current;
				bestFoundIsWorking = currentIsWorking;
				bestFoundIsSupported = currentIsSupported;
				bestFoundClass = currentClass;
				continue;
			worse:
				continue;
			}

			return bestFound;
		}

		private void detatchClassifiers() {
			if (m_Classifiers != null) {
				foreach (KeyValuePair<long, HullClassifier> pair in m_Classifiers) {
					pair.Value.FatBlock.IsWorkingChanged -= classifierWorkingChanged;
				}
				m_Classifiers = null;
			}
			m_Classifier = null;
		}

		private void removeExtraClassifiers(int removeCount = 100) {
			// Supposedly we've only added the best classifier up to now,
			// so all we need to check for is unused classifiers

			if (m_Classifier == null) {
				log("m_Classifier is not set! Aborting.",
					"removeExtraClassifiers", Logger.severity.ERROR);
				return;
			}

			List<HullClassifier> worstClassifiers = findWorstClassifiers(1);
			foreach (HullClassifier classifier in worstClassifiers) {
				removeBlock(classifier.SlimBlock);
			}
		}

		/// <summary>
		/// Find the worst classifiers so we can remove them instead of better ones
		/// </summary>
		/// <remarks>
		/// This is really dumb, just randomly picks some we aren't directly using.
		/// Making this simpler, and making findBestClassifiers simpler, would be
		/// best done by interfacing IComparable with Hull Classifiers.
		/// The cleanest way to do that would be making Hull Classifiers a logic component
		/// so we can track isWorking there and make sure it's closed when they are
		/// This would require the ge to be able to find the HullClassifier component on
		/// an added fatblock, which seems like it might be possible, but also might not
		/// </remarks>
		private List<HullClassifier> findWorstClassifiers(uint removeCount = 0) {
			List<HullClassifier> worstClassifiers = new List<HullClassifier>();
			uint worstClassifiersCount = 0;
			long currentClassifierID = m_Classifier.FatBlock.EntityId;

			foreach (KeyValuePair<long, HullClassifier> pair in m_Classifiers) {
				if (pair.Key == currentClassifierID)
					continue;

				if (removeCount > worstClassifiersCount) {
					worstClassifiers.Add(pair.Value);
					worstClassifiersCount++;
				}
			}

			return worstClassifiers;
		}

		private void classifierWorkingChanged(IMyCubeBlock b) {
			log("Classifier " + b.EntityId + " changed working state, flagging for update", 
				"classifierWorkingChanged");
			m_CheckClassifierNextUpdate = true;
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
			bool changed;
			if (s_Settings.SimpleOwnership) {
				changed = m_Owner.reevaluateOwnership(new List<long> { m_Grid.getClassifierBlock().OwnerId });
			}
			else {
				changed = m_Owner.reevaluateOwnership(BigOwners);
			}

			if (changed) {
				m_CheckCleanupNextUpdate = true;
				log("owner changed", "reevaluateOwnership");
			}
			else {
				log("no change", "reevaluateOwnership");
			}

			return changed;
		}

		private void reserveEffectiveClassFromOwner() {
			m_Owner.setClassification(m_EffectiveClass);
		}

		private bool isUnowned() {
			return m_Owner.OwnerType == GridOwner.OWNER_TYPE.UNOWNED;
		}

		private void detatchOwner() {
			if (m_Owner != null) {
				log("detatching owner", "detatchOwner");
				m_Owner.Close();
				m_Owner = null;
			}
		}

		public void markSupported(long fleetOwnerID) {
			log("Marking as supported", "markSupported");
			// we may want this later if we need to lookup a fleet ?
			//m_FleetOwnerID = fleetOwnerID;
			m_Supported = true;
			m_CheckCleanupNextUpdate = true;
		}

		public void markUnsupported(long fleetOwnerID) {
			log("Marking as unsupported", "markUnsupported");
			//m_FleetOwnerID = fleetOwnerID;
			m_Supported = false;
			m_CheckClassifierNextUpdate = true;
			m_CheckCleanupNextUpdate = true;
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
			int classifiersCount = m_Classifiers.Count;
			if (classifiersCount > 1) {
				violations.Add(new VIOLATION() {
					Type = VIOLATION_TYPE.TOO_MANY_CLASSIFIERS,
					Name = "Hull Classifiers",
					Count = classifiersCount,
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
			log("Updating violations", "updateViolations");

			int oldCount;
			if (m_CurrentViolations == null) {
				oldCount = 0;
			} else {
				oldCount = m_CurrentViolations.Count;
			}

			m_CurrentViolations = currentViolations();
			int newCount = m_CurrentViolations.Count;
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
			// we actually know we're on the highest supported classifier, 
			// so if we're unsupported it must not have any supported available
			if (removeClass) {
				log("Grid has no supported working classifiers, remove some blocks",
					"doCleanupPhase", Logger.severity.TRACE);

				// detection for exploration ships, remove them immediately
				if (CLEANUP_PREFABS_IMMEDIATELY && isGeneratedPrefab()) {
					totalToRemove = m_BlockCount;
				} else {
					// having a Unsupported grid is equivalent to having one with zero allowed blocks
					totalToRemove = phasedRemoveCount(new VIOLATION {
						Type = VIOLATION_TYPE.TOTAL_BLOCKS,
						Count = m_BlockCount,
						Limit = 0
					});
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
					notifyCleanupViolation(null);
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
		#region Utility - General

		private bool isGeneratedPrefab() {
			return m_ReservedClass == DEFAULT_CLASS &&
				isUnowned() &&
				m_BlockCount > PREFAB_BLOCK_THRESHOLD;
		}

		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false) {
			return Container.Entity.GetObjectBuilder();
		}

		private void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			if (m_Logger != null)
				m_Logger.log(level, method, message);
		}

		public void serialize(VRage.ByteStream stream) {
			stream.addBoolean(SupportedByFleet);
			stream.addLong(Grid.EntityId);
			stream.addUShort((ushort)m_EffectiveClass);
			stream.addString(Grid.DisplayName);
			stream.addUShort((ushort)BlockCount);

			// Serialize position data if the owner of the grid
			if (Grid.canInteractWith(Owner.PlayerID)) {
				stream.addBoolean(true);
				stream.addLong((long)Grid.GetPosition().X);
				stream.addLong((long)Grid.GetPosition().Y);
				stream.addLong((long)Grid.GetPosition().Z);
			}
			else {
				stream.addBoolean(false);
			}
		}

		public static GridData deserialize(VRage.ByteStream stream) {
			GridData result = new GridData();

			result.supported = stream.getBoolean();
			result.shipID = stream.getLong();
			result.shipClass = (HullClass.CLASS)stream.getUShort();
			result.shipName = stream.getString();
			result.blockCount = (int)stream.getUShort();
			result.displayPos = stream.getBoolean();
			if (result.displayPos) {
				long x, y, z;
				x = stream.getLong();
				y = stream.getLong();
				z = stream.getLong();
				result.shipPosition = new VRageMath.Vector3D(x, y, z);
			}
			else {
				result.shipPosition = new VRageMath.Vector3D();
			}
			return result;
		}

		#endregion
	}
}

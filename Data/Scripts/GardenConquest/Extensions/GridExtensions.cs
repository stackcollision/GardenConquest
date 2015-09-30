using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Components;
using VRageMath;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using InGame = Sandbox.ModAPI.Ingame;
using Interfaces = Sandbox.ModAPI.Interfaces;

namespace GardenConquest.Extensions {

	/// <summary>
	/// Helper functions for SE grids
	/// </summary>
	public static class GridExtensions {

		private static readonly float INGAME_PLACEMENT_MAX_DISTANCE = 60f;

		private static Logger s_Logger = new Logger("IMyCubeGrid", "Static");

		/// <summary>
		/// Attempts to place count of a physical object in inventory of grid
		/// Will place as many of the object as possible in each inventory until
		/// nothing remains to place. Returns number remaining to place.
		/// </summary>
		public static int placeInCargo(this IMyCubeGrid grid, 
			SerializableDefinitionId def, MyObjectBuilder_Component builder, 
			int count) {

			if (count <= 0) return 0;
			int remaining = count;

			var containers = new List<IMySlimBlock>();
			grid.GetBlocks(containers, x => 
				x.FatBlock != null &&
				x.FatBlock as Interfaces.IMyInventoryOwner != null
				);             
			if (containers.Count == 0)
				return remaining;

			foreach (IMySlimBlock block in containers) {
				if (remaining == 0) break;

				var inventoryOwner = block.FatBlock as Interfaces.IMyInventoryOwner;
				var inventory = inventoryOwner.GetInventory(0) as IMyInventory;

				if (inventory == null) {
					// log error, invOwner existed but not inventory>?
					continue;
				}

				if (inventory.CanItemsBeAdded((VRage.MyFixedPoint)remaining, def)) {
					// Add all the items if it has enough space
					inventory.AddItems(remaining, builder);
					remaining = 0;
				} else {
					// Add them incrementally if there's some space
					// I would prefer to do some math to tell how many we can add,
					// instead of just continually looping through with 1.
					// But the logic to get the volume of a component 
					// is surprisingly complex without access to the Adapter
					// and we'd need to check the inventory's supported types
					while (remaining > 0 && inventory.CanItemsBeAdded((VRage.MyFixedPoint)1, def)) {
						inventory.AddItems(1, builder);
						remaining--;
					}
				}
			}

			return remaining;
		}

		/// <summary>
		/// Returns a list of players near a grid.  Used to send messages
		/// </summary>
		/// <param name="grid"></param>
		/// <returns></returns>
		public static List<IMyPlayer> getPlayersWithinPlacementRadius(this IMyCubeGrid self) {
			log("Getting players near grid " + self.DisplayName);

			VRageMath.Vector3 gridSize = self.LocalAABB.Size;
			float gridMaxLength = Math.Max(gridSize.X, Math.Max(gridSize.Y, gridSize.Z));
			float maxDistFromGrid = gridMaxLength + INGAME_PLACEMENT_MAX_DISTANCE;

			return self.getPlayersWithin(maxDistFromGrid);
		}

		public static List<long> getPlayerIDsWithinPlacementRadius(this IMyCubeGrid self) {
			return getPlayersWithinPlacementRadius(self).ConvertAll(x => x.PlayerID);
		}
		/// <summary>
		/// Returns a list of players within radius of grid
		/// </summary>
		/// <param name="grid"></param>
		/// <returns></returns>
		public static List<IMyPlayer> getPlayersWithin(this IMyCubeGrid self, float radius) {
			log("Getting players near grid " + self.DisplayName);

			Vector3 position = self.GetPosition();
			return MyAPIGateway.Players.getPlayersNearPoint(position, radius);
		}

		/// <summary>
		/// Checks whether a grid is owned only by one faction
		/// If a single block is owned by another player, returns false
		/// </summary>
		/// <param name="grid">Grid to check</param>
		/// <returns></returns>
		public static bool ownedBySingleFaction(this IMyCubeGrid grid) {
			// No one owns the grid
			if (grid.BigOwners.Count == 0)
				return false;

			// Guaranteed to have at least 1 owner after previous check
			IMyFactionCollection facs = MyAPIGateway.Session.Factions;
			IMyFaction fac = facs.TryGetPlayerFaction(grid.BigOwners[0]);
			
			// Test big owners
			for (int i = 1; i < grid.BigOwners.Count; ++i) {
				IMyFaction newF = facs.TryGetPlayerFaction(grid.BigOwners[i]);
				if (newF != fac)
					return false;
			}

			// Test small owners
			for (int i = 0; i < grid.SmallOwners.Count; ++i) {
				IMyFaction newF = facs.TryGetPlayerFaction(grid.SmallOwners[i]);
				if (newF != fac)
					return false;
			}

			// Didn't encounter any factions different from the BigOwner[0] faction
			return true;
		}

		/// <summary>
		/// Is the given player allowed get information or use commands on the grid?
		/// Player is able to interact with the grid if the player is part of a specific
		/// block's faction OR the player owns the block
		/// The block is dependent on the CommandsRequireClassifier setting
		/// </summary>
		/// <param name="playerID"></param>
		/// <param name="grid"></param>
		/// <returns></returns>
		public static bool canInteractWith(this IMyCubeGrid grid, long playerID) {
			IMyCubeBlock blockToCheck;

			// Get the type of block to check based on settings
			if (Core.ConquestSettings.getInstance().SimpleOwnership) {
				blockToCheck = grid.getClassifierBlock();
			}
			else {
				blockToCheck = grid.getMainCockpit();
			}

			if (blockToCheck != null) {
				MyRelationsBetweenPlayerAndBlock relationship = blockToCheck.GetUserRelationToOwner(playerID);
				if (relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership || relationship == MyRelationsBetweenPlayerAndBlock.Enemies) {
					return false;
				}
				else if (relationship == MyRelationsBetweenPlayerAndBlock.FactionShare || relationship == MyRelationsBetweenPlayerAndBlock.Owner) {
					return true;
				}
				// Being in a faction doesn't necessarily mean FactionShare, so need to check for faction status
				else {
					IMyFactionCollection factions = MyAPIGateway.Session.Factions;
					IMyFaction blocksFaction = factions.TryGetPlayerFaction(blockToCheck.OwnerId);

					// Block is either owned by friendly faction or user's faction
					if (blocksFaction != null) {
						long owningFactionID = blocksFaction.FactionId;
						if (owningFactionID == factions.TryGetPlayerFaction(playerID).FactionId) {
							return true;
						}
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Get the classifier block on the grid
		/// </summary>
		/// <param name="grid"></param>
		/// <remarks>Grids should only have 1 classifier block</remarks>
		/// <returns>The HullClassifier as IMyCubeBlock if found, null otherwise</returns>
		public static IMyCubeBlock getClassifierBlock(this IMyCubeGrid grid) {
			List<IMySlimBlock> blocks = new List<IMySlimBlock>();

			// Get all blocks with fatblocks
			grid.GetBlocks(blocks, (b => b.FatBlock != null));

			foreach (IMySlimBlock block in blocks) {
				if (block.isClassifierBlock()) {
					return block.FatBlock;
				}
			}
			return null;
		}

		/// <summary>
		/// Get the main cockpit on the grid
		/// </summary>
		/// <param name="grid"></param>
		/// <remarks>Grids should only have 1 main cockpit</remarks>
		/// <returns>The main cockpit as IMyCubeBlock if found, null otherwise</returns>
		public static IMyCubeBlock getMainCockpit(this IMyCubeGrid grid) {
			List<IMySlimBlock> cockpitBlocks = new List<IMySlimBlock>();

			// Get all cockpit blocks
			grid.GetBlocks(cockpitBlocks, (b => b.FatBlock != null && b.FatBlock is InGame.IMyShipController));

			foreach (IMySlimBlock block in cockpitBlocks) {
				if (Interfaces.TerminalPropertyExtensions.GetValueBool(block.FatBlock as IMyTerminalBlock, "MainCockpit")) {
					return block.FatBlock;
				}
			}
			return null;
		}

		private static void log(String message, String method = null, Logger.severity level = Logger.severity.DEBUG) {
			s_Logger.log(level, method, message);
		}

	}

}

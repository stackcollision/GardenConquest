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
using Sandbox.Definitions;
using Sandbox.ModAPI;
using InGame = Sandbox.ModAPI.Ingame;
using Interfaces = Sandbox.ModAPI.Interfaces;

namespace GardenConquest.Extensions {

	/// <summary>
	/// Helper functions for SE grids
	/// </summary>
	public static class GridExtensions {

		/// <summary>
		/// Gets the first available non-empty cargo container on a grid.  
		/// If optional parameters given, first cargo which can fit that volume.
		/// </summary>
		/// <param name="def">Optional: Builder definition</param>
		/// <param name="count">Optional: Number of items</param>
		/// <returns></returns>
		public static InGame.IMyCargoContainer getAvailableCargo(this IMyCubeGrid grid, VRage.ObjectBuilders.SerializableDefinitionId? def = null, int count = 1) {
			List<IMySlimBlock> containers = new List<IMySlimBlock>();
			grid.GetBlocks(containers, x => x.FatBlock != null && x.FatBlock is InGame.IMyCargoContainer);

			if (containers.Count == 0)
				return null;

			if (def == null) {
				// Don't care about fit, just return the first one
				return containers[0].FatBlock as InGame.IMyCargoContainer;
			} else {
				foreach (IMySlimBlock block in containers) {
					InGame.IMyCargoContainer c = block.FatBlock as InGame.IMyCargoContainer;
					Interfaces.IMyInventoryOwner invo = c as Interfaces.IMyInventoryOwner;
					Interfaces.IMyInventory inv = invo.GetInventory(0);

					// TODO: check for fit
					return c;
				}
			}

			return null;
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
		/// Player is able to interact with the grid if the player is part of the
		/// mainCockpit's faction OR the player owns the mainCockpit
		/// </summary>
		/// <param name="playerID"></param>
		/// <param name="grid"></param>
		/// <remarks>
		/// Relatively expensive since we iterate through all blocks in grid!
		/// </remarks>
		/// <returns></returns>
		public static bool canInteractWith(this IMyCubeGrid grid, long playerID) {
			bool result = false;
			List<IMySlimBlock> fatBlocks = new List<IMySlimBlock>();

			// Get all cockpit blocks
			Func<IMySlimBlock, bool> isFatBlock = b => b.FatBlock != null && b.FatBlock is InGame.IMyShipController;
			grid.GetBlocks(fatBlocks, isFatBlock);

			// We'll need the faction list to compare the factions of the given player and the owners of the fatblocks in the grid
			IMyFactionCollection factions = MyAPIGateway.Session.Factions;

			// Go through and search for the main cockpit. If found, set result and break out of loop, since there should only be 1 main cockpit
			foreach (IMySlimBlock block in fatBlocks) {
				if (Interfaces.TerminalPropertyExtensions.GetValueBool(block.FatBlock as InGame.IMyTerminalBlock, "MainCockpit")) {
					IMyFaction blocksFaction = factions.TryGetPlayerFaction(block.FatBlock.OwnerId);
					// Owner of block is running solo
					if (blocksFaction == null) {
						if (block.FatBlock.OwnerId == playerID) {
							result = true;
							break;
						}
					}
					// Owner of block is part of a faction
					else {
						long owningFactionID = blocksFaction.FactionId;
						if (owningFactionID == factions.TryGetPlayerFaction(playerID).FactionId) {
							result = true;
							break;
						}
					}
				}
			}
			return result;
		}

	}

}

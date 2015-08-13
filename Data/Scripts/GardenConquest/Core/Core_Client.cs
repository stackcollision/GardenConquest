using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.ModAPI;
using InGame = Sandbox.ModAPI.Ingame;

using GardenConquest.Extensions;
using GardenConquest.Messaging;

namespace GardenConquest.Core {

	/// <summary>
	/// Core of the client
	/// </summary>
	public class Core_Client : Core_Base {
		#region Class Members

		private CommandProcessor m_CmdProc = null;
		private ResponseProcessor m_MailMan = null;
		private bool m_NeedSettings = true;

		private IMyPlayer m_Player;
		private int m_CurrentFrame;

		#endregion
		#region Inherited Methods

		public override void initialize() {
			if (s_Logger == null)
				s_Logger = new Logger("Conquest Core", "Client");

			m_Player = MyAPIGateway.Session.Player;
			m_CurrentFrame = 0;

			m_MailMan = new ResponseProcessor();

			m_CmdProc = new CommandProcessor(m_MailMan);
			m_CmdProc.initialize();
		}

		public override void unloadData() {
			log("Unloading", "unloadData");
			m_CmdProc.shutdown();
			m_MailMan.unload();
		}

		public override void updateBeforeSimulation() {
			if (m_NeedSettings) {
				try {
					m_NeedSettings = !m_MailMan.requestSettings();
				} catch (Exception e) {
					log("Error" + e, "updateBeforeSimulation", Logger.severity.ERROR);
				}
			}

			if (m_CurrentFrame >= Constants.UpdateFrequency - 1) {
				if (m_Player.Controller.ControlledEntity is InGame.IMyShipController) {
					IMyCubeGrid currentControllerGrid = (m_Player.Controller.ControlledEntity as IMyCubeBlock).CubeGrid;
					IMyCubeBlock classifierBlock = currentControllerGrid.getClassifierBlock();
					if (classifierBlock != null && classifierBlock.OwnerId != m_Player.PlayerID && ConquestSettings.getInstance().SimpleOwnership) {
						MyAPIGateway.Utilities.ShowNotification("WARNING: Take control of the hull classifier or you may be tracked by the original owner!", 1250, MyFontEnum.Red);
					}
				}
				m_CurrentFrame = 0;
			}
			++m_CurrentFrame;
		}

		#endregion
		#region Hooks

		#endregion
	}
}

using MultiplayerMod.Framework.Mobile.Menus;
using MultiplayerMod.Framework.Network;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static MultiplayerMod.Framework.Mobile.Menus.SCoopGameMenu;
namespace MultiplayerMod.Framework.Command
{
    internal class ClientConnectMenuCommand : ICommand
    {
        public string Name => "client_connectMenu";

        public string Description => "Display the quick connection menu";

        public void Execute(string str, string[] args)
        {

            if (args.Length > 0)
            {
                enterIP(args[0]);
                return;
            }


            string title = Game1.content.LoadString("Strings\\UI:CoopMenu_EnterIP");
            TitleTextInputMenu titleTextInputMenu = new TitleTextInputMenu(title, (address) =>
            {
                enterIP(address);
            }, ModUtilities.ModConfig.LastIP);
            setMenu(titleTextInputMenu);
        }
        public void enterIP(string address)
        {

            try
            {
                StartupPreferences startupPreferences2 = new StartupPreferences();
                startupPreferences2.loadPreferences(false, false);
                startupPreferences2.lastEnteredIP = address;
                startupPreferences2.savePreferences(false, false);
            }
            catch (Exception)
            {
            }

            ModUtilities.ModConfig.LastIP = address;
            ModUtilities.Helper.WriteConfig(ModUtilities.ModConfig);
            setMenu(new SFarmhandMenu(ModUtilities.multiplayer.InitClient(new ModClient(ModUtilities.ModConfig, address))));
        }
        public static void setMenu(IClickableMenu clickableMenu)
        {
            if (Game1.activeClickableMenu is TitleMenu titleMenu)
            {
                TitleMenu.subMenu = clickableMenu;
            }
            else
            {
                Game1.activeClickableMenu = clickableMenu;
            }
        }
    }
}

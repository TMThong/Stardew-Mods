using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using MultiplayerMod.Framework.Patch;
using MultiplayerMod.Framework;
using System.Reflection;
using System;
using MultiplayerMod.Framework.Command;
using StardewValley.Menus;
using MultiplayerMod.Framework.Mobile.Menus;
using StardewValley.Monsters;
using System.IO;
using StardewValley;
using System.Linq;

namespace MultiplayerMod
{
    public class ModEntry : Mod
    {
        internal Config config;
        internal PatchManager PatchManager { get; set; }
        internal static IModHelper ModHelper;
        internal static IMonitor ModMonitor;
        internal CommandManager CommandManager { get; set; }
        public override void Entry(IModHelper helper)
        {
            ModUtilities.Helper = helper;
            ModUtilities.ModMonitor = Monitor;
            config = helper.ReadConfig<Config>();
            ModUtilities.ModConfig = config;
            ModHelper = helper;
            ModMonitor = Monitor;
            PatchManager = new PatchManager(Helper, ModManifest, config);
            PatchManager.Apply();
            CommandManager = new CommandManager();
            CommandManager.Apply(helper);
            ApplyDebug();
            Helper.Events.Display.MenuChanged += OnMenuChanged;
            Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        }
        void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            if(Game1.gameMode == 6 && Game1.IsMultiplayer)
            {
                Game1.activeClickableMenu = null;
            }
        }
        void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            Monitor.Log(e.NewMenu.GetType().Name , LogLevel.Alert);
            if(e.NewMenu is TitleMenu)
            {

            }
        }

        void ApplyDebug()
        {
#if DEBUG
            Helper.Events.Input.ButtonPressed += (o, e) =>
            {
                if (e.Button == SButton.M)
                {
                    CommandManager.GetCommand("client_connectMenu").Execute("client_connectMenu", new string[] { });
                }
                else if (e.Button == SButton.N)
                {
                    Game1.activeClickableMenu = new SCoopMenuMobile();
                }
                else if (e.Button == SButton.P)
                {
                    Game1.activeClickableMenu = new CharacterCustomization(CharacterCustomization.Source.HostNewFarm);
                }
                else if (e.Button == SButton.L)
                {
                    Game1.activeClickableMenu = new SCoopGameMenu(false);
                }
            };
#endif
        }

    }
}

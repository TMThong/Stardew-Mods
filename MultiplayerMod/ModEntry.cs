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
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

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
            
        }

        /// <summary>
        /// This is for debugging, never mind it ...
        /// </summary>
        void ApplyDebug()
        {
#if DEBUG
            Game1.debugMode = true;
            Helper.Events.GameLoop.UpdateTicking += (sender, args) =>
            {
                if(Game1.client != null && args.Ticks % 30 == 0)
                {
                    Monitor.Log($"Game1.displayHUD {Game1.displayHUD} , Game1.eventUp {Game1.eventUp} , Game1.currentBillboard {Game1.currentBillboard} , Game1.gameMode {Game1.gameMode} ,  Game1.freezeControls {Game1.freezeControls} , Game1.panMode {Game1.panMode} , Game1.HostPaused {Game1.HostPaused} , takingMapScreenshot {Game1.game1.takingMapScreenshot}", LogLevel.Warn);
                }
            };
            var listField = new List<IReflectedField<object>>();
            /*
            try
            {
                foreach(var field in typeof(Game1).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    try
                    {
                        listField.Add(Helper.Reflection.GetField<object>(Game1.game1, field.Name));
                    }
                    catch(Exception ex)
                    {
                        Monitor.Log(ex.GetBaseException().ToString() , LogLevel.Error);
                    }
                }
                foreach (var field in typeof(Game1).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    try
                    {
                        listField.Add(Helper.Reflection.GetField<object>(Game1.game1.GetType(), field.Name));
                    }
                    catch (Exception ex)
                    {
                        Monitor.Log(ex.GetBaseException().ToString(), LogLevel.Error);
                    }
                }
            }
            catch(Exception ex)
            {

            }*/
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
            /*Helper.Events.GameLoop.UpdateTicked += (o, e) =>
            {
                if (Game1.currentLocation != null && e.Ticks % 20 == 0)
                {
                    if(ModUtilities.IsAndroid)
                    Monitor.Log($"GameMode {Game1.gameMode}", LogLevel.Debug);
                    try
                    {
                        Monitor.Log("Starting Field Game1 Debug" , LogLevel.Alert);
                        foreach (var field in listField)
                        {
                            Monitor.Log($"Field  {field.FieldInfo.Name} Is Null ({field.GetValue() != null}) | TYPE {field.FieldInfo.FieldType.Namespace + "." + field.FieldInfo.FieldType.Name}", LogLevel.Debug);
                        }
                        Monitor.Log("Stopped Field Game1 Debug", LogLevel.Alert);
                    }
                    catch(Exception ex)
                    {

                    }
                }
            };*/
#endif
        }

    }
}

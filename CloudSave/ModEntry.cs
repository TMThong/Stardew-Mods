using CloudSave.Framework.Patch;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.IO;

namespace CloudSave
{
    public class ModEntry : Mod
    {
        internal Config config;

        private PatchManager PatchManager;

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<Config>();
            PatchManager = new PatchManager(helper, this.ModManifest, config);
            PatchManager.Apply();
            this.Helper.ConsoleCommands.Add("save_to_cloud", "", SaveCommand);
        }
        /// <summary>
        /// Test method
        /// </summary>
        public void SaveCommand(string cmd, string[] options)
        {
            if (Context.IsPlayerFree)
            {
                 
            }
            else this.Monitor.Log("Players must enter the save.", LogLevel.Error);
        }
    }
}

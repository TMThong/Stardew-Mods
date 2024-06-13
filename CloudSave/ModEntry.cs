using CloudSave.Framework.Patch;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;

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
        }
    }
}

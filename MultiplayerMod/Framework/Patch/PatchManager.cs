using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using StardewModdingAPI;
using MultiplayerMod;
using HarmonyLib;
using MultiplayerMod.Framework.Patch.Mobile;
using MultiplayerMod.Framework.Patch.Desktop;
using StardewValleyMod.Shared.FastHarmony;

namespace MultiplayerMod.Framework.Patch
{
    internal class PatchManager : AbstractPatchManager<Config>
    {
        public PatchManager(IModHelper helper, IManifest manifest, Config config) : base(helper, manifest, config) 
        {
            
        }

        public override void Init()
        {
            if (Constants.TargetPlatform == GamePlatform.Android)
            {
                Patches.Add(new SMultiplayerPatch());
                Patches.Add(new GameServerPatch());
            }
            else
            {
                Patches.Add(new CoopMenuPatch());
            }
            Patches.Add(new FarmerPatch());
        }
    }
}

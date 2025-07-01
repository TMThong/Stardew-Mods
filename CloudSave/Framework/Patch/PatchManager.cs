using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValleyMod.Shared;
using StardewValleyMod.Shared.FastHarmony;
namespace CloudSave.Framework.Patch
{
    internal class PatchManager : AbstractPatchManager<Config>
    {
        public PatchManager(IModHelper helper, IManifest manifest, Config config) : base(helper, manifest, config)
        {
            
        }

        public override void Init()
        {
            
        }
    }
}

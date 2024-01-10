using HarmonyLib;
using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AndroidPatcher.Patches
{
    internal class PatchManager
    {
        public IModHelper Helper { get; set; }
        public IManifest Manifest { get; set; }
        public Config Config { get; set; }
        public List<IPatch> Patches { get; set; } = new List<IPatch>();
        public Harmony Harmony { get; }
        public PatchManager(IModHelper helper, IManifest manifest, Config config)
        {
            Helper = helper;
            Manifest = manifest;
            Config = config;
            Harmony = new Harmony(Manifest.UniqueID);


            this.Patches.Add(new FarmerPatch());
        }
        public void Apply()
        {
            foreach (var patch in Patches)
            {
                var p = patch;
                p.Apply(Harmony);

            }
        }
    }
}

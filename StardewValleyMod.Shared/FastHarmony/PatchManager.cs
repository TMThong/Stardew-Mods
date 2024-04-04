using HarmonyLib;
using Patch1._6Mobile;
using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Text;

namespace StardewValleyMod.Shared.FastHarmony
{
    public class AbstractPatchManager
    {
        public IModHelper Helper { get; set; }
        public IManifest Manifest { get; set; }
        public Config Config { get; set; }
        public List<IPatch> Patches { get; set; } = new List<IPatch>();
        public Harmony Harmony { get; }
        public AbstractPatchManager(IModHelper helper, IManifest manifest, Config config)
        {
            Helper = helper;
            Manifest = manifest;
            Config = config;
            Harmony = new Harmony(Manifest.UniqueID);
            InitPatch();
        }

        public virtual void InitPatch()
        {

        }

        public virtual void Apply()
        {
            foreach (var patch in Patches)
            {
                var p = patch;
                p.Apply(Harmony);
            }
        }
    }
}

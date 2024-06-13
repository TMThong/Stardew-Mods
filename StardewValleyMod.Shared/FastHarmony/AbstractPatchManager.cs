using HarmonyLib;
using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Text;

namespace StardewValleyMod.Shared.FastHarmony
{
    public class AbstractPatchManager<TConfig>
    {
        public IModHelper Helper { get; set; }
        public IManifest Manifest { get; set; }
        public TConfig Config { get; set; }
        public List<IPatch> Patches { get; set; } = new List<IPatch>();
        public Harmony Harmony { get; }
        public AbstractPatchManager(IModHelper helper, IManifest manifest, TConfig config)
        {
            Helper = helper;
            Manifest = manifest;
            Config = config;
            Harmony = new Harmony(Manifest.UniqueID);
            Init();
        }
        public virtual void Apply()
        {
            foreach (var patch in Patches)
            {
                var p = patch;
                p.Apply(Harmony);
            }
        }

        public virtual void Init()
        {

        }
    }
}

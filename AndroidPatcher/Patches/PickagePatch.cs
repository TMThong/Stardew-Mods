using HarmonyLib;
using StardewValley.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AndroidPatcher.Patches
{
    internal class PickagePatch : IPatch
    {
        public Type PatchType => typeof(Pickaxe);

        public void Apply(Harmony harmony)
        {
            
        }
    }
}

using HarmonyLib;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AndroidPatcher.Patches
{
    internal class FarmerPatch : IPatch
    {
        public Type PatchType => typeof(Farmer);

        public void Apply(Harmony harmony)
        {
             
        }
    }
}

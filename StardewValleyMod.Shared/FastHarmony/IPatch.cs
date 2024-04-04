using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace StardewValleyMod.Shared.FastHarmony
{
    public interface IPatch
    {
        public Type TypePatch { get; }
        void Apply(Harmony harmony);
    }
}

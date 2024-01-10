using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroDelayMap
{
    public class ModEntry : Mod
    {
        private Harmony Harmony = new Harmony("zerodelaymap");

        public override void Entry(IModHelper helper)
        {
            Harmony.Patch(AccessTools.Method(typeof(Game1), "warpFarmer", new Type[] { typeof(LocationRequest), typeof(int), typeof(int), typeof(int) }), postfix: new HarmonyMethod(this.GetType(), nameof(postfix_warpFarmer)));
            Harmony.Patch(AccessTools.Method(typeof(Game1), "fadeScreenToBlack", new Type[] { }), prefix: new HarmonyMethod(this.GetType(), nameof(prefix_fadeScreenToBlack)));
        }


        private static bool prefix_fadeScreenToBlack()
        {
            if (Game1.isWarping)
            {
                return false;
            }
            return true;
        }


        private static void postfix_warpFarmer()
        {
            var method = AccessTools.Method(typeof(Game1), "onFadeToBlackComplete");
            method.Invoke(Game1.game1, null);
        }
    }
}

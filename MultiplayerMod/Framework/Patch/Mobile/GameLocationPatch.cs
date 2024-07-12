using HarmonyLib;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMod.Shared.FastHarmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerMod.Framework.Patch.Mobile
{
    internal class GameLocationPatch : IPatch
    {

        public Type TypePatch => typeof(GameLocation);

        public void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.PropertyGetter(TypePatch, "tapToMove"), postfix: new HarmonyMethod(this.GetType(), nameof(postfix_get_tapToMove)));
        }
        private static void postfix_get_tapToMove(GameLocation __instance, ref object __result)
        {
            if (__result == null)
            {
                object TapToMove = typeof(IClickableMenu).Assembly.GetType("StardewValley.Mobile.TapToMove").CreateInstance<object>(new object[] { __instance });
                ModEntry.tapToMoveProperty.SetValue(__instance, TapToMove);
                __result = TapToMove;
            }
        }
    }
}

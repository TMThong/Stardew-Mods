using HarmonyLib;
using StardewValley;
using StardewValley.Menus;
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
        private static readonly Type PATCH_TYPE = typeof(GameLocation);
        public static PropertyInfo tapToMoveProperty;
         
        

        public void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.PropertyGetter(PATCH_TYPE, "tapToMove"), prefix: new HarmonyMethod(this.GetType(), nameof(prefix_get_tapToMove)));
        }

        private static bool prefix_get_tapToMove(GameLocation __instance)
        {
            if (tapToMoveProperty.GetValue(__instance) == null)
            {
                object TapToMove = typeof(IClickableMenu).Assembly.GetType("StardewValley.Mobile.TapToMove").CreateInstance<object>(new object[] { __instance });
                tapToMoveProperty.SetValue(__instance, TapToMove);
            }
            return true;
        }
    }
}

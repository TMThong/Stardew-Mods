using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMod.Shared.FastHarmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerMod.Framework.Patch.Mobile
{
    internal class ShippingMenuPatch : IPatch
    {

        private readonly static Dictionary<ShippingMenu, bool> ShippingMenu_activated = new Dictionary<ShippingMenu, bool>();

        public Type TypePatch => typeof(ShippingMenu);

        public void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Constructor(TypePatch, new Type[] { typeof(IList<Item>) }), new HarmonyMethod(this.GetType(), nameof(postfix_ctor)));
            harmony.Patch(AccessTools.Method(TypePatch, "update", new Type[] { typeof(GameTime) }), prefix: new HarmonyMethod(this.GetType(), nameof(prefix_update)));
        }

        private static void postfix_ctor(IList<Item> items, ShippingMenu __instance)
        {
            ShippingMenu_activated[__instance] = false;
        }

        private static bool prefix_update(GameTime time, ShippingMenu __instance)
        {
            if (ShippingMenu_activated.ContainsKey(__instance))
            {
                ShippingMenu_activated.Clear();
                Game1.player.team.endOfNightStatus.UpdateState("shipment");
            }

            if (Game1.activeClickableMenu != __instance)
            {
                return true;
            }
            bool savedYet = ModUtilities.Helper.Reflection.GetField<bool>(__instance, "savedYet").GetValue();
            if (savedYet)
            {
                if (Game1.PollForEndOfNewDaySync())
                {
                    __instance.exitThisMenu(playSound: false);
                }

                return false;
            }
            return true;
        }
    }
}

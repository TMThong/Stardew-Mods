﻿
using HarmonyLib;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMod.Shared.FastHarmony;
using System;

namespace MultiplayerMod.Framework.Patch.Mobile
{
    internal class MobileCustomizerPatch : IPatch
    {

        public MobileCustomizerPatch()
        {
            TypePatch = typeof(IClickableMenu).Assembly.GetType("StardewValley.Menus.MobileCustomizer");
        }

        public void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(TypePatch, "receiveLeftClick", new Type[] { typeof(int), typeof(int), typeof(bool) }), postfix: new HarmonyMethod(this.GetType(), nameof(postfix_receiveLeftClick)));
            harmony.Patch(AccessTools.Method(TypePatch, "optionButtonClick", new Type[] { typeof(string) }), postfix: new HarmonyMethod(this.GetType(), nameof(postfix_optionButtonClick)));
            harmony.Patch(AccessTools.Constructor(TypePatch), postfix: new HarmonyMethod(this.GetType(), nameof(postfix_ctor)));
        }

        public static bool skipIntro { get; internal set; } = false;

        public Type TypePatch { get; }

        private static void postfix_receiveLeftClick(object __instance, int x, int y, bool playSound = true)
        {
            skipIntro = ModUtilities.Helper.Reflection.GetField<bool>(__instance, "skipIntro").GetValue();
        }
        private static void postfix_ctor(object __instance)
        {
            skipIntro = false;
        }
        private static void postfix_optionButtonClick(string name)
        {
            if (name == "OK")
            {
                if (Game1.client != null)
                {
                    Game1.player.isCustomized.Value = true;
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using MultiplayerMod.Framework.Network;
using StardewValley.Network;
using StardewValleyMod.Shared.FastHarmony;
using LidgrenServer = MultiplayerMod.Framework.Network.LidgrenServer;

namespace MultiplayerMod.Framework.Patch
{
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    internal class GameServerPatch : IPatch
    {
        public Type TypePatch => typeof(GameServer);

        public void Apply(Harmony harmonyInstance)
        {
            harmonyInstance.Patch(AccessTools.Constructor(TypePatch, new Type[] { typeof(bool) }), postfix: new HarmonyMethod(AccessTools.Method(this.GetType(), nameof(Postfix_contruction))));
        }
        private static void Postfix_contruction(GameServer __instance)
        {
            List<Server> servers = ModUtilities.Helper.Reflection.GetField<List<Server>>(__instance, "servers").GetValue();           
            LidgrenServer lidgrenServer = new LidgrenServer(__instance);
            servers.Add(ModUtilities.multiplayer.InitServer(lidgrenServer));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}

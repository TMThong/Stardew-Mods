using HarmonyLib;
using StardewValley;
using StardewValleyMod.Shared.FastHarmony;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerMod.Framework.Patch.Mobile
{
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    internal class SaveGamePatch : IPatch
    {

        public Type TypePatch => typeof(SaveGame);

        public SaveGamePatch() { }

        public static void deleteEmergencySaveIfCalled(object obj)
        {
            ModUtilities.Helper.Reflection.GetMethod(typeof(SaveGame), "deleteEmergencySaveIfCalled").Invoke(obj);
        }
        public static bool newerBackUpExists(string obj)
        {
            return ModUtilities.Helper.Reflection.GetMethod(typeof(SaveGame), "newerBackUpExists").Invoke<bool>(obj);
        }
        public static void Load(string filename, bool loadEmergencySave = false, bool loadBackupSave = false)
        {
            ModUtilities.Helper.Reflection.GetMethod(typeof(SaveGame), "Load").Invoke(filename, loadEmergencySave, loadBackupSave);
        }

        private static void postfix_getLoadEnumerator(string file)
        {
            if (Game1.multiplayerMode == 2)
            {
                Game1.options.setServerMode("friends");
            }
        }

        public void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(TypePatch, "getLoadEnumerator"), postfix: new HarmonyMethod(this.GetType() , nameof(postfix_getLoadEnumerator)));
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}

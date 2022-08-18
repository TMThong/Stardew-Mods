using StardewValley;
using StardewValley.Network;
using StardewValley.SDKs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerMod.Framework.Patch.Mobile
{
    internal class ProgramPatch
    {
        public static SDKHelper sdk
        {
            get
            {
                return ModUtilities.Helper.Reflection.GetField<SDKHelper>(typeof(Program), "sdk").GetValue();
            }
            set
            {
                ModUtilities.Helper.Reflection.GetField<SDKHelper>(typeof(Program), "sdk").SetValue(value);
            }
        }       
    }
}

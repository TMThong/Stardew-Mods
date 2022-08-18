using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerMod.Framework.Patch.Mobile
{
    public class Game1Patch : IPatch
    {
        public static IReflectedField<int> xEdgeField;
        public static IReflectedField<string> savesPathField;
        public static IReflectedField<Texture2D> mobileSpriteSheetField;
        public Game1Patch()
        {
            xEdgeField = ModUtilities.Helper.Reflection.GetField<int>(typeof(Game1), "xEdge");
            savesPathField = ModUtilities.Helper.Reflection.GetField<string>(typeof(Game1), "savesPath");
            mobileSpriteSheetField = ModUtilities.Helper.Reflection.GetField<Texture2D>(typeof(Game1), "mobileSpriteSheet");
        }

        public static Texture2D mobileSpriteSheet
        {
            get
            {
                return mobileSpriteSheetField.GetValue();
            }
            set { mobileSpriteSheetField.SetValue(value); }
        }

        public static int xEdge
        {
            get
            {
                return xEdgeField.GetValue();
            }
            set
            {
                xEdgeField.SetValue(value);
            }
        }
        public static string savesPath
        {
            get
            {
                return savesPathField.GetValue();
            }
            set
            {
                savesPathField.SetValue(value);
            }
        }

        public void Apply(Harmony harmony)
        {

        }
    }

}

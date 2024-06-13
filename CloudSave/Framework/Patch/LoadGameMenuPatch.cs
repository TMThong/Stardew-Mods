using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using StardewValleyMod.Shared.FastHarmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudSave.Framework.Patch
{
    internal class LoadGameMenuPatch : LoadGameMenu, IPatch
    {
        public Type TypePatch => typeof(LoadGameMenu);

        public void Apply(Harmony harmony)
        {
             
        }

        public class DownloadSaveGameButton : LoadGameMenu.MenuSlot
        {
            private string message;

            private DownloadSaveGameButton(LoadGameMenu menu) : base(menu)
            {
            }

            public DownloadSaveGameButton(string message, LoadGameMenu menu) : this(menu)
            {
                this.message = message;
                if (this.message == null)
                    throw new ArgumentNullException(nameof(message));
            }

            public override void Activate()
            {
                 
            }

            public override void Draw(SpriteBatch b, int i)
            {
                int widthOfString = SpriteText.getWidthOfString(message);
                int heightOfString = SpriteText.getHeightOfString(message);
                Rectangle bounds = menu.slotButtons[i].bounds;
                int x = bounds.X + (bounds.Width - widthOfString) / 2;
                int y = bounds.Y + (bounds.Height - heightOfString) / 2;
                SpriteText.drawString(b, message, x, y);
            }
        }
    }
}

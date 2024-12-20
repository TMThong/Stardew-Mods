﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Ping
{
    public class ModEntry : Mod
    {

        public Config Config { get; set; }

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<Config>();
            helper.Events.Display.RenderedHud += OnRenderedHud;
        }

        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            if(Game1.IsClient)
            {
                if(Game1.client != null)
                {
                    SpriteFont smallFont = Game1.smallFont;
                    string txt = $"Ping: {(int)Game1.client.GetPingToHost()} ms";
                    Vector2 size = smallFont.MeasureString(txt);
                    Game1.DrawBox(8, 8, (int)size.X, (int)size.Y);
                    e.SpriteBatch.DrawString(smallFont, txt, new Vector2(8, 8), Color.Black);
                }
            }
        }
    }
}

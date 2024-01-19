using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ThongUICore.Framework.Renderer
{
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    public abstract class BaseRenderer
    {
        public abstract void Update(GameTime gameTime);


        public abstract void Draw(SpriteBatch spriteBatch, int x, int y, int width, int height);


        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}

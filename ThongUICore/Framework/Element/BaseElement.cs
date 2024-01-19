using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley.Menus;
namespace ThongUICore.Framework.Element
{
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    public abstract class BaseElement : OptionsElement
    {
        public BaseElement(string label) : base(label)
        {
        }

        public BaseElement(string label, Rectangle bounds, int whichOption) : base(label, bounds, whichOption)
        {
        }

        public BaseElement(string label, int x, int y, int width, int height, int whichOption = -1) : base(label, x, y, width, height, whichOption)
        {
        }

        public override void draw(SpriteBatch b, int slotX, int slotY, IClickableMenu context = null)
        {
            base.draw(b, slotX, slotY, context);
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override void leftClickHeld(int x, int y)
        {
            base.leftClickHeld(x, y);
        }

        public override void leftClickReleased(int x, int y)
        {
            base.leftClickReleased(x, y);
        }

        public override void receiveKeyPress(Keys key)
        {
            base.receiveKeyPress(key);
        }

        public override void receiveLeftClick(int x, int y)
        {
            base.receiveLeftClick(x, y);
        }

        public override string ToString()
        {
            return base.ToString();
        }

        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}

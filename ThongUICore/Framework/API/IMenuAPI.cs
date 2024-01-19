using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ThongUICore.Framework.API
{
    public interface IMenuAPI
    {
        public IClickableMenu Create();
        public IClickableMenu Create(int x, int y, int width, int height, bool showUpperRightCloseButton = false);

        public IClickableMenu AddView(OptionsElement optionsElement);

        
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ThongUICore.Framework.Manager
{
    public class MenuManager
    {
        public Dictionary<string, int> ModOptionIdentify { get; } = new Dictionary<string, int>();

        public int CreateWhichOptionId(string menuId)
        {
            if (ModOptionIdentify.ContainsKey(menuId))
            {
                ModOptionIdentify[menuId]++;
                return ModOptionIdentify[menuId];
            }

            ModOptionIdentify[menuId] = 0;
            return 0;
        }

        public bool ReleaseWhichOption(string menuId)
        {
            return ModOptionIdentify.Remove(menuId);
        }
    }
}

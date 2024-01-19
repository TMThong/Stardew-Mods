using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ThongUICore.Framework.Manager
{
    public static class ThongUIManager
    {
        public static readonly MenuManager MenuManager = new MenuManager();
        public static IMonitor Monitor;
        public static IModHelper ModHelper;
    }
}

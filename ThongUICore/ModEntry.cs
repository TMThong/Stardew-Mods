using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace ThongUICore
{
    public class ModEntry : Mod
    {
        internal static Config config;
        internal ITranslationHelper i18n => Helper.Translation;

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<Config>();
        }
    }
}

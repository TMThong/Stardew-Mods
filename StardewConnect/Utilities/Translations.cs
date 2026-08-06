using StardewModdingAPI;

namespace StardewConnect.Utilities
{
    /// <summary>Thin static wrapper around SMAPI's translation helper so menus do not have to carry it around.</summary>
    internal static class Translations
    {
        private static ITranslationHelper helper;

        public static void Initialise(ITranslationHelper translations)
        {
            helper = translations;
        }

        public static string Get(string key)
        {
            return helper == null ? key : helper.Get(key).ToString();
        }

        public static string Get(string key, object tokens)
        {
            return helper == null ? key : helper.Get(key, tokens).ToString();
        }
    }
}

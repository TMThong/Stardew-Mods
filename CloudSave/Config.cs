using StardewModdingAPI;

namespace CloudSave
{
    internal class Config
    {
        public SButton debugKey { get; set; }

        public Config()
        {
            debugKey = SButton.J;
        }
    }
}

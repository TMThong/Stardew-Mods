using Microsoft.Xna.Framework.Content;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XNBArchive
{
    internal class FakeContentManager : ContentManager
    {
        public FakeContentManager(IServiceProvider serviceProvider, string rootDirectory) : base(serviceProvider, rootDirectory)
        {

        }

        protected override Stream OpenStream(string assetName)
        {
            return File.OpenRead(assetName);
        }
    }
}

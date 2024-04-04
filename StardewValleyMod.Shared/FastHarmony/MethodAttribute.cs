using System;
using System.Collections.Generic;
using System.Text;

namespace StardewValleyMod.Shared.FastHarmony
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class MethodPatchAttribute : System.Attribute
    {
        public Type TypePatch {  get; }

    }
}

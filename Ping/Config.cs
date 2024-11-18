using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ping
{
    public class Config
    {
        public Color PingFast { get; set; } = Color.Green;

        public Color PingNormal { get; set; } = Color.Yellow;

        public Color PingWeak { get; set; } = Color.Orange;

        public Color PingVeryWeak { get; set; } = Color.Red;
    }
}

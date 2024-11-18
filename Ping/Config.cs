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
        /// <summary>
        /// For pings between 0 and 50 ms
        /// </summary>
        public Color PingFast { get; set; } = Color.Green;
        /// <summary>
        /// For pings between 51 and 150 ms
        /// </summary>
        public Color PingNormal { get; set; } = Color.Yellow;
        /// <summary>
        /// For pings between 151 and 300 ms
        /// </summary>
        public Color PingWeak { get; set; } = Color.Orange;
        /// <summary>
        /// For pings greater than 301 ms
        /// </summary>
        public Color PingVeryWeak { get; set; } = Color.Red;
    }
}

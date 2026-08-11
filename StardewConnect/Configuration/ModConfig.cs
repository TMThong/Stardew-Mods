using System;
using System.Collections.Generic;
using StardewModdingAPI;

namespace StardewConnect.Configuration
{
    /// <summary>User editable settings, persisted to <c>config.json</c> by SMAPI.</summary>
    internal class ModConfig
    {
        /// <summary>Signaling server URL. Must be <c>wss://</c> outside of local development.</summary>
        public string SignalingServerUrl { get; set; } = "wss://rtc.thongdev.com";

        /// <summary>Opens the Stardew Connect menu.</summary>
        public SButton OpenMenuKey { get; set; } = SButton.F9;

        /// <summary>Maximum number of players in a hosted room, including the host.</summary>
        public int MaxPlayers { get; set; } = 8;

        /// <summary>Seconds to wait for the WebSocket handshake before giving up.</summary>
        public int ConnectTimeoutSeconds { get; set; } = 15;

        /// <summary>Seconds to wait for a <c>room_created</c> / <c>room_joined</c> reply.</summary>
        public int RequestTimeoutSeconds { get; set; } = 20;

        /// <summary>Seconds between application level ping messages.</summary>
        public int HeartbeatIntervalSeconds { get; set; } = 20;

        /// <summary>Reconnect the signaling socket automatically while idle (never re-creates a room).</summary>
        public bool AutoReconnectSignaling { get; set; } = true;

        /// <summary>Maximum reconnect attempts before the session is marked as failed.</summary>
        public int MaxReconnectAttempts { get; set; } = 5;

        /// <summary>Verbose networking logs. SDP bodies and passwords are never logged regardless.</summary>
        public bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// Use the in-memory mock transport instead of real WebRTC. Useful to exercise the
        /// signaling flow without a working ICE path.
        /// </summary>
        public bool UseMockWebRtcTransport { get; set; } = false;

        /// <summary>STUN/TURN servers used for ICE gathering.</summary>
        public List<IceServerConfig> IceServers { get; set; } = new List<IceServerConfig>
        {
            new IceServerConfig { Urls = "stun:stun.l.google.com:19302" },
            new IceServerConfig { Urls = "stun:stun1.l.google.com:19302" }
        };

        /// <summary>Clamps values that would otherwise break the networking layer.</summary>
        public void Normalise()
        {
            if (string.IsNullOrWhiteSpace(this.SignalingServerUrl))
                this.SignalingServerUrl = "wss://localhost:8080";
            this.SignalingServerUrl = this.SignalingServerUrl.Trim();

            this.MaxPlayers = Clamp(this.MaxPlayers, 2, 64);
            this.ConnectTimeoutSeconds = Clamp(this.ConnectTimeoutSeconds, 3, 120);
            this.RequestTimeoutSeconds = Clamp(this.RequestTimeoutSeconds, 3, 120);
            this.HeartbeatIntervalSeconds = Clamp(this.HeartbeatIntervalSeconds, 5, 300);
            this.MaxReconnectAttempts = Clamp(this.MaxReconnectAttempts, 0, 50);
            this.IceServers ??= new List<IceServerConfig>();
        }

        /// <summary>True when the configured URL is a plaintext socket to a non-local host.</summary>
        public bool IsInsecureRemoteUrl()
        {
            if (!Uri.TryCreate(this.SignalingServerUrl, UriKind.Absolute, out Uri uri))
                return false;
            if (!string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase))
                return false;

            string host = uri.Host;
            return !(host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("127.0.0.1", StringComparison.Ordinal)
                || host.Equals("::1", StringComparison.Ordinal));
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            return value > max ? max : value;
        }
    }

    /// <summary>A single ICE server entry.</summary>
    internal class IceServerConfig
    {
        /// <summary>For example <c>stun:stun.l.google.com:19302</c> or <c>turn:turn.example.com:3478</c>.</summary>
        public string Urls { get; set; } = string.Empty;

        /// <summary>TURN username. Leave empty for STUN.</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// TURN credential. Prefer short lived credentials fetched at runtime over long lived
        /// secrets committed anywhere near the mod.
        /// </summary>
        public string Credential { get; set; } = string.Empty;
    }
}

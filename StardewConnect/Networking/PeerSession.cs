using System;

namespace StardewConnect.Networking
{
    /// <summary>Lifecycle of one peer-to-peer link, mirroring the WebRTC connection states.</summary>
    public enum PeerConnectionState
    {
        New,
        CreatingOffer,
        WaitingForAnswer,
        Negotiating,
        Connecting,
        Connected,
        Disconnected,
        Failed,
        Closed
    }

    /// <summary>
    /// Everything the mod knows about one remote peer: who they are, how far the WebRTC
    /// handshake got, and whether the data channel is usable.
    /// </summary>
    internal sealed class PeerSession
    {
        public PeerSession(string peerId, bool isHost)
        {
            this.PeerId = peerId ?? string.Empty;
            this.IsHost = isHost;
            this.JoinedAt = DateTimeOffset.UtcNow;
        }

        public string PeerId { get; }

        /// <summary>True when this peer is the room host.</summary>
        public bool IsHost { get; }

        public PeerConnectionState State { get; set; } = PeerConnectionState.New;

        /// <summary>True once the "stardew-connect" data channel is open in both directions.</summary>
        public bool IsDataChannelOpen { get; set; }

        public DateTimeOffset JoinedAt { get; }

        public DateTimeOffset? ConnectedAt { get; set; }

        /// <summary>Round trip time measured over the data channel, or null before the first sample.</summary>
        public TimeSpan? RoundTripTime { get; set; }

        /// <summary>Last failure reported for this peer, shown in the host menu.</summary>
        public string LastError { get; set; } = string.Empty;

        /// <summary>Short id used in the UI, e.g. "a1b2c3d4".</summary>
        public string ShortId => this.PeerId.Length >= 8 ? this.PeerId.Substring(0, 8) : this.PeerId;

        public bool IsUsable => this.State == PeerConnectionState.Connected && this.IsDataChannelOpen;

        public void MarkConnected()
        {
            this.State = PeerConnectionState.Connected;
            this.IsDataChannelOpen = true;
            this.ConnectedAt ??= DateTimeOffset.UtcNow;
            this.LastError = string.Empty;
        }

        public void MarkFailed(string reason)
        {
            this.State = PeerConnectionState.Failed;
            this.IsDataChannelOpen = false;
            this.LastError = reason ?? string.Empty;
        }
    }
}

using System;
using System.Collections.Generic;
using StardewConnect.Networking;

namespace StardewConnect.Rooms
{
    /// <summary>High level state of the whole Stardew Connect session, as shown in the UI.</summary>
    public enum ConnectionState
    {
        Disconnected,
        ConnectingToSignaling,
        CreatingRoom,
        WaitingForPlayers,
        JoiningRoom,
        NegotiatingWebRtc,
        Connected,
        Reconnecting,
        Failed
    }

    /// <summary>Whether this game instance owns the room or joined someone else's.</summary>
    internal enum RoomRole
    {
        None,
        Host,
        Client
    }

    /// <summary>
    /// A snapshot of the current room.
    ///
    /// Only ever mutated on the game's update thread (see <see cref="Utilities.MainThreadDispatcher"/>),
    /// so menus can read it without locking.
    /// </summary>
    internal sealed class RoomState
    {
        private readonly List<PeerSession> peers = new List<PeerSession>();

        public ConnectionState Connection { get; set; } = ConnectionState.Disconnected;

        public RoomRole Role { get; set; } = RoomRole.None;

        public string RoomId { get; set; } = string.Empty;

        /// <summary>
        /// Only populated while hosting, because the host has to show it to invite people.
        /// A joining client clears it the moment the join request has been sent.
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>The peer id the server issued to this connection.</summary>
        public string LocalPeerId { get; set; } = string.Empty;

        public string HostPeerId { get; set; } = string.Empty;

        public int MaxPlayers { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        /// <summary>Short human readable line shown under the title.</summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>Last error shown to the player, or empty.</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        public IReadOnlyList<PeerSession> Peers => this.peers;

        public bool IsHost => this.Role == RoomRole.Host;

        public bool IsInRoom => !string.IsNullOrEmpty(this.RoomId) && !string.IsNullOrEmpty(this.LocalPeerId);

        /// <summary>Total players in the room, including this one.</summary>
        public int PlayerCount => this.peers.Count + (this.IsInRoom ? 1 : 0);

        public PeerSession GetPeer(string peerId)
        {
            if (string.IsNullOrEmpty(peerId))
                return null;

            foreach (PeerSession peer in this.peers)
            {
                if (peer.PeerId == peerId)
                    return peer;
            }

            return null;
        }

        public PeerSession AddOrGetPeer(string peerId, bool isHost)
        {
            PeerSession existing = this.GetPeer(peerId);
            if (existing != null)
                return existing;

            PeerSession peer = new PeerSession(peerId, isHost);
            this.peers.Add(peer);
            return peer;
        }

        public bool RemovePeer(string peerId)
        {
            PeerSession peer = this.GetPeer(peerId);
            if (peer == null)
                return false;

            this.peers.Remove(peer);
            return true;
        }

        public void ClearPeers()
        {
            this.peers.Clear();
        }

        /// <summary>Resets everything, wiping the password from memory.</summary>
        public void Reset()
        {
            this.Connection = ConnectionState.Disconnected;
            this.Role = RoomRole.None;
            this.RoomId = string.Empty;
            this.Password = string.Empty;
            this.LocalPeerId = string.Empty;
            this.HostPeerId = string.Empty;
            this.MaxPlayers = 0;
            this.ExpiresAt = null;
            this.StatusMessage = string.Empty;
            this.peers.Clear();
        }
    }
}

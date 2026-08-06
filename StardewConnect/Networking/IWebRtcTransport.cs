using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StardewConnect.Protocol;

namespace StardewConnect.Networking
{
    /// <summary>Which side of the negotiation produced a description.</summary>
    public enum SessionDescriptionKind
    {
        Offer,
        Answer
    }

    internal sealed class LocalDescriptionEventArgs : EventArgs
    {
        public LocalDescriptionEventArgs(string peerId, SessionDescriptionKind kind, string sdp)
        {
            this.PeerId = peerId;
            this.Kind = kind;
            this.Sdp = sdp;
        }

        public string PeerId { get; }
        public SessionDescriptionKind Kind { get; }
        public string Sdp { get; }
    }

    internal sealed class LocalIceCandidateEventArgs : EventArgs
    {
        public LocalIceCandidateEventArgs(string peerId, IceCandidatePayload candidate)
        {
            this.PeerId = peerId;
            this.Candidate = candidate;
        }

        public string PeerId { get; }
        public IceCandidatePayload Candidate { get; }
    }

    internal sealed class PeerStateChangedEventArgs : EventArgs
    {
        public PeerStateChangedEventArgs(string peerId, PeerConnectionState state, string detail = null)
        {
            this.PeerId = peerId;
            this.State = state;
            this.Detail = detail ?? string.Empty;
        }

        public string PeerId { get; }
        public PeerConnectionState State { get; }
        public string Detail { get; }
    }

    internal sealed class PeerDataEventArgs : EventArgs
    {
        public PeerDataEventArgs(string peerId, byte[] payload)
        {
            this.PeerId = peerId;
            this.Payload = payload;
        }

        public string PeerId { get; }
        public byte[] Payload { get; }
    }

    /// <summary>
    /// The peer-to-peer data path, deliberately kept behind an interface.
    ///
    /// Signaling (see <see cref="ISignalingClient"/>) only carries the strings this interface
    /// produces and consumes, so swapping WebRTC for raw TCP, KCP or ENet later means writing a
    /// new implementation of this one type and nothing else.
    ///
    /// All events may be raised from background threads; callers are responsible for marshalling
    /// onto the game thread.
    /// </summary>
    internal interface IWebRtcTransport : IDisposable
    {
        /// <summary>Name of the single reliable, ordered data channel used for game traffic.</summary>
        string DataChannelLabel { get; }

        /// <summary>Raised when a local offer or answer is ready to be relayed to the remote peer.</summary>
        event EventHandler<LocalDescriptionEventArgs> LocalDescriptionReady;

        /// <summary>Raised for every locally gathered ICE candidate.</summary>
        event EventHandler<LocalIceCandidateEventArgs> LocalIceCandidateReady;

        /// <summary>Raised whenever a peer's connection state changes.</summary>
        event EventHandler<PeerStateChangedEventArgs> PeerStateChanged;

        /// <summary>Raised for every payload received on the data channel.</summary>
        event EventHandler<PeerDataEventArgs> DataReceived;

        /// <summary>Peer ids this transport currently tracks.</summary>
        IReadOnlyCollection<string> ConnectedPeers { get; }

        /// <summary>Host side: create the peer connection and data channel, then emit an offer.</summary>
        Task StartOfferAsync(string peerId, CancellationToken cancellationToken);

        /// <summary>Client side: apply a remote offer and emit the answer.</summary>
        Task AcceptOfferAsync(string peerId, string sdp, CancellationToken cancellationToken);

        /// <summary>Host side: apply the answer that came back from a client.</summary>
        Task AcceptAnswerAsync(string peerId, string sdp, CancellationToken cancellationToken);

        /// <summary>Apply a remote ICE candidate.</summary>
        Task AddIceCandidateAsync(string peerId, IceCandidatePayload candidate, CancellationToken cancellationToken);

        /// <summary>Sends a payload on the data channel. Returns false when the channel is not open.</summary>
        bool Send(string peerId, byte[] payload);

        /// <summary>Current state for a peer, or <see cref="PeerConnectionState.Closed"/> when unknown.</summary>
        PeerConnectionState GetPeerState(string peerId);

        /// <summary>Tears down one peer connection.</summary>
        Task ClosePeerAsync(string peerId);

        /// <summary>Tears down every peer connection.</summary>
        Task CloseAllAsync();
    }
}

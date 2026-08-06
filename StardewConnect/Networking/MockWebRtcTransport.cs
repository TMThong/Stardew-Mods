using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using StardewConnect.Protocol;
using StardewModdingAPI;

namespace StardewConnect.Networking
{
    /// <summary>
    /// An <see cref="IWebRtcTransport"/> that performs the full signaling choreography with
    /// synthetic SDP and ICE payloads instead of a real peer connection.
    ///
    /// It exists to exercise room creation, joining and the offer/answer/ICE relay end to end
    /// without depending on a working ICE path, which is exactly what you want when debugging
    /// the server or a firewall.
    ///
    /// Limitation, by design: <see cref="Send"/> reports success and counts the bytes, but
    /// there is no remote endpoint, so <see cref="DataReceived"/> never fires. Switch
    /// <c>UseMockWebRtcTransport</c> off in config.json for actual gameplay traffic.
    /// </summary>
    internal sealed class MockWebRtcTransport : IWebRtcTransport
    {
        private readonly ConcurrentDictionary<string, MockPeer> peers = new ConcurrentDictionary<string, MockPeer>();
        private readonly IMonitor monitor;
        private bool disposed;

        public MockWebRtcTransport(IMonitor monitor)
        {
            this.monitor = monitor;
        }

        public string DataChannelLabel => "stardew-connect";

        public event EventHandler<LocalDescriptionEventArgs> LocalDescriptionReady;

        public event EventHandler<LocalIceCandidateEventArgs> LocalIceCandidateReady;

        public event EventHandler<PeerStateChangedEventArgs> PeerStateChanged;

        /// <summary>Never raised: the mock has no remote endpoint. Kept to satisfy the interface.</summary>
#pragma warning disable CS0067 // deliberately never invoked - see the class summary
        public event EventHandler<PeerDataEventArgs> DataReceived;
#pragma warning restore CS0067

        public IReadOnlyCollection<string> ConnectedPeers => new List<string>(this.peers.Keys);

        public Task StartOfferAsync(string peerId, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            MockPeer peer = this.peers.GetOrAdd(peerId, id => new MockPeer(id));
            this.SetState(peer, PeerConnectionState.CreatingOffer);

            string sdp = BuildSdp("mock-offer", peerId);
            this.LocalDescriptionReady?.Invoke(this, new LocalDescriptionEventArgs(peerId, SessionDescriptionKind.Offer, sdp));
            this.EmitCandidates(peerId);
            this.SetState(peer, PeerConnectionState.WaitingForAnswer);

            return Task.CompletedTask;
        }

        public Task AcceptOfferAsync(string peerId, string sdp, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sdp))
                throw new ArgumentException("The remote offer is empty.", nameof(sdp));

            MockPeer peer = this.peers.GetOrAdd(peerId, id => new MockPeer(id));
            peer.RemoteDescriptionLength = sdp.Length;
            this.SetState(peer, PeerConnectionState.Negotiating);

            string answer = BuildSdp("mock-answer", peerId);
            this.LocalDescriptionReady?.Invoke(this, new LocalDescriptionEventArgs(peerId, SessionDescriptionKind.Answer, answer));
            this.EmitCandidates(peerId);

            this.SetState(peer, PeerConnectionState.Connecting);
            this.SetState(peer, PeerConnectionState.Connected, "mock data channel open");
            return Task.CompletedTask;
        }

        public Task AcceptAnswerAsync(string peerId, string sdp, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sdp))
                throw new ArgumentException("The remote answer is empty.", nameof(sdp));

            if (!this.peers.TryGetValue(peerId, out MockPeer peer))
                throw new InvalidOperationException($"No pending mock connection for peer {peerId}.");

            peer.RemoteDescriptionLength = sdp.Length;
            this.SetState(peer, PeerConnectionState.Connecting);
            this.SetState(peer, PeerConnectionState.Connected, "mock data channel open");
            return Task.CompletedTask;
        }

        public Task AddIceCandidateAsync(string peerId, IceCandidatePayload candidate, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            MockPeer peer = this.peers.GetOrAdd(peerId, id => new MockPeer(id));
            peer.RemoteCandidateCount++;

            if (candidate != null && candidate.Candidate.Length > 0)
                this.monitor.Log($"[mock] peer {Shorten(peerId)} accepted remote candidate #{peer.RemoteCandidateCount}.", LogLevel.Trace);

            return Task.CompletedTask;
        }

        public bool Send(string peerId, byte[] payload)
        {
            if (this.disposed || payload == null)
                return false;

            if (!this.peers.TryGetValue(peerId, out MockPeer peer) || peer.State != PeerConnectionState.Connected)
                return false;

            peer.BytesSent += payload.Length;
            return true;
        }

        public PeerConnectionState GetPeerState(string peerId)
        {
            return this.peers.TryGetValue(peerId, out MockPeer peer) ? peer.State : PeerConnectionState.Closed;
        }

        public Task ClosePeerAsync(string peerId)
        {
            if (this.peers.TryRemove(peerId, out MockPeer peer))
            {
                peer.State = PeerConnectionState.Closed;
                this.PeerStateChanged?.Invoke(this, new PeerStateChangedEventArgs(peerId, PeerConnectionState.Closed, "closed"));
            }

            return Task.CompletedTask;
        }

        public Task CloseAllAsync()
        {
            foreach (string peerId in new List<string>(this.peers.Keys))
                this.ClosePeerAsync(peerId).GetAwaiter().GetResult();

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (this.disposed)
                return;

            this.disposed = true;
            this.peers.Clear();
        }

        // ------------------------------------------------------------------ helpers

        private void EmitCandidates(string peerId)
        {
            // Two candidates is enough to prove the relay works in both directions.
            this.LocalIceCandidateReady?.Invoke(this, new LocalIceCandidateEventArgs(peerId,
                new IceCandidatePayload("candidate:1 1 udp 2130706431 127.0.0.1 50000 typ host", "0", 0)));
            this.LocalIceCandidateReady?.Invoke(this, new LocalIceCandidateEventArgs(peerId,
                new IceCandidatePayload("candidate:2 1 udp 1694498815 192.0.2.1 50001 typ srflx", "0", 0)));
        }

        private void SetState(MockPeer peer, PeerConnectionState state, string detail = null)
        {
            peer.State = state;
            this.PeerStateChanged?.Invoke(this, new PeerStateChangedEventArgs(peer.PeerId, state, detail));
        }

        private static string BuildSdp(string tag, string peerId)
        {
            string sessionId = Math.Abs(peerId.GetHashCode()).ToString(CultureInfo.InvariantCulture);
            return string.Join("\r\n",
                "v=0",
                $"o=- {sessionId} 2 IN IP4 127.0.0.1",
                $"s={tag}",
                "t=0 0",
                "a=group:BUNDLE 0",
                "m=application 9 UDP/DTLS/SCTP webrtc-datachannel",
                "c=IN IP4 0.0.0.0",
                "a=mid:0",
                "a=sctp-port:5000",
                "a=label:stardew-connect",
                string.Empty);
        }

        private static string Shorten(string peerId)
        {
            return peerId != null && peerId.Length >= 8 ? peerId.Substring(0, 8) : peerId ?? string.Empty;
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
                throw new ObjectDisposedException(nameof(MockWebRtcTransport));
        }

        private sealed class MockPeer
        {
            public MockPeer(string peerId)
            {
                this.PeerId = peerId;
            }

            public string PeerId { get; }
            public PeerConnectionState State { get; set; } = PeerConnectionState.New;
            public int RemoteCandidateCount { get; set; }
            public int RemoteDescriptionLength { get; set; }
            public long BytesSent { get; set; }
        }
    }
}

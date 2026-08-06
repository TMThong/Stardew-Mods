using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;
using StardewConnect.Configuration;
using StardewConnect.Protocol;
using StardewModdingAPI;

namespace StardewConnect.Networking
{
    /// <summary>
    /// Real WebRTC transport, backed by SIPSorcery.
    ///
    /// One <see cref="RTCPeerConnection"/> per remote peer, each carrying a single reliable,
    /// ordered data channel called <c>stardew-connect</c>. The host creates the channel and
    /// sends the offer; the client receives the channel through <c>ondatachannel</c>.
    ///
    /// Every SIPSorcery callback arrives on a library thread. This class stays away from game
    /// state entirely and simply re-raises the events; <see cref="Rooms.RoomSession"/> is what
    /// marshals them onto the game thread.
    /// </summary>
    internal sealed class WebRtcTransport : IWebRtcTransport
    {
        /// <summary>SCTP data channels are reliable but not designed for huge single messages.</summary>
        private const int MaxMessageBytes = 256 * 1024;

        private readonly ConcurrentDictionary<string, PeerLink> links = new ConcurrentDictionary<string, PeerLink>();

        /// <summary>
        /// ICE candidates that arrived before the peer connection they belong to existed.
        /// Trickle ICE makes this normal, and dropping them can cost the only viable path.
        /// </summary>
        private readonly ConcurrentDictionary<string, List<IceCandidatePayload>> earlyCandidates =
            new ConcurrentDictionary<string, List<IceCandidatePayload>>();

        private readonly IMonitor monitor;
        private readonly ModConfig config;
        private bool disposed;

        public WebRtcTransport(IMonitor monitor, ModConfig config)
        {
            this.monitor = monitor;
            this.config = config;
        }

        public string DataChannelLabel => "stardew-connect";

        public event EventHandler<LocalDescriptionEventArgs> LocalDescriptionReady;

        public event EventHandler<LocalIceCandidateEventArgs> LocalIceCandidateReady;

        public event EventHandler<PeerStateChangedEventArgs> PeerStateChanged;

        public event EventHandler<PeerDataEventArgs> DataReceived;

        public IReadOnlyCollection<string> ConnectedPeers
        {
            get
            {
                List<string> connected = new List<string>();
                foreach (KeyValuePair<string, PeerLink> entry in this.links)
                {
                    if (entry.Value.Channel != null && entry.Value.Channel.IsOpened)
                        connected.Add(entry.Key);
                }

                return connected;
            }
        }

        // ------------------------------------------------------------------ negotiation

        public async Task StartOfferAsync(string peerId, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            PeerLink link = this.CreateLink(peerId);
            this.Report(link, PeerConnectionState.CreatingOffer);

            RTCDataChannel channel = await link.Connection
                .createDataChannel(this.DataChannelLabel, new RTCDataChannelInit { ordered = true })
                .ConfigureAwait(false);

            this.AttachChannel(link, channel);

            cancellationToken.ThrowIfCancellationRequested();

            RTCSessionDescriptionInit offer = link.Connection.createOffer(null);
            await link.Connection.setLocalDescription(offer).ConfigureAwait(false);

            this.monitor.Log($"WebRTC: sending offer to peer {Shorten(peerId)}.", LogLevel.Debug);
            this.LocalDescriptionReady?.Invoke(this, new LocalDescriptionEventArgs(peerId, SessionDescriptionKind.Offer, offer.sdp));
            this.Report(link, PeerConnectionState.WaitingForAnswer);
        }

        public async Task AcceptOfferAsync(string peerId, string sdp, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sdp))
                throw new ArgumentException("The remote offer is empty.", nameof(sdp));

            PeerLink link = this.CreateLink(peerId);
            this.Report(link, PeerConnectionState.Negotiating);

            SetDescriptionResultEnum result = link.Connection.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.offer,
                sdp = sdp
            });

            if (result != SetDescriptionResultEnum.OK)
            {
                this.Fail(link, $"the remote offer was rejected ({result})");
                throw new InvalidOperationException($"Could not apply the offer from peer {Shorten(peerId)}: {result}.");
            }

            link.MarkRemoteDescriptionSet();
            this.DrainPendingCandidates(link);

            cancellationToken.ThrowIfCancellationRequested();

            RTCSessionDescriptionInit answer = link.Connection.createAnswer(null);
            await link.Connection.setLocalDescription(answer).ConfigureAwait(false);

            this.monitor.Log($"WebRTC: answering peer {Shorten(peerId)}.", LogLevel.Debug);
            this.LocalDescriptionReady?.Invoke(this, new LocalDescriptionEventArgs(peerId, SessionDescriptionKind.Answer, answer.sdp));
            this.Report(link, PeerConnectionState.Connecting);
        }

        public Task AcceptAnswerAsync(string peerId, string sdp, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sdp))
                throw new ArgumentException("The remote answer is empty.", nameof(sdp));

            if (!this.links.TryGetValue(peerId, out PeerLink link))
                throw new InvalidOperationException($"No pending peer connection for {Shorten(peerId)}.");

            SetDescriptionResultEnum result = link.Connection.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = sdp
            });

            if (result != SetDescriptionResultEnum.OK)
            {
                this.Fail(link, $"the remote answer was rejected ({result})");
                throw new InvalidOperationException($"Could not apply the answer from peer {Shorten(peerId)}: {result}.");
            }

            link.MarkRemoteDescriptionSet();
            this.DrainPendingCandidates(link);
            this.Report(link, PeerConnectionState.Connecting);

            return Task.CompletedTask;
        }

        public Task AddIceCandidateAsync(string peerId, IceCandidatePayload candidate, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Candidate))
                return Task.CompletedTask;

            // No peer connection yet: park the candidate until the offer/answer creates one.
            if (!this.links.TryGetValue(peerId, out PeerLink link))
            {
                List<IceCandidatePayload> parked = this.earlyCandidates.GetOrAdd(peerId, _ => new List<IceCandidatePayload>());
                lock (parked)
                    parked.Add(candidate);
                return Task.CompletedTask;
            }

            // The connection exists but may not have a remote description yet.
            if (!link.QueueOrApply(candidate, out IceCandidatePayload ready))
                return Task.CompletedTask;

            this.Apply(link, ready);
            return Task.CompletedTask;
        }

        // ------------------------------------------------------------------ data

        public bool Send(string peerId, byte[] payload)
        {
            if (this.disposed || payload == null)
                return false;

            if (payload.Length > MaxMessageBytes)
                throw new ArgumentException($"Payload of {payload.Length} bytes exceeds the {MaxMessageBytes} byte data channel limit.", nameof(payload));

            if (!this.links.TryGetValue(peerId, out PeerLink link))
                return false;

            RTCDataChannel channel = link.Channel;
            if (channel == null || !channel.IsOpened)
                return false;

            try
            {
                channel.send(payload);
                return true;
            }
            catch (Exception error)
            {
                this.monitor.Log($"WebRTC: send to peer {Shorten(peerId)} failed: {error.Message}", LogLevel.Warn);
                return false;
            }
        }

        public PeerConnectionState GetPeerState(string peerId)
        {
            return this.links.TryGetValue(peerId, out PeerLink link) ? link.State : PeerConnectionState.Closed;
        }

        // ------------------------------------------------------------------ teardown

        public Task ClosePeerAsync(string peerId)
        {
            if (this.links.TryRemove(peerId, out PeerLink link))
            {
                link.Dispose();
                this.PeerStateChanged?.Invoke(this, new PeerStateChangedEventArgs(peerId, PeerConnectionState.Closed, "closed"));
            }

            return Task.CompletedTask;
        }

        public Task CloseAllAsync()
        {
            foreach (string peerId in new List<string>(this.links.Keys))
            {
                if (this.links.TryRemove(peerId, out PeerLink link))
                {
                    link.Dispose();
                    this.PeerStateChanged?.Invoke(this, new PeerStateChangedEventArgs(peerId, PeerConnectionState.Closed, "closed"));
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (this.disposed)
                return;

            this.disposed = true;

            foreach (string peerId in new List<string>(this.links.Keys))
            {
                if (this.links.TryRemove(peerId, out PeerLink link))
                    link.Dispose();
            }
        }

        // ------------------------------------------------------------------ wiring

        private PeerLink CreateLink(string peerId)
        {
            // A renegotiation for a peer we already know about starts from a clean connection,
            // but any candidates it had buffered still belong to this peer.
            if (this.links.TryRemove(peerId, out PeerLink stale))
            {
                this.ParkCandidates(peerId, stale.TakePendingCandidates());
                stale.Retire();
                stale.Dispose();
            }

            PeerLink link = this.CreateLinkCore(peerId);
            this.links[peerId] = link;

            if (this.earlyCandidates.TryRemove(peerId, out List<IceCandidatePayload> parked))
            {
                lock (parked)
                    link.AddPendingCandidates(parked);
            }

            return link;
        }

        private void ParkCandidates(string peerId, IReadOnlyList<IceCandidatePayload> candidates)
        {
            if (candidates.Count == 0)
                return;

            List<IceCandidatePayload> parked = this.earlyCandidates.GetOrAdd(peerId, _ => new List<IceCandidatePayload>());
            lock (parked)
                parked.AddRange(candidates);
        }

        private PeerLink CreateLinkCore(string peerId)
        {
            RTCPeerConnection connection = new RTCPeerConnection(this.BuildConfiguration());
            PeerLink link = new PeerLink(peerId, connection);

            connection.onicecandidate += iceCandidate =>
            {
                if (iceCandidate == null || string.IsNullOrWhiteSpace(iceCandidate.candidate))
                    return;

                IceCandidatePayload payload = new IceCandidatePayload(
                    iceCandidate.candidate,
                    iceCandidate.sdpMid,
                    iceCandidate.sdpMLineIndex);

                this.LocalIceCandidateReady?.Invoke(this, new LocalIceCandidateEventArgs(peerId, payload));
            };

            connection.onicecandidateerror += (candidate, error) =>
                this.monitor.Log($"WebRTC: ICE error for peer {Shorten(peerId)}: {error}", LogLevel.Debug);

            connection.onconnectionstatechange += state =>
            {
                this.monitor.Log($"WebRTC: peer {Shorten(peerId)} is now {state}.", LogLevel.Debug);

                switch (state)
                {
                    case RTCPeerConnectionState.connected:
                        // Wait for the data channel: a connected transport with a closed
                        // channel is not usable yet.
                        if (link.Channel != null && link.Channel.IsOpened)
                            this.Report(link, PeerConnectionState.Connected, "data channel open");
                        else
                            this.Report(link, PeerConnectionState.Connecting, "transport connected");
                        break;

                    case RTCPeerConnectionState.connecting:
                        this.Report(link, PeerConnectionState.Connecting);
                        break;

                    case RTCPeerConnectionState.disconnected:
                        this.Report(link, PeerConnectionState.Disconnected, "transport disconnected");
                        break;

                    case RTCPeerConnectionState.failed:
                        this.Fail(link, "the peer connection failed (ICE could not find a path)");
                        break;

                    case RTCPeerConnectionState.closed:
                        this.Report(link, PeerConnectionState.Closed);
                        break;
                }
            };

            // Client side: the host creates the channel, we receive it here.
            connection.ondatachannel += channel => this.AttachChannel(link, channel);

            return link;
        }

        private void AttachChannel(PeerLink link, RTCDataChannel channel)
        {
            if (channel == null)
                return;

            if (!string.Equals(channel.label, this.DataChannelLabel, StringComparison.Ordinal))
            {
                this.monitor.Log($"WebRTC: ignoring unexpected data channel '{channel.label}' from peer {Shorten(link.PeerId)}.", LogLevel.Warn);
                return;
            }

            link.Channel = channel;

            channel.onopen += () =>
            {
                this.monitor.Log($"WebRTC: data channel open with peer {Shorten(link.PeerId)}.", LogLevel.Info);
                this.Report(link, PeerConnectionState.Connected, "data channel open");
            };

            channel.onclose += () =>
            {
                this.monitor.Log($"WebRTC: data channel closed with peer {Shorten(link.PeerId)}.", LogLevel.Debug);
                this.Report(link, PeerConnectionState.Disconnected, "data channel closed");
            };

            channel.onerror += error => this.Fail(link, $"data channel error: {error}");

            channel.onmessage += (dataChannel, protocol, data) =>
            {
                if (data == null || data.Length == 0)
                    return;

                try
                {
                    this.DataReceived?.Invoke(this, new PeerDataEventArgs(link.PeerId, data));
                }
                catch (Exception error)
                {
                    this.monitor.Log($"A data channel handler threw: {error}", LogLevel.Error);
                }
            };

            if (channel.IsOpened)
                this.Report(link, PeerConnectionState.Connected, "data channel open");
        }

        private void DrainPendingCandidates(PeerLink link)
        {
            foreach (IceCandidatePayload candidate in link.TakePendingCandidates())
                this.Apply(link, candidate);
        }

        private void Apply(PeerLink link, IceCandidatePayload candidate)
        {
            try
            {
                link.Connection.addIceCandidate(new RTCIceCandidateInit
                {
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid ?? "0",
                    sdpMLineIndex = (ushort)Math.Max(0, candidate.SdpMLineIndex ?? 0)
                });
            }
            catch (Exception error)
            {
                this.monitor.Log($"WebRTC: rejected a remote ICE candidate for peer {Shorten(link.PeerId)}: {error.Message}", LogLevel.Debug);
            }
        }

        private RTCConfiguration BuildConfiguration()
        {
            List<RTCIceServer> iceServers = new List<RTCIceServer>();

            foreach (IceServerConfig entry in this.config.IceServers)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Urls))
                    continue;

                RTCIceServer server = new RTCIceServer { urls = entry.Urls.Trim() };
                if (!string.IsNullOrEmpty(entry.Username))
                    server.username = entry.Username;
                if (!string.IsNullOrEmpty(entry.Credential))
                    server.credential = entry.Credential;

                iceServers.Add(server);
            }

            return new RTCConfiguration { iceServers = iceServers };
        }

        private void Report(PeerLink link, PeerConnectionState state, string detail = null)
        {
            if (link.State == state)
                return;

            link.State = state;
            this.PeerStateChanged?.Invoke(this, new PeerStateChangedEventArgs(link.PeerId, state, detail));
        }

        private void Fail(PeerLink link, string detail)
        {
            link.State = PeerConnectionState.Failed;
            this.monitor.Log($"WebRTC: peer {Shorten(link.PeerId)} failed - {detail}", LogLevel.Warn);
            this.PeerStateChanged?.Invoke(this, new PeerStateChangedEventArgs(link.PeerId, PeerConnectionState.Failed, detail));
        }

        private static string Shorten(string peerId)
        {
            return peerId != null && peerId.Length >= 8 ? peerId.Substring(0, 8) : peerId ?? string.Empty;
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
                throw new ObjectDisposedException(nameof(WebRtcTransport));
        }

        /// <summary>One peer connection plus the state needed to sequence the handshake correctly.</summary>
        private sealed class PeerLink : IDisposable
        {
            private readonly List<IceCandidatePayload> pendingCandidates = new List<IceCandidatePayload>();
            private readonly object gate = new object();
            private bool remoteDescriptionSet;

            public PeerLink(string peerId, RTCPeerConnection connection)
            {
                this.PeerId = peerId;
                this.Connection = connection;
            }

            public string PeerId { get; }

            public RTCPeerConnection Connection { get; }

            public RTCDataChannel Channel { get; set; }

            public PeerConnectionState State { get; set; } = PeerConnectionState.New;

            public void MarkRemoteDescriptionSet()
            {
                lock (this.gate)
                    this.remoteDescriptionSet = true;
            }

            /// <summary>
            /// Returns true when the candidate can be applied straight away; otherwise it is
            /// buffered until the remote description arrives.
            /// </summary>
            public bool QueueOrApply(IceCandidatePayload candidate, out IceCandidatePayload ready)
            {
                lock (this.gate)
                {
                    if (this.remoteDescriptionSet)
                    {
                        ready = candidate;
                        return true;
                    }

                    this.pendingCandidates.Add(candidate);
                    ready = null;
                    return false;
                }
            }

            public IReadOnlyList<IceCandidatePayload> TakePendingCandidates()
            {
                lock (this.gate)
                {
                    List<IceCandidatePayload> copy = new List<IceCandidatePayload>(this.pendingCandidates);
                    this.pendingCandidates.Clear();
                    return copy;
                }
            }

            public void Dispose()
            {
                try
                {
                    this.Channel?.close();
                }
                catch (Exception)
                {
                    // the connection is going away anyway
                }

                try
                {
                    this.Connection?.close();
                    this.Connection?.Dispose();
                }
                catch (Exception)
                {
                    // the connection is going away anyway
                }

                this.State = PeerConnectionState.Closed;
            }
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using StardewConnect.Configuration;
using StardewConnect.Networking;
using StardewConnect.Protocol;
using StardewConnect.Utilities;
using StardewModdingAPI;

namespace StardewConnect.Rooms
{
    /// <summary>
    /// Orchestrates one Stardew Connect session: signaling, room lifecycle and the WebRTC
    /// negotiation that follows.
    ///
    /// Threading contract: signaling and transport callbacks arrive on background threads and
    /// are immediately marshalled onto the game thread through
    /// <see cref="MainThreadDispatcher"/>. Everything that touches <see cref="RoomState"/> runs
    /// on the game thread, so the menus can read it without locking.
    /// </summary>
    internal sealed class RoomSession : IDisposable
    {
        private readonly ISignalingClient signaling;
        private readonly IWebRtcTransport transport;
        private readonly NetworkMessageRouter router;
        private readonly MainThreadDispatcher dispatcher;
        private readonly ModConfig config;
        private readonly IMonitor monitor;

        private CancellationTokenSource operationSource;
        private bool disposed;

        public RoomSession(
            ISignalingClient signaling,
            IWebRtcTransport transport,
            NetworkMessageRouter router,
            MainThreadDispatcher dispatcher,
            ModConfig config,
            IMonitor monitor)
        {
            this.signaling = signaling;
            this.transport = transport;
            this.router = router;
            this.dispatcher = dispatcher;
            this.config = config;
            this.monitor = monitor;

            this.signaling.MessageReceived += this.OnSignalingMessage;
            this.signaling.StateChanged += this.OnSignalingStateChanged;

            this.transport.LocalDescriptionReady += this.OnLocalDescriptionReady;
            this.transport.LocalIceCandidateReady += this.OnLocalIceCandidateReady;
            this.transport.PeerStateChanged += this.OnPeerStateChanged;

            this.router.RoundTripMeasured += this.OnRoundTripMeasured;
        }

        /// <summary>Current session state. Only read or written on the game thread.</summary>
        public RoomState State { get; } = new RoomState();

        /// <summary>Raised on the game thread whenever <see cref="State"/> changed.</summary>
        public event EventHandler Changed;

        /// <summary>Raised on the game thread once a room has been created and is waiting for players.</summary>
        public event EventHandler RoomOpened;

        /// <summary>True while a create/join/leave operation is in flight.</summary>
        public bool IsBusy { get; private set; }

        // ------------------------------------------------------------------ commands

        /// <summary>Connects to the signaling server and creates a room.</summary>
        public void StartHosting()
        {
            if (this.IsBusy || this.State.IsInRoom)
                return;

            this.BeginOperation(ConnectionState.ConnectingToSignaling, Translations.Get("status.connecting"));
            this.RunBackground("host", async token =>
            {
                await this.EnsureConnectedAsync(token).ConfigureAwait(false);

                this.dispatcher.Invoke(() =>
                {
                    this.State.Connection = ConnectionState.CreatingRoom;
                    this.State.StatusMessage = Translations.Get("status.creatingRoom");
                    this.RaiseChanged();
                });

                ServerMessage reply = await this.signaling
                    .SendRequestAsync(new CreateRoomRequest(this.config.MaxPlayers), token)
                    .ConfigureAwait(false);

                if (!(reply is RoomCreatedMessage created))
                    throw new InvalidOperationException($"Unexpected reply '{reply.Type}' to create_room.");

                this.dispatcher.Invoke(() =>
                {
                    this.State.Role = RoomRole.Host;
                    this.State.RoomId = created.RoomId;
                    this.State.Password = created.Password;
                    this.State.LocalPeerId = created.PeerId;
                    this.State.HostPeerId = created.PeerId;
                    this.State.MaxPlayers = created.MaxPlayers > 0 ? created.MaxPlayers : this.config.MaxPlayers;
                    this.State.ExpiresAt = created.ExpiresAt;
                    this.State.Connection = ConnectionState.WaitingForPlayers;
                    this.State.StatusMessage = Translations.Get("status.waitingForPlayers");
                    this.State.ErrorMessage = string.Empty;

                    // A live room cannot survive a reconnect: the server would hand us a new
                    // session with no room attached. Stop auto-reconnecting and surface the
                    // drop instead of silently recreating anything.
                    this.signaling.AutoReconnectEnabled = false;

                    this.monitor.Log($"Room {created.RoomId} created (max {this.State.MaxPlayers} players).", LogLevel.Info);
                    this.RaiseChanged();
                    this.RoomOpened?.Invoke(this, EventArgs.Empty);
                });
            });
        }

        /// <summary>Connects to the signaling server and joins an existing room.</summary>
        public void JoinRoom(string roomId, string password)
        {
            if (this.IsBusy || this.State.IsInRoom)
                return;

            string normalisedRoomId = (roomId ?? string.Empty).Trim().ToUpperInvariant();
            string secret = password ?? string.Empty;

            if (normalisedRoomId.Length == 0 || secret.Length == 0)
            {
                this.State.ErrorMessage = Translations.Get("error.missingCredentials");
                this.RaiseChanged();
                return;
            }

            this.BeginOperation(ConnectionState.ConnectingToSignaling, Translations.Get("status.connecting"));
            this.RunBackground("join", async token =>
            {
                await this.EnsureConnectedAsync(token).ConfigureAwait(false);

                this.dispatcher.Invoke(() =>
                {
                    this.State.Connection = ConnectionState.JoiningRoom;
                    this.State.StatusMessage = Translations.Get("status.joiningRoom");
                    this.RaiseChanged();
                });

                // The password lives in this local only for the duration of the request.
                ServerMessage reply = await this.signaling
                    .SendRequestAsync(new JoinRoomRequest(normalisedRoomId, secret), token)
                    .ConfigureAwait(false);

                if (!(reply is RoomJoinedMessage joined))
                    throw new InvalidOperationException($"Unexpected reply '{reply.Type}' to join_room.");

                this.dispatcher.Invoke(() =>
                {
                    this.State.Role = RoomRole.Client;
                    this.State.RoomId = joined.RoomId;
                    this.State.Password = string.Empty;
                    this.State.LocalPeerId = joined.PeerId;
                    this.State.HostPeerId = joined.HostPeerId;
                    this.State.MaxPlayers = joined.MaxPlayers;
                    this.State.Connection = ConnectionState.NegotiatingWebRtc;
                    this.State.StatusMessage = Translations.Get("status.negotiating");
                    this.State.ErrorMessage = string.Empty;
                    this.State.AddOrGetPeer(joined.HostPeerId, isHost: true).State = PeerConnectionState.Negotiating;

                    this.signaling.AutoReconnectEnabled = false;

                    this.monitor.Log($"Joined room {joined.RoomId}; waiting for the host's offer.", LogLevel.Info);
                    this.RaiseChanged();
                });
            });
        }

        /// <summary>Leaves the room (client) or closes it (host), then drops the signaling socket.</summary>
        public void LeaveRoom()
        {
            if (!this.State.IsInRoom)
            {
                this.Teardown(Translations.Get("status.disconnected"));
                return;
            }

            bool isHost = this.State.IsHost;
            this.IsBusy = true;
            this.RaiseChanged();

            this.RunBackground("leave", async token =>
            {
                try
                {
                    if (this.signaling.IsConnected)
                    {
                        ClientRequest request = isHost ? new CloseRoomRequest() : (ClientRequest)new LeaveRoomRequest();
                        await this.signaling.SendAsync(request, token).ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    this.monitor.Log($"Could not tell the server we are leaving: {error.Message}", LogLevel.Debug);
                }

                await this.transport.CloseAllAsync().ConfigureAwait(false);
                await this.signaling.DisconnectAsync("left room").ConfigureAwait(false);

                this.dispatcher.Invoke(() => this.Teardown(Translations.Get("status.disconnected")));
            });
        }

        /// <summary>Text for the "Copy Invite" button.</summary>
        public string BuildInviteText()
        {
            return string.Join(Environment.NewLine,
                Translations.Get("invite.title"),
                $"Room ID: {this.State.RoomId}",
                $"Password: {this.State.Password}");
        }

        // ------------------------------------------------------------------ signaling events

        private void OnSignalingStateChanged(object sender, SignalingStateEventArgs args)
        {
            this.dispatcher.Invoke(() =>
            {
                switch (args.State)
                {
                    case SignalingState.Reconnecting:
                        this.State.Connection = ConnectionState.Reconnecting;
                        this.State.StatusMessage = Translations.Get("status.reconnecting");
                        break;

                    case SignalingState.Failed:
                        this.State.Connection = ConnectionState.Failed;
                        this.State.ErrorMessage = Translations.Get("error.signalingFailed");
                        break;

                    case SignalingState.Disconnected:
                        if (this.State.IsInRoom)
                        {
                            // The room is gone as far as the server is concerned. Never try to
                            // recreate it behind the player's back.
                            this.State.Connection = ConnectionState.Failed;
                            this.State.ErrorMessage = Translations.Get("error.signalingLost");
                            this.RunBackground("teardown", async _ =>
                            {
                                await this.transport.CloseAllAsync().ConfigureAwait(false);
                                this.dispatcher.Invoke(() => this.Teardown(Translations.Get("error.signalingLost")));
                            });
                        }
                        break;
                }

                this.RaiseChanged();
            });
        }

        private void OnSignalingMessage(object sender, ServerMessageEventArgs args)
        {
            ServerMessage message = args.Message;
            this.dispatcher.Invoke(() => this.HandleServerMessage(message));
        }

        private void HandleServerMessage(ServerMessage message)
        {
            switch (message)
            {
                case PeerJoinedMessage peerJoined:
                    this.HandlePeerJoined(peerJoined);
                    break;

                case PeerLeftMessage peerLeft:
                    this.HandlePeerLeft(peerLeft);
                    break;

                case HostDisconnectedMessage:
                    this.State.ErrorMessage = Translations.Get("error.hostDisconnected");
                    this.monitor.Log("The room host disconnected.", LogLevel.Info);
                    break;

                case RoomClosedMessage closed:
                    this.HandleRoomClosed(closed);
                    break;

                case WebRtcOfferReceivedMessage offer:
                    this.HandleRemoteOffer(offer);
                    break;

                case WebRtcAnswerReceivedMessage answer:
                    this.HandleRemoteAnswer(answer);
                    break;

                case IceCandidateReceivedMessage ice:
                    this.HandleRemoteIceCandidate(ice);
                    break;

                case ErrorResponseMessage error:
                    this.State.ErrorMessage = DescribeError(error.Code, error.Message);
                    this.monitor.Log($"Signaling error {error.Code}: {error.Message}", LogLevel.Warn);
                    break;

                case UnknownServerMessage unknown:
                    this.monitor.Log($"Ignoring unsupported server message '{unknown.Type}'.", LogLevel.Trace);
                    break;
            }

            this.RaiseChanged();
        }

        private void HandlePeerJoined(PeerJoinedMessage message)
        {
            if (!this.State.IsHost)
                return;

            PeerSession peer = this.State.AddOrGetPeer(message.PeerId, isHost: false);
            peer.State = PeerConnectionState.CreatingOffer;
            this.State.Connection = ConnectionState.NegotiatingWebRtc;
            this.State.StatusMessage = Translations.Get("status.negotiating");
            this.monitor.Log($"Peer {peer.ShortId} joined; starting WebRTC negotiation.", LogLevel.Info);

            string peerId = message.PeerId;
            this.RunBackground("offer", token => this.transport.StartOfferAsync(peerId, token));
        }

        private void HandlePeerLeft(PeerLeftMessage message)
        {
            if (!this.State.RemovePeer(message.PeerId))
                return;

            this.monitor.Log($"Peer {message.PeerId} left the room ({message.Reason}).", LogLevel.Info);

            string peerId = message.PeerId;
            this.RunBackground("close-peer", _ => this.transport.ClosePeerAsync(peerId));
            this.RecomputeConnectionState();
        }

        private void HandleRoomClosed(RoomClosedMessage message)
        {
            string reason = message.Reason switch
            {
                RoomClosedReasons.Expired => Translations.Get("error.roomExpired"),
                RoomClosedReasons.HostDisconnected => Translations.Get("error.hostDisconnected"),
                RoomClosedReasons.ServerShutdown => Translations.Get("error.serverShutdown"),
                _ => Translations.Get("status.roomClosed")
            };

            this.monitor.Log($"Room {message.RoomId} closed ({message.Reason}).", LogLevel.Info);

            this.RunBackground("room-closed", async _ =>
            {
                await this.transport.CloseAllAsync().ConfigureAwait(false);
                await this.signaling.DisconnectAsync("room closed").ConfigureAwait(false);
                this.dispatcher.Invoke(() =>
                {
                    this.Teardown(reason);
                    if (message.Reason != RoomClosedReasons.HostClosed)
                        this.State.ErrorMessage = reason;
                    this.RaiseChanged();
                });
            });
        }

        private void HandleRemoteOffer(WebRtcOfferReceivedMessage message)
        {
            PeerSession peer = this.State.AddOrGetPeer(message.FromPeerId, message.FromPeerId == this.State.HostPeerId);
            peer.State = PeerConnectionState.Negotiating;

            string peerId = message.FromPeerId;
            string sdp = message.Sdp;
            this.RunBackground("accept-offer", token => this.transport.AcceptOfferAsync(peerId, sdp, token));
        }

        private void HandleRemoteAnswer(WebRtcAnswerReceivedMessage message)
        {
            PeerSession peer = this.State.GetPeer(message.FromPeerId);
            if (peer != null)
                peer.State = PeerConnectionState.Connecting;

            string peerId = message.FromPeerId;
            string sdp = message.Sdp;
            this.RunBackground("accept-answer", token => this.transport.AcceptAnswerAsync(peerId, sdp, token));
        }

        private void HandleRemoteIceCandidate(IceCandidateReceivedMessage message)
        {
            string peerId = message.FromPeerId;
            IceCandidatePayload candidate = message.Candidate;
            this.RunBackground("ice", token => this.transport.AddIceCandidateAsync(peerId, candidate, token));
        }

        // ------------------------------------------------------------------ transport events

        private void OnLocalDescriptionReady(object sender, LocalDescriptionEventArgs args)
        {
            string roomId = this.State.RoomId;
            string localPeerId = this.State.LocalPeerId;
            if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(localPeerId))
                return;

            ClientMessage message = args.Kind == SessionDescriptionKind.Offer
                ? new WebRtcOfferMessage(roomId, localPeerId, args.PeerId, args.Sdp)
                : (ClientMessage)new WebRtcAnswerMessage(roomId, localPeerId, args.PeerId, args.Sdp);

            this.RunBackground("send-sdp", token => this.signaling.SendAsync(message, token));
        }

        private void OnLocalIceCandidateReady(object sender, LocalIceCandidateEventArgs args)
        {
            string roomId = this.State.RoomId;
            string localPeerId = this.State.LocalPeerId;
            if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(localPeerId))
                return;

            IceCandidateMessage message = new IceCandidateMessage(roomId, localPeerId, args.PeerId, args.Candidate);
            this.RunBackground("send-ice", token => this.signaling.SendAsync(message, token));
        }

        private void OnPeerStateChanged(object sender, PeerStateChangedEventArgs args)
        {
            this.dispatcher.Invoke(() =>
            {
                PeerSession peer = this.State.GetPeer(args.PeerId);
                if (peer == null)
                    return;

                switch (args.State)
                {
                    case PeerConnectionState.Connected:
                        peer.MarkConnected();
                        this.monitor.Log($"Peer {peer.ShortId} connected over WebRTC.", LogLevel.Info);
                        break;

                    case PeerConnectionState.Failed:
                        peer.MarkFailed(args.Detail);
                        this.monitor.Log($"Peer {peer.ShortId} failed: {args.Detail}", LogLevel.Warn);
                        break;

                    default:
                        peer.State = args.State;
                        peer.IsDataChannelOpen = false;
                        break;
                }

                this.RecomputeConnectionState();
                this.RaiseChanged();
            });
        }

        private void OnRoundTripMeasured(object sender, RoundTripMeasuredEventArgs args)
        {
            this.dispatcher.Invoke(() =>
            {
                PeerSession peer = this.State.GetPeer(args.PeerId);
                if (peer == null)
                    return;

                peer.RoundTripTime = args.RoundTrip;
                this.RaiseChanged();
            });
        }

        // ------------------------------------------------------------------ helpers

        private async Task EnsureConnectedAsync(CancellationToken token)
        {
            if (this.signaling.IsConnected)
                return;

            if (!Uri.TryCreate(this.config.SignalingServerUrl, UriKind.Absolute, out Uri uri))
                throw new InvalidOperationException($"'{this.config.SignalingServerUrl}' is not a valid signaling URL.");

            await this.signaling.ConnectAsync(uri, token).ConfigureAwait(false);
        }

        private void BeginOperation(ConnectionState state, string status)
        {
            this.IsBusy = true;
            this.State.Connection = state;
            this.State.StatusMessage = status;
            this.State.ErrorMessage = string.Empty;
            this.RaiseChanged();
        }

        /// <summary>Runs network work off the game thread and reports failures through the UI state.</summary>
        private void RunBackground(string description, Func<CancellationToken, Task> work)
        {
            CancellationTokenSource source = this.operationSource;
            if (source == null || source.IsCancellationRequested)
            {
                source = new CancellationTokenSource();
                this.operationSource = source;
            }

            CancellationToken token = source.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await work(token).ConfigureAwait(false);
                    this.dispatcher.Invoke(() =>
                    {
                        this.IsBusy = false;
                        this.RaiseChanged();
                    });
                }
                catch (OperationCanceledException)
                {
                    this.dispatcher.Invoke(() =>
                    {
                        this.IsBusy = false;
                        this.RaiseChanged();
                    });
                }
                catch (Exception error)
                {
                    string friendly = error switch
                    {
                        SignalingErrorException signalingError => DescribeError(signalingError.Code, signalingError.Message),
                        TimeoutException => Translations.Get("error.timeout"),
                        _ => Translations.Get("error.generic")
                    };

                    this.monitor.Log($"Operation '{description}' failed: {error.Message}", LogLevel.Warn);
                    if (this.config.VerboseLogging)
                        this.monitor.Log(error.ToString(), LogLevel.Trace);

                    this.dispatcher.Invoke(() =>
                    {
                        this.IsBusy = false;
                        this.State.ErrorMessage = friendly;
                        if (!this.State.IsInRoom)
                            this.State.Connection = ConnectionState.Failed;
                        this.RaiseChanged();
                    });
                }
            });
        }

        private void RecomputeConnectionState()
        {
            if (!this.State.IsInRoom)
                return;

            int connected = 0;
            int negotiating = 0;
            foreach (PeerSession peer in this.State.Peers)
            {
                if (peer.IsUsable)
                    connected++;
                else if (peer.State != PeerConnectionState.Failed && peer.State != PeerConnectionState.Closed)
                    negotiating++;
            }

            if (connected > 0)
            {
                this.State.Connection = ConnectionState.Connected;
                this.State.StatusMessage = Translations.Get("status.connected");
            }
            else if (negotiating > 0)
            {
                this.State.Connection = ConnectionState.NegotiatingWebRtc;
                this.State.StatusMessage = Translations.Get("status.negotiating");
            }
            else if (this.State.IsHost)
            {
                this.State.Connection = ConnectionState.WaitingForPlayers;
                this.State.StatusMessage = Translations.Get("status.waitingForPlayers");
            }
        }

        /// <summary>Resets the session state and wipes the password from memory.</summary>
        private void Teardown(string status)
        {
            this.State.Reset();
            this.State.StatusMessage = status ?? string.Empty;
            this.IsBusy = false;
            this.signaling.AutoReconnectEnabled = this.config.AutoReconnectSignaling;
            this.RaiseChanged();
        }

        private void RaiseChanged()
        {
            try
            {
                this.Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception error)
            {
                this.monitor.Log($"A session state handler threw: {error}", LogLevel.Error);
            }
        }

        /// <summary>Maps a server error code onto a friendly, translated line.</summary>
        private static string DescribeError(string code, string fallback)
        {
            switch (code)
            {
                case ErrorCodes.InvalidRoomCredentials:
                    return Translations.Get("error.invalidCredentials");
                case ErrorCodes.RoomFull:
                    return Translations.Get("error.roomFull");
                case ErrorCodes.TooManyFailedAttempts:
                    return Translations.Get("error.tooManyAttempts");
                case ErrorCodes.RateLimited:
                    return Translations.Get("error.rateLimited");
                case ErrorCodes.TooManyConnections:
                    return Translations.Get("error.tooManyConnections");
                case ErrorCodes.ServerShuttingDown:
                    return Translations.Get("error.serverShutdown");
                case ErrorCodes.AlreadyInRoom:
                case ErrorCodes.NotInRoom:
                case ErrorCodes.NotRoomHost:
                case ErrorCodes.PeerNotFound:
                case ErrorCodes.PeerIdMismatch:
                case ErrorCodes.RoomMismatch:
                case ErrorCodes.InvalidMessage:
                case ErrorCodes.MessageTooLarge:
                case ErrorCodes.UnsupportedMessageType:
                    return Translations.Get("error.protocol");
                default:
                    return string.IsNullOrWhiteSpace(fallback) ? Translations.Get("error.generic") : fallback;
            }
        }

        public void Dispose()
        {
            if (this.disposed)
                return;

            this.disposed = true;

            this.signaling.MessageReceived -= this.OnSignalingMessage;
            this.signaling.StateChanged -= this.OnSignalingStateChanged;
            this.transport.LocalDescriptionReady -= this.OnLocalDescriptionReady;
            this.transport.LocalIceCandidateReady -= this.OnLocalIceCandidateReady;
            this.transport.PeerStateChanged -= this.OnPeerStateChanged;
            this.router.RoundTripMeasured -= this.OnRoundTripMeasured;
            this.router.Detach();

            try
            {
                this.operationSource?.Cancel();
                this.operationSource?.Dispose();
            }
            catch (Exception)
            {
                // shutting down
            }

            this.State.Reset();
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StardewConnect.Configuration;
using StardewConnect.Protocol;
using StardewModdingAPI;

namespace StardewConnect.Networking
{
    /// <summary>
    /// <see cref="ClientWebSocket"/> based implementation of <see cref="ISignalingClient"/>.
    ///
    /// Responsibilities:
    ///  - one receive loop that reassembles fragmented frames and parses them,
    ///  - request/response correlation through <c>requestId</c>,
    ///  - an application level ping so a silently dead link is noticed,
    ///  - reconnect with exponential backoff (only while no room is active).
    ///
    /// Nothing here touches game state, and passwords are never written to the log.
    /// </summary>
    internal sealed class SignalingClient : ISignalingClient
    {
        /// <summary>Matches MAX_MESSAGE_SIZE_BYTES on the server.</summary>
        private const int MaxIncomingMessageBytes = 64 * 1024;

        private const int ReceiveBufferSize = 16 * 1024;
        private const int MaxReconnectDelaySeconds = 30;
        private const int ShutdownGraceMs = 1500;

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IMonitor monitor;
        private readonly ModConfig config;
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ServerMessage>> pendingRequests =
            new ConcurrentDictionary<string, TaskCompletionSource<ServerMessage>>();
        private readonly object stateLock = new object();
        private readonly Random jitter = new Random();

        /// <summary>Cancelled once, on dispose. Every connection is linked to it.</summary>
        private readonly CancellationTokenSource clientLifetime = new CancellationTokenSource();

        private ClientWebSocket socket;
        private CancellationTokenSource connectionSource;
        private Task receiveTask;
        private Task heartbeatTask;
        private Uri endpoint;
        private bool disposed;
        private bool intentionalClose;
        private int reconnectAttempt;

        public SignalingClient(IMonitor monitor, ModConfig config)
        {
            this.monitor = monitor;
            this.config = config;
            this.AutoReconnectEnabled = config.AutoReconnectSignaling;
        }

        public SignalingState State { get; private set; } = SignalingState.Disconnected;

        public bool IsConnected => this.State == SignalingState.Connected && this.socket?.State == WebSocketState.Open;

        public bool AutoReconnectEnabled { get; set; }

        public event EventHandler<SignalingStateEventArgs> StateChanged;

        public event EventHandler<ServerMessageEventArgs> MessageReceived;

        // ------------------------------------------------------------------ connect / disconnect

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();

            if (uri == null)
                throw new ArgumentNullException(nameof(uri));
            if (!string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) && !string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unsupported signaling scheme '{uri.Scheme}'. Use ws:// or wss://.", nameof(uri));

            if (this.IsConnected)
                return;

            this.endpoint = uri;
            this.intentionalClose = false;
            this.AutoReconnectEnabled = this.config.AutoReconnectSignaling;
            await this.OpenSocketAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task OpenSocketAsync(CancellationToken cancellationToken)
        {
            this.SetState(SignalingState.Connecting, this.endpoint.ToString());

            ClientWebSocket newSocket = new ClientWebSocket();
            newSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(this.config.HeartbeatIntervalSeconds);

            using (CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(this.clientLifetime.Token, cancellationToken))
            {
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(this.config.ConnectTimeoutSeconds));

                try
                {
                    await newSocket.ConnectAsync(this.endpoint, connectTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !this.clientLifetime.IsCancellationRequested)
                {
                    newSocket.Dispose();
                    this.SetState(SignalingState.Failed, "connection timed out");
                    throw new TimeoutException($"Timed out connecting to {this.endpoint} after {this.config.ConnectTimeoutSeconds}s.");
                }
                catch (Exception)
                {
                    newSocket.Dispose();
                    this.SetState(SignalingState.Failed, "connection failed");
                    throw;
                }
            }

            CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(this.clientLifetime.Token);
            CancellationTokenSource previous;

            lock (this.stateLock)
            {
                previous = this.connectionSource;
                this.socket = newSocket;
                this.connectionSource = connection;
            }

            previous?.Dispose();

            this.reconnectAttempt = 0;
            this.SetState(SignalingState.Connected, this.endpoint.Host);

            CancellationToken connectionToken = connection.Token;
            this.receiveTask = Task.Run(() => this.ReceiveLoopAsync(newSocket, connectionToken));
            this.heartbeatTask = Task.Run(() => this.HeartbeatLoopAsync(connectionToken));
        }

        public async Task DisconnectAsync(string reason)
        {
            if (this.disposed)
                return;

            this.intentionalClose = true;
            this.AutoReconnectEnabled = false;

            ClientWebSocket current;
            CancellationTokenSource connection;
            Task pendingReceive;
            Task pendingHeartbeat;

            lock (this.stateLock)
            {
                current = this.socket;
                connection = this.connectionSource;
                pendingReceive = this.receiveTask;
                pendingHeartbeat = this.heartbeatTask;
                this.socket = null;
                this.connectionSource = null;
                this.receiveTask = null;
                this.heartbeatTask = null;
            }

            this.FailPendingRequests(new OperationCanceledException($"Signaling connection closed: {reason}"));

            if (current != null)
            {
                try
                {
                    if (current.State == WebSocketState.Open)
                    {
                        using CancellationTokenSource closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await current.CloseAsync(WebSocketCloseStatus.NormalClosure, Truncate(reason, 120), closeTimeout.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    this.LogVerbose($"Ignoring error while closing the signaling socket: {error.Message}");
                }
            }

            connection?.Cancel();

            await WaitForCompletionAsync(pendingReceive).ConfigureAwait(false);
            await WaitForCompletionAsync(pendingHeartbeat).ConfigureAwait(false);

            current?.Dispose();
            connection?.Dispose();

            this.SetState(SignalingState.Disconnected, reason);
        }

        private static async Task WaitForCompletionAsync(Task task)
        {
            if (task == null)
                return;

            try
            {
                await Task.WhenAny(task, Task.Delay(ShutdownGraceMs)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // the loops log their own failures
            }
        }

        // ------------------------------------------------------------------ sending

        public async Task SendAsync(ClientMessage message, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();

            if (message == null)
                throw new ArgumentNullException(nameof(message));

            ClientWebSocket current = this.socket;
            if (current == null || current.State != WebSocketState.Open)
                throw new InvalidOperationException("The signaling connection is not open.");

            // Serialise against the runtime type: System.Text.Json on .NET 6 would otherwise
            // only emit the members declared on ClientMessage.
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), SerializerOptions);
            if (payload.Length > MaxIncomingMessageBytes)
                throw new InvalidOperationException($"Refusing to send a {payload.Length} byte signaling message; the server limit is {MaxIncomingMessageBytes} bytes.");

            await this.sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await current.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                this.sendLock.Release();
            }

            this.LogVerbose($"-> {message.Type}");
        }

        public async Task<ServerMessage> SendRequestAsync(ClientRequest request, CancellationToken cancellationToken)
        {
            this.ThrowIfDisposed();

            if (request == null)
                throw new ArgumentNullException(nameof(request));

            TaskCompletionSource<ServerMessage> completion = new TaskCompletionSource<ServerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!this.pendingRequests.TryAdd(request.RequestId, completion))
                throw new InvalidOperationException($"Duplicate requestId '{request.RequestId}'.");

            try
            {
                await this.SendAsync(request, cancellationToken).ConfigureAwait(false);

                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(this.clientLifetime.Token, cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(this.config.RequestTimeoutSeconds));

                using CancellationTokenRegistration registration = timeout.Token.Register(() => completion.TrySetCanceled());

                ServerMessage reply;
                try
                {
                    reply = await completion.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"The server did not answer '{request.Type}' within {this.config.RequestTimeoutSeconds}s.");
                }

                if (reply is ErrorResponseMessage error)
                    throw new SignalingErrorException(error.Code, error.Message);

                return reply;
            }
            finally
            {
                this.pendingRequests.TryRemove(request.RequestId, out _);
            }
        }

        // ------------------------------------------------------------------ receive loop

        private async Task ReceiveLoopAsync(ClientWebSocket target, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[ReceiveBufferSize];
            string closeReason = "connection closed";

            try
            {
                while (!cancellationToken.IsCancellationRequested && target.State == WebSocketState.Open)
                {
                    using MemoryStream frame = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await target.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            closeReason = $"server closed the connection ({result.CloseStatus}: {result.CloseStatusDescription})";
                            this.monitor.Log($"Signaling: {closeReason}", LogLevel.Debug);
                            return;
                        }

                        if (frame.Length + result.Count > MaxIncomingMessageBytes)
                        {
                            closeReason = "server sent an oversized frame";
                            this.monitor.Log($"Signaling: {closeReason}; dropping the connection.", LogLevel.Warn);
                            return;
                        }

                        frame.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text)
                        continue;

                    this.HandleFrame(Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length));
                }
            }
            catch (OperationCanceledException)
            {
                closeReason = "cancelled";
            }
            catch (WebSocketException error)
            {
                closeReason = error.Message;
                this.monitor.Log($"Signaling connection lost: {error.Message}", LogLevel.Warn);
            }
            catch (Exception error)
            {
                closeReason = error.Message;
                this.monitor.Log($"Signaling receive loop failed: {error}", LogLevel.Error);
            }
            finally
            {
                await this.HandleConnectionClosedAsync(closeReason).ConfigureAwait(false);
            }
        }

        private void HandleFrame(string json)
        {
            ServerMessage message = ServerMessageParser.Parse(json);
            if (message == null)
            {
                this.monitor.Log("Discarded an unparseable signaling frame.", LogLevel.Warn);
                return;
            }

            this.LogVerbose($"<- {message.Type}");

            if (!string.IsNullOrEmpty(message.RequestId)
                && this.pendingRequests.TryRemove(message.RequestId, out TaskCompletionSource<ServerMessage> completion))
            {
                completion.TrySetResult(message);
                return;
            }

            try
            {
                this.MessageReceived?.Invoke(this, new ServerMessageEventArgs(message));
            }
            catch (Exception error)
            {
                this.monitor.Log($"A signaling message handler threw: {error}", LogLevel.Error);
            }
        }

        private async Task HandleConnectionClosedAsync(string reason)
        {
            ClientWebSocket closed;
            lock (this.stateLock)
            {
                closed = this.socket;
                this.socket = null;
            }

            closed?.Dispose();

            this.FailPendingRequests(new InvalidOperationException($"Signaling connection closed: {reason}"));

            if (this.intentionalClose || this.disposed)
            {
                this.SetState(SignalingState.Disconnected, reason);
                return;
            }

            if (!this.AutoReconnectEnabled || this.config.MaxReconnectAttempts <= 0)
            {
                this.SetState(SignalingState.Disconnected, reason);
                return;
            }

            await this.ReconnectAsync(reason).ConfigureAwait(false);
        }

        /// <summary>
        /// Reconnects with exponential backoff.
        ///
        /// It only restores the socket. A reconnected socket is a brand new session on the
        /// server, so the room is *not* recreated or rejoined here - the session layer decides
        /// what to do once the state has been confirmed.
        /// </summary>
        private async Task ReconnectAsync(string reason)
        {
            CancellationToken token = this.clientLifetime.Token;

            while (!this.disposed && !this.intentionalClose && this.AutoReconnectEnabled && !token.IsCancellationRequested)
            {
                this.reconnectAttempt++;
                if (this.reconnectAttempt > this.config.MaxReconnectAttempts)
                {
                    this.SetState(SignalingState.Failed, $"gave up after {this.config.MaxReconnectAttempts} reconnect attempts");
                    return;
                }

                int delaySeconds = Math.Min(MaxReconnectDelaySeconds, (int)Math.Pow(2, Math.Min(this.reconnectAttempt, 5)));
                int delayMs = (delaySeconds * 1000) + this.jitter.Next(0, 500);

                this.SetState(SignalingState.Reconnecting, $"attempt {this.reconnectAttempt} in {delaySeconds}s ({reason})");

                try
                {
                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                    await this.OpenSocketAsync(token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception error)
                {
                    reason = error.Message;
                    this.LogVerbose($"Reconnect attempt {this.reconnectAttempt} failed: {error.Message}");
                }
            }
        }

        // ------------------------------------------------------------------ heartbeat

        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            TimeSpan interval = TimeSpan.FromSeconds(this.config.HeartbeatIntervalSeconds);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

                    if (!this.IsConnected)
                        continue;

                    try
                    {
                        await this.SendRequestAsync(new PingRequest(), cancellationToken).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        this.monitor.Log("Signaling heartbeat timed out; dropping the connection.", LogLevel.Warn);
                        this.AbortSocket();
                        return;
                    }
                    catch (Exception error) when (!(error is OperationCanceledException))
                    {
                        this.LogVerbose($"Heartbeat failed: {error.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }

        private void AbortSocket()
        {
            ClientWebSocket current = this.socket;
            try
            {
                current?.Abort();
            }
            catch (Exception error)
            {
                this.LogVerbose($"Ignoring error while aborting the socket: {error.Message}");
            }
        }

        // ------------------------------------------------------------------ helpers

        private void FailPendingRequests(Exception error)
        {
            foreach (var entry in this.pendingRequests)
            {
                if (this.pendingRequests.TryRemove(entry.Key, out TaskCompletionSource<ServerMessage> completion))
                    completion.TrySetException(error);
            }
        }

        private void SetState(SignalingState state, string detail)
        {
            if (this.State == state && state != SignalingState.Reconnecting)
                return;

            this.State = state;
            this.monitor.Log($"Signaling state: {state}{(string.IsNullOrEmpty(detail) ? string.Empty : $" ({detail})")}", LogLevel.Debug);

            try
            {
                this.StateChanged?.Invoke(this, new SignalingStateEventArgs(state, detail));
            }
            catch (Exception error)
            {
                this.monitor.Log($"A signaling state handler threw: {error}", LogLevel.Error);
            }
        }

        private void LogVerbose(string message)
        {
            if (this.config.VerboseLogging)
                this.monitor.Log(message, LogLevel.Trace);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
                throw new ObjectDisposedException(nameof(SignalingClient));
        }

        public void Dispose()
        {
            if (this.disposed)
                return;

            this.disposed = true;
            this.intentionalClose = true;
            this.AutoReconnectEnabled = false;

            CancellationTokenSource connection;
            ClientWebSocket current;
            lock (this.stateLock)
            {
                connection = this.connectionSource;
                current = this.socket;
                this.connectionSource = null;
                this.socket = null;
                this.receiveTask = null;
                this.heartbeatTask = null;
            }

            try
            {
                this.clientLifetime.Cancel();
            }
            catch (Exception)
            {
                // nothing useful to do while disposing
            }

            connection?.Dispose();
            this.clientLifetime.Dispose();
            current?.Dispose();

            this.FailPendingRequests(new ObjectDisposedException(nameof(SignalingClient)));
            this.sendLock.Dispose();
            this.State = SignalingState.Disconnected;
        }
    }
}

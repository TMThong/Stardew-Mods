using System;
using System.Threading;
using System.Threading.Tasks;
using StardewConnect.Protocol;

namespace StardewConnect.Networking
{
    /// <summary>State of the WebSocket link to the signaling server.</summary>
    public enum SignalingState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Failed
    }

    internal sealed class SignalingStateEventArgs : EventArgs
    {
        public SignalingStateEventArgs(SignalingState state, string detail = null)
        {
            this.State = state;
            this.Detail = detail ?? string.Empty;
        }

        public SignalingState State { get; }
        public string Detail { get; }
    }

    internal sealed class ServerMessageEventArgs : EventArgs
    {
        public ServerMessageEventArgs(ServerMessage message)
        {
            this.Message = message;
        }

        public ServerMessage Message { get; }
    }

    /// <summary>Thrown when the server answers a request with an <c>error</c> message.</summary>
    internal sealed class SignalingErrorException : Exception
    {
        public SignalingErrorException(string code, string message)
            : base(message)
        {
            this.Code = code ?? ErrorCodes.InternalError;
        }

        /// <summary>One of the constants in <see cref="ErrorCodes"/>.</summary>
        public string Code { get; }
    }

    /// <summary>
    /// The signaling transport: a single WebSocket connection carrying room control and
    /// relayed WebRTC descriptions. It never carries gameplay data.
    ///
    /// Events are raised from the receive loop's thread; subscribers must marshal onto the
    /// game thread themselves.
    /// </summary>
    internal interface ISignalingClient : IDisposable
    {
        SignalingState State { get; }

        bool IsConnected { get; }

        /// <summary>
        /// Reconnect automatically after an unexpected drop. The session layer turns this off
        /// while a room is active, because a reconnected socket is a brand new session on the
        /// server and the room cannot be silently recreated.
        /// </summary>
        bool AutoReconnectEnabled { get; set; }

        event EventHandler<SignalingStateEventArgs> StateChanged;

        /// <summary>Raised for server messages that are not the answer to a pending request.</summary>
        event EventHandler<ServerMessageEventArgs> MessageReceived;

        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

        Task DisconnectAsync(string reason);

        Task SendAsync(ClientMessage message, CancellationToken cancellationToken);

        /// <summary>Sends a request and waits for the reply with the matching <c>requestId</c>.</summary>
        /// <exception cref="SignalingErrorException">The server replied with an <c>error</c>.</exception>
        /// <exception cref="TimeoutException">No reply arrived in time.</exception>
        Task<ServerMessage> SendRequestAsync(ClientRequest request, CancellationToken cancellationToken);
    }
}

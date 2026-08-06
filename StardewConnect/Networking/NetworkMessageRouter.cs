using System;
using System.Collections.Generic;
using StardewModdingAPI;

namespace StardewConnect.Networking
{
    /// <summary>Payload handler for one logical channel.</summary>
    /// <param name="peerId">Peer the payload came from.</param>
    /// <param name="payload">Channel payload, without the envelope byte.</param>
    internal delegate void PeerPayloadHandler(string peerId, ArraySegment<byte> payload);

    internal sealed class RoundTripMeasuredEventArgs : EventArgs
    {
        public RoundTripMeasuredEventArgs(string peerId, TimeSpan roundTrip)
        {
            this.PeerId = peerId;
            this.RoundTrip = roundTrip;
        }

        public string PeerId { get; }
        public TimeSpan RoundTrip { get; }
    }

    /// <summary>
    /// Multiplexes the single "stardew-connect" data channel into logical channels.
    ///
    /// Envelope: <c>[channel:1][payload...]</c>. Channel 0 is reserved for control traffic
    /// (keepalive / round trip measurement) handled here; everything else is dispatched to
    /// whoever registered for it, which is where future gameplay sync will plug in.
    ///
    /// This layer never touches the signaling socket: once WebRTC is up, game data goes
    /// peer to peer only.
    /// </summary>
    internal sealed class NetworkMessageRouter
    {
        /// <summary>Reserved control channel.</summary>
        public const byte ControlChannel = 0;

        /// <summary>Default channel for gameplay payloads.</summary>
        public const byte GameChannel = 1;

        private const byte ControlPing = 1;
        private const byte ControlPong = 2;

        private const int MaxPayloadBytes = 240 * 1024;

        private readonly Dictionary<byte, PeerPayloadHandler> handlers = new Dictionary<byte, PeerPayloadHandler>();
        private readonly IWebRtcTransport transport;
        private readonly IMonitor monitor;

        public NetworkMessageRouter(IWebRtcTransport transport, IMonitor monitor)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.monitor = monitor;
            this.transport.DataReceived += this.OnDataReceived;
        }

        /// <summary>Raised when a control pong comes back, giving the data channel round trip time.</summary>
        public event EventHandler<RoundTripMeasuredEventArgs> RoundTripMeasured;

        /// <summary>Registers (or replaces) the handler for a channel. Channel 0 is reserved.</summary>
        public void RegisterHandler(byte channel, PeerPayloadHandler handler)
        {
            if (channel == ControlChannel)
                throw new ArgumentException("Channel 0 is reserved for control traffic.", nameof(channel));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            this.handlers[channel] = handler;
        }

        public void UnregisterHandler(byte channel)
        {
            this.handlers.Remove(channel);
        }

        /// <summary>Sends a payload to one peer. Returns false when the data channel is not open.</summary>
        public bool Send(string peerId, byte channel, byte[] payload)
        {
            if (payload == null)
                payload = Array.Empty<byte>();
            if (payload.Length > MaxPayloadBytes)
                throw new ArgumentException($"Payload of {payload.Length} bytes exceeds the {MaxPayloadBytes} byte limit.", nameof(payload));

            byte[] framed = new byte[payload.Length + 1];
            framed[0] = channel;
            Buffer.BlockCopy(payload, 0, framed, 1, payload.Length);

            return this.transport.Send(peerId, framed);
        }

        /// <summary>Sends a payload to every connected peer. Returns the number of peers reached.</summary>
        public int Broadcast(byte channel, byte[] payload)
        {
            int sent = 0;
            foreach (string peerId in this.transport.ConnectedPeers)
            {
                if (this.Send(peerId, channel, payload))
                    sent++;
            }

            return sent;
        }

        /// <summary>Sends a control ping; the reply drives <see cref="RoundTripMeasured"/>.</summary>
        public bool SendPing(string peerId)
        {
            byte[] payload = new byte[9];
            payload[0] = ControlPing;
            BitConverter.TryWriteBytes(new Span<byte>(payload, 1, 8), DateTime.UtcNow.Ticks);
            return this.transport.Send(peerId, Envelope(ControlChannel, payload));
        }

        private void OnDataReceived(object sender, PeerDataEventArgs args)
        {
            byte[] data = args?.Payload;
            if (data == null || data.Length < 1)
                return;

            byte channel = data[0];
            ArraySegment<byte> payload = new ArraySegment<byte>(data, 1, data.Length - 1);

            if (channel == ControlChannel)
            {
                this.HandleControl(args.PeerId, payload);
                return;
            }

            if (!this.handlers.TryGetValue(channel, out PeerPayloadHandler handler))
            {
                this.monitor.Log($"Dropped a payload for unregistered channel {channel} from peer {Shorten(args.PeerId)}.", LogLevel.Trace);
                return;
            }

            try
            {
                handler(args.PeerId, payload);
            }
            catch (Exception error)
            {
                this.monitor.Log($"Channel {channel} handler failed: {error}", LogLevel.Error);
            }
        }

        private void HandleControl(string peerId, ArraySegment<byte> payload)
        {
            if (payload.Count < 1 || payload.Array == null)
                return;

            byte opcode = payload.Array[payload.Offset];

            switch (opcode)
            {
                case ControlPing:
                {
                    if (payload.Count < 9)
                        return;

                    // Echo the caller's timestamp back untouched.
                    byte[] pong = new byte[9];
                    pong[0] = ControlPong;
                    Buffer.BlockCopy(payload.Array, payload.Offset + 1, pong, 1, 8);
                    this.transport.Send(peerId, Envelope(ControlChannel, pong));
                    break;
                }

                case ControlPong:
                {
                    if (payload.Count < 9)
                        return;

                    long ticks = BitConverter.ToInt64(payload.Array, payload.Offset + 1);
                    TimeSpan roundTrip = TimeSpan.FromTicks(Math.Max(0, DateTime.UtcNow.Ticks - ticks));
                    this.RoundTripMeasured?.Invoke(this, new RoundTripMeasuredEventArgs(peerId, roundTrip));
                    break;
                }

                default:
                    this.monitor.Log($"Ignored unknown control opcode {opcode} from peer {Shorten(peerId)}.", LogLevel.Trace);
                    break;
            }
        }

        private static byte[] Envelope(byte channel, byte[] payload)
        {
            byte[] framed = new byte[payload.Length + 1];
            framed[0] = channel;
            Buffer.BlockCopy(payload, 0, framed, 1, payload.Length);
            return framed;
        }

        private static string Shorten(string peerId)
        {
            return peerId != null && peerId.Length >= 8 ? peerId.Substring(0, 8) : peerId ?? string.Empty;
        }

        public void Detach()
        {
            this.transport.DataReceived -= this.OnDataReceived;
            this.handlers.Clear();
        }
    }
}

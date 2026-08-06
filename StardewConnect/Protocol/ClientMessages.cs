using System;
using System.Text.Json.Serialization;

namespace StardewConnect.Protocol
{
    /// <summary>Base type for everything the mod sends to the signaling server.</summary>
    internal abstract class ClientMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; }

        protected ClientMessage(string type)
        {
            this.Type = type;
        }
    }

    /// <summary>A message whose reply is correlated through <c>requestId</c>.</summary>
    internal abstract class ClientRequest : ClientMessage
    {
        [JsonPropertyName("requestId")]
        public string RequestId { get; }

        protected ClientRequest(string type, string requestId = null)
            : base(type)
        {
            this.RequestId = requestId ?? Guid.NewGuid().ToString();
        }
    }

    internal sealed class CreateRoomRequest : ClientRequest
    {
        [JsonPropertyName("maxPlayers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxPlayers { get; }

        public CreateRoomRequest(int? maxPlayers = null)
            : base(MessageTypes.CreateRoom)
        {
            this.MaxPlayers = maxPlayers;
        }
    }

    internal sealed class JoinRoomRequest : ClientRequest
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; }

        [JsonPropertyName("password")]
        public string Password { get; }

        public JoinRoomRequest(string roomId, string password)
            : base(MessageTypes.JoinRoom)
        {
            this.RoomId = roomId;
            this.Password = password;
        }
    }

    internal sealed class LeaveRoomRequest : ClientRequest
    {
        public LeaveRoomRequest()
            : base(MessageTypes.LeaveRoom) { }
    }

    internal sealed class CloseRoomRequest : ClientRequest
    {
        public CloseRoomRequest()
            : base(MessageTypes.CloseRoom) { }
    }

    internal sealed class PingRequest : ClientRequest
    {
        public PingRequest()
            : base(MessageTypes.Ping) { }
    }

    /// <summary>Base type for the three relayed WebRTC messages.</summary>
    internal abstract class SignalMessage : ClientMessage
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; }

        [JsonPropertyName("fromPeerId")]
        public string FromPeerId { get; }

        [JsonPropertyName("targetPeerId")]
        public string TargetPeerId { get; }

        protected SignalMessage(string type, string roomId, string fromPeerId, string targetPeerId)
            : base(type)
        {
            this.RoomId = roomId;
            this.FromPeerId = fromPeerId;
            this.TargetPeerId = targetPeerId;
        }
    }

    internal sealed class WebRtcOfferMessage : SignalMessage
    {
        [JsonPropertyName("sdp")]
        public string Sdp { get; }

        public WebRtcOfferMessage(string roomId, string fromPeerId, string targetPeerId, string sdp)
            : base(MessageTypes.WebRtcOffer, roomId, fromPeerId, targetPeerId)
        {
            this.Sdp = sdp;
        }
    }

    internal sealed class WebRtcAnswerMessage : SignalMessage
    {
        [JsonPropertyName("sdp")]
        public string Sdp { get; }

        public WebRtcAnswerMessage(string roomId, string fromPeerId, string targetPeerId, string sdp)
            : base(MessageTypes.WebRtcAnswer, roomId, fromPeerId, targetPeerId)
        {
            this.Sdp = sdp;
        }
    }

    internal sealed class IceCandidateMessage : SignalMessage
    {
        [JsonPropertyName("candidate")]
        public IceCandidatePayload Candidate { get; }

        public IceCandidateMessage(string roomId, string fromPeerId, string targetPeerId, IceCandidatePayload candidate)
            : base(MessageTypes.IceCandidate, roomId, fromPeerId, targetPeerId)
        {
            this.Candidate = candidate;
        }
    }

    /// <summary>A single ICE candidate, matching the browser <c>RTCIceCandidateInit</c> shape.</summary>
    internal sealed class IceCandidatePayload
    {
        [JsonPropertyName("candidate")]
        public string Candidate { get; set; } = string.Empty;

        [JsonPropertyName("sdpMid")]
        public string SdpMid { get; set; }

        [JsonPropertyName("sdpMLineIndex")]
        public int? SdpMLineIndex { get; set; }

        public IceCandidatePayload() { }

        public IceCandidatePayload(string candidate, string sdpMid, int? sdpMLineIndex)
        {
            this.Candidate = candidate ?? string.Empty;
            this.SdpMid = sdpMid;
            this.SdpMLineIndex = sdpMLineIndex;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StardewConnect.Protocol
{
    /// <summary>Base type for everything the signaling server sends back.</summary>
    internal abstract class ServerMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>Set when the message answers a request the mod sent.</summary>
        [JsonPropertyName("requestId")]
        public string RequestId { get; set; }
    }

    /// <summary>A message type this build does not know about. Kept so new server versions do not break old mods.</summary>
    internal sealed class UnknownServerMessage : ServerMessage
    {
        public string RawJson { get; set; } = string.Empty;
    }

    internal sealed class RoomCreatedMessage : ServerMessage
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("peerId")]
        public string PeerId { get; set; } = string.Empty;

        [JsonPropertyName("maxPlayers")]
        public int MaxPlayers { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    internal sealed class RoomJoinedMessage : ServerMessage
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; } = string.Empty;

        [JsonPropertyName("peerId")]
        public string PeerId { get; set; } = string.Empty;

        [JsonPropertyName("hostPeerId")]
        public string HostPeerId { get; set; } = string.Empty;

        [JsonPropertyName("maxPlayers")]
        public int MaxPlayers { get; set; }

        [JsonPropertyName("peers")]
        public List<string> Peers { get; set; } = new List<string>();
    }

    internal sealed class PeerJoinedMessage : ServerMessage
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; } = string.Empty;

        [JsonPropertyName("peerId")]
        public string PeerId { get; set; } = string.Empty;

        [JsonPropertyName("peerCount")]
        public int PeerCount { get; set; }
    }

    internal sealed class PeerLeftMessage : ServerMessage
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; } = string.Empty;

        [JsonPropertyName("peerId")]
        public string PeerId { get; set; } = string.Empty;

        [JsonPropertyName("peerCount")]
        public int PeerCount { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    internal sealed class HostDisconnectedMessage : ServerMessage
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; } = string.Empty;
    }

    internal sealed class RoomClosedMessage : ServerMessage
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    internal sealed class WebRtcOfferReceivedMessage : ServerMessage
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; } = string.Empty;

        [JsonPropertyName("fromPeerId")]
        public string FromPeerId { get; set; } = string.Empty;

        [JsonPropertyName("targetPeerId")]
        public string TargetPeerId { get; set; } = string.Empty;

        [JsonPropertyName("sdp")]
        public string Sdp { get; set; } = string.Empty;
    }

    internal sealed class WebRtcAnswerReceivedMessage : ServerMessage
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; } = string.Empty;

        [JsonPropertyName("fromPeerId")]
        public string FromPeerId { get; set; } = string.Empty;

        [JsonPropertyName("targetPeerId")]
        public string TargetPeerId { get; set; } = string.Empty;

        [JsonPropertyName("sdp")]
        public string Sdp { get; set; } = string.Empty;
    }

    internal sealed class IceCandidateReceivedMessage : ServerMessage
    {
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; } = string.Empty;

        [JsonPropertyName("fromPeerId")]
        public string FromPeerId { get; set; } = string.Empty;

        [JsonPropertyName("targetPeerId")]
        public string TargetPeerId { get; set; } = string.Empty;

        [JsonPropertyName("candidate")]
        public IceCandidatePayload Candidate { get; set; } = new IceCandidatePayload();
    }

    internal sealed class PongMessage : ServerMessage
    {
        [JsonPropertyName("serverTime")]
        public DateTimeOffset? ServerTime { get; set; }
    }

    internal sealed class ErrorResponseMessage : ServerMessage
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>Turns raw JSON frames into typed <see cref="ServerMessage"/> instances.</summary>
    internal static class ServerMessageParser
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Disallow
        };

        /// <summary>Parses a frame. Returns <c>null</c> only when the payload is not a JSON object with a string <c>type</c>.</summary>
        public static ServerMessage Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            string type;
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return null;
                if (!document.RootElement.TryGetProperty("type", out JsonElement typeElement) || typeElement.ValueKind != JsonValueKind.String)
                    return null;
                type = typeElement.GetString();
            }
            catch (JsonException)
            {
                return null;
            }

            try
            {
                switch (type)
                {
                    case MessageTypes.RoomCreated:
                        return JsonSerializer.Deserialize<RoomCreatedMessage>(json, Options);
                    case MessageTypes.RoomJoined:
                        return JsonSerializer.Deserialize<RoomJoinedMessage>(json, Options);
                    case MessageTypes.PeerJoined:
                        return JsonSerializer.Deserialize<PeerJoinedMessage>(json, Options);
                    case MessageTypes.PeerLeft:
                        return JsonSerializer.Deserialize<PeerLeftMessage>(json, Options);
                    case MessageTypes.HostDisconnected:
                        return JsonSerializer.Deserialize<HostDisconnectedMessage>(json, Options);
                    case MessageTypes.RoomClosed:
                        return JsonSerializer.Deserialize<RoomClosedMessage>(json, Options);
                    case MessageTypes.WebRtcOffer:
                        return JsonSerializer.Deserialize<WebRtcOfferReceivedMessage>(json, Options);
                    case MessageTypes.WebRtcAnswer:
                        return JsonSerializer.Deserialize<WebRtcAnswerReceivedMessage>(json, Options);
                    case MessageTypes.IceCandidate:
                        return JsonSerializer.Deserialize<IceCandidateReceivedMessage>(json, Options);
                    case MessageTypes.Pong:
                        return JsonSerializer.Deserialize<PongMessage>(json, Options);
                    case MessageTypes.Error:
                        return JsonSerializer.Deserialize<ErrorResponseMessage>(json, Options);
                    default:
                        UnknownServerMessage unknown = JsonSerializer.Deserialize<UnknownServerMessage>(json, Options) ?? new UnknownServerMessage();
                        unknown.Type = type ?? string.Empty;
                        unknown.RawJson = json;
                        return unknown;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

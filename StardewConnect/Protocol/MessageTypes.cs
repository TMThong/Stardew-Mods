namespace StardewConnect.Protocol
{
    /// <summary>Wire level message type discriminators shared with svd-connect-server.</summary>
    internal static class MessageTypes
    {
        // ----- client -> server -----
        public const string CreateRoom = "create_room";
        public const string JoinRoom = "join_room";
        public const string LeaveRoom = "leave_room";
        public const string CloseRoom = "close_room";
        public const string Ping = "ping";

        // ----- server -> client -----
        public const string RoomCreated = "room_created";
        public const string RoomJoined = "room_joined";
        public const string PeerJoined = "peer_joined";
        public const string PeerLeft = "peer_left";
        public const string HostDisconnected = "host_disconnected";
        public const string RoomClosed = "room_closed";
        public const string Pong = "pong";
        public const string Error = "error";

        // ----- both directions -----
        public const string WebRtcOffer = "webrtc_offer";
        public const string WebRtcAnswer = "webrtc_answer";
        public const string IceCandidate = "ice_candidate";
    }

    /// <summary>Error codes the signaling server can return.</summary>
    internal static class ErrorCodes
    {
        public const string InvalidMessage = "INVALID_MESSAGE";
        public const string MessageTooLarge = "MESSAGE_TOO_LARGE";
        public const string UnsupportedMessageType = "UNSUPPORTED_MESSAGE_TYPE";
        public const string InvalidRoomCredentials = "INVALID_ROOM_CREDENTIALS";
        public const string RoomFull = "ROOM_FULL";
        public const string AlreadyInRoom = "ALREADY_IN_ROOM";
        public const string NotInRoom = "NOT_IN_ROOM";
        public const string NotRoomHost = "NOT_ROOM_HOST";
        public const string PeerNotFound = "PEER_NOT_FOUND";
        public const string PeerIdMismatch = "PEER_ID_MISMATCH";
        public const string RoomMismatch = "ROOM_MISMATCH";
        public const string RateLimited = "RATE_LIMITED";
        public const string TooManyConnections = "TOO_MANY_CONNECTIONS";
        public const string TooManyFailedAttempts = "TOO_MANY_FAILED_ATTEMPTS";
        public const string ServerShuttingDown = "SERVER_SHUTTING_DOWN";
        public const string InternalError = "INTERNAL_ERROR";
    }

    /// <summary>Reasons carried by <c>room_closed</c>.</summary>
    internal static class RoomClosedReasons
    {
        public const string HostClosed = "host_closed";
        public const string Expired = "expired";
        public const string HostDisconnected = "host_disconnected";
        public const string ServerShutdown = "server_shutdown";
    }
}

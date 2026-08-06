# Stardew Connect - Signaling protocol

Canonical reference for the WebSocket protocol between the **StardewConnect** mod and
**svd-connect-server**.

- Transport: one WebSocket per peer, `wss://` in production.
- Encoding: UTF-8 JSON **text** frames. Binary frames are rejected.
- Size: capped by `MAX_MESSAGE_SIZE_BYTES` (default 65536) at the `ws` layer and re-checked
  in the router.
- Identity: `peerId` and `roomId` are assigned by the server. A client can never choose them.

C# types live in [`StardewConnect/Protocol`](../../StardewConnect/Protocol); the server
schemas live in [`svd-connect-server/src/protocol`](../../svd-connect-server/src/protocol).

---

## Client to server

| `type` | C# type | Purpose |
| --- | --- | --- |
| `create_room` | `CreateRoomRequest` | Create a room and become its host |
| `join_room` | `JoinRoomRequest` | Join an existing room |
| `leave_room` | `LeaveRoomRequest` | Leave the room |
| `close_room` | `CloseRoomRequest` | Host only: destroy the room |
| `webrtc_offer` | `WebRtcOfferMessage` | Relay an SDP offer to one peer |
| `webrtc_answer` | `WebRtcAnswerMessage` | Relay an SDP answer to one peer |
| `ice_candidate` | `IceCandidateMessage` | Relay one ICE candidate to one peer |
| `ping` | `PingRequest` | Application level liveness check |

### create_room

```json
{ "type": "create_room", "requestId": "uuid", "maxPlayers": 4 }
```

`maxPlayers` is optional and clamped to the server's `MAX_PLAYERS_PER_ROOM`.

### join_room

```json
{ "type": "join_room", "requestId": "uuid", "roomId": "A7KM4P", "password": "K8F2Q7MX" }
```

`roomId` is upper-cased server side. The password is validated only by length (1..128) so a
malformed password does not become an oracle for the real format.

### leave_room / close_room

```json
{ "type": "leave_room",  "requestId": "uuid" }
{ "type": "close_room",  "requestId": "uuid" }
```

### webrtc_offer / webrtc_answer

```json
{
  "type": "webrtc_offer",
  "roomId": "A7KM4P",
  "fromPeerId": "uuid",
  "targetPeerId": "uuid",
  "sdp": "v=0\r\n..."
}
```

`sdp` is capped by `MAX_SDP_LENGTH` (default 32768).

### ice_candidate

```json
{
  "type": "ice_candidate",
  "roomId": "A7KM4P",
  "fromPeerId": "uuid",
  "targetPeerId": "uuid",
  "candidate": {
    "candidate": "candidate:1 1 udp 2130706431 192.0.2.10 54321 typ host",
    "sdpMid": "0",
    "sdpMLineIndex": 0
  }
}
```

`sdpMid` and `sdpMLineIndex` may be `null`; the mod substitutes `"0"` / `0` when applying
the candidate. `candidate` is capped by `MAX_ICE_CANDIDATE_LENGTH` (default 1024).

### ping

```json
{ "type": "ping", "requestId": "uuid" }
```

Independent of the WebSocket ping/pong frames the server sends every
`HEARTBEAT_INTERVAL_SECONDS`; those are handled by `ClientWebSocket` automatically.

---

## Server to client

| `type` | C# type | When |
| --- | --- | --- |
| `room_created` | `RoomCreatedMessage` | Answer to `create_room` |
| `room_joined` | `RoomJoinedMessage` | Answer to `join_room` |
| `peer_joined` | `PeerJoinedMessage` | To the host, when someone joins |
| `peer_left` | `PeerLeftMessage` | To the host, when someone leaves |
| `host_disconnected` | `HostDisconnectedMessage` | To clients, when the host vanishes |
| `room_closed` | `RoomClosedMessage` | Room destroyed |
| `webrtc_offer` / `webrtc_answer` / `ice_candidate` | `*ReceivedMessage` | Relayed |
| `pong` | `PongMessage` | Answer to `ping` |
| `error` | `ErrorResponseMessage` | Anything rejected |

### room_created

```json
{
  "type": "room_created",
  "requestId": "uuid",
  "roomId": "A7KM4P",
  "password": "K8F2Q7MX",
  "peerId": "uuid",
  "maxPlayers": 8,
  "expiresAt": "2026-08-06T11:20:51.075Z"
}
```

The only time the plaintext password crosses the wire. The server keeps a scrypt hash and
nothing else.

### room_joined

```json
{
  "type": "room_joined",
  "requestId": "uuid",
  "roomId": "A7KM4P",
  "peerId": "uuid",
  "hostPeerId": "uuid",
  "maxPlayers": 8,
  "peers": ["uuid"]
}
```

`peers` lists the other members at the moment of joining (useful for a future mesh).

### peer_joined / peer_left

```json
{ "type": "peer_joined", "roomId": "A7KM4P", "peerId": "uuid", "peerCount": 2 }
{ "type": "peer_left",  "roomId": "A7KM4P", "peerId": "uuid", "peerCount": 1, "reason": "left" }
```

`reason` is `left`, `disconnected`, `timeout` or `kicked`.

### host_disconnected / room_closed

```json
{ "type": "host_disconnected", "roomId": "A7KM4P" }
{ "type": "room_closed", "roomId": "A7KM4P", "reason": "host_disconnected" }
```

`reason` is `host_closed`, `expired`, `host_disconnected` or `server_shutdown`.

### Relayed signaling

Forwarded verbatim to `targetPeerId`, except `fromPeerId`, which the server **rewrites** to
the peer id it issued to the sending socket. A client therefore cannot forge an identity
even if it lies in the payload.

### pong

```json
{ "type": "pong", "requestId": "uuid", "serverTime": "2026-08-06T11:20:51.075Z" }
```

### error

```json
{
  "type": "error",
  "requestId": "uuid",
  "code": "INVALID_ROOM_CREDENTIALS",
  "message": "Room ID or password is invalid."
}
```

| Code | Meaning | Mod behaviour |
| --- | --- | --- |
| `INVALID_MESSAGE` | Bad JSON, wrong shape, or a binary frame | Generic protocol error |
| `MESSAGE_TOO_LARGE` | Frame over `MAX_MESSAGE_SIZE_BYTES` | Generic protocol error |
| `UNSUPPORTED_MESSAGE_TYPE` | Unknown `type` | Generic protocol error |
| `INVALID_ROOM_CREDENTIALS` | Unknown room, expired room, hostless room **or** wrong password | "Room ID or password is invalid." |
| `ROOM_FULL` | `maxPlayers` reached | "That room is already full." |
| `ALREADY_IN_ROOM` | Socket already in a room | Generic protocol error |
| `NOT_IN_ROOM` | Action needs a room | Generic protocol error |
| `NOT_ROOM_HOST` | Host-only action | Generic protocol error |
| `PEER_NOT_FOUND` | `targetPeerId` not in this room | Generic protocol error |
| `PEER_ID_MISMATCH` | `fromPeerId` is not this socket's peer id | Generic protocol error |
| `ROOM_MISMATCH` | `roomId` is not this socket's room | Generic protocol error |
| `RATE_LIMITED` | Room-creation quota, or a join already in flight | "Too many requests..." |
| `TOO_MANY_CONNECTIONS` | Per-IP socket cap | "Too many connections from your network." |
| `TOO_MANY_FAILED_ATTEMPTS` | Password lockout for this IP + room | "Too many failed attempts..." |
| `SERVER_SHUTTING_DOWN` | Graceful shutdown | "The signaling server is shutting down." |
| `INTERNAL_ERROR` | Unexpected fault | Generic error |

Deliberate design choice: unknown room, expired room and wrong password all return the same
`INVALID_ROOM_CREDENTIALS`, and the "no such room" path still burns a throwaway scrypt
verification so response timing does not leak whether a room exists.

---

## Forward compatibility

The mod's `ServerMessageParser` maps any unrecognised `type` to `UnknownServerMessage` and
logs it at trace level instead of failing. New server messages will not break older mods.

---

## Data channel framing (peer to peer)

Once the `stardew-connect` data channel is open, `NetworkMessageRouter` frames every payload
as:

```text
[channel:1 byte][payload...]
```

| Channel | Use |
| --- | --- |
| `0` | Control. Reserved and handled by the router itself |
| `1` | Game payloads (`NetworkMessageRouter.GameChannel`) |
| `2..255` | Free for future features |

Control opcodes:

| Opcode | Payload | Meaning |
| --- | --- | --- |
| `1` | `int64` UTC ticks | Ping. The receiver echoes it back |
| `2` | `int64` UTC ticks | Pong. Drives `RoundTripMeasured` |

Maximum payload per message is 240 KiB at the router and 256 KiB at the transport.

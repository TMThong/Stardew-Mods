# svd-connect-server

WebRTC signaling server for [**Stardew Connect**](../StardewConnect) — a SMAPI mod that lets
players host private peer-to-peer multiplayer rooms.

The server does exactly four things:

1. creates rooms (random Room ID + password),
2. authenticates joins,
3. relays SDP offers/answers and ICE candidates between peers in the same room,
4. cleans up rooms when they expire or the host disappears.

**It never sees gameplay traffic.** Once the WebRTC DataChannel is up, game data flows
directly between host and clients.

System documentation lives in [`docs/StardewConnect`](../docs/StardewConnect):
[architecture](../docs/StardewConnect/architecture.md) ·
[protocol](../docs/StardewConnect/protocol.md) ·
[deployment](../docs/StardewConnect/deployment.md) ·
[debugging](../docs/StardewConnect/debugging.md) ·
[security checklist](../docs/StardewConnect/security-checklist.md) ·
[limitations](../docs/StardewConnect/limitations.md)

---

## Requirements

- Node.js **20+** (tested on 22 LTS)
- No database — rooms live in RAM only

## Quick start (local)

```bash
npm install
cp .env.example .env
npm run dev
```

The server listens on `ws://localhost:8080` (and serves `GET /health`, `GET /metrics`).

Run the end-to-end smoke test against it in a second terminal:

```bash
npm run manual-test
```

That script performs the full host → join → offer/answer/ICE → leave → close flow and
prints every frame, so it doubles as living protocol documentation.

## Scripts

| Command | What it does |
| --- | --- |
| `npm run dev` | Watch mode via `tsx` |
| `npm run build` | Type-check + emit `dist/` (ESM) |
| `npm run build:cjs` | Emit `dist-cjs/` (CommonJS) for hosts whose loader `require()`s the startup file - cPanel/LiteSpeed, Passenger |
| `npm start` | Run the compiled server (`dist/index.js`) |
| `npm run start:cjs` | Run the CommonJS build |
| `npm run typecheck` | Type-check `src/` **and** `tests/` without emitting |
| `npm test` | Full vitest suite (unit + real-WebSocket integration) |
| `npm run manual-test [url]` | Smoke test against a running server |

## Configuration

Every setting is an environment variable; see [`.env.example`](.env.example) for the full
list with defaults.

| Variable | Default | Notes |
| --- | --- | --- |
| `PORT` / `HOST` | `8080` / `0.0.0.0` | Listener |
| `NODE_ENV` | `development` | `production` enables JSON logs + the TLS policy check |
| `ROOM_TTL_MINUTES` | `30` | How long a room waits for its first client |
| `MAX_PLAYERS_PER_ROOM` | `8` | Includes the host |
| `CLEANUP_INTERVAL_SECONDS` | `30` | Expired-room sweep |
| `MAX_ROOMS_PER_IP_WINDOW` | `5` | Room creations per IP per window |
| `ROOM_CREATION_WINDOW_MINUTES` | `10` | Window for the above |
| `MAX_CONNECTIONS_PER_IP` | `10` | Concurrent sockets per IP |
| `MAX_FAILED_JOIN_ATTEMPTS` | `5` | Wrong passwords before an IP is locked out of *that room* |
| `FAILED_JOIN_LOCKOUT_MINUTES` | `5` | Lockout duration |
| `MAX_INVALID_MESSAGES_PER_CONNECTION` | `5` | Then the socket is closed with `1008` |
| `MAX_MESSAGE_SIZE_BYTES` | `65536` | Enforced by `ws` (`maxPayload`) and re-checked in the router |
| `MAX_SDP_LENGTH` | `32768` | Per-message SDP cap |
| `MAX_ICE_CANDIDATE_LENGTH` | `1024` | Per-candidate cap |
| `HEARTBEAT_INTERVAL_SECONDS` | `30` | WebSocket ping; a socket that misses one ping cycle is terminated |
| `LOG_LEVEL` | `info` | `error \| warn \| info \| debug \| trace` |
| `LOG_JSON` | auto | Defaults to `true` when `NODE_ENV=production` |
| `ALLOWED_ORIGINS` | *(empty)* | Comma-separated browser Origin allowlist. Native clients send no Origin and are always allowed |
| `TRUST_PROXY` | `false` | Read `X-Forwarded-For` / `X-Forwarded-Proto` |
| `REQUIRE_SECURE_TRANSPORT` | `true` | In production, refuse to start without TLS or `TRUST_PROXY` |
| `TLS_CERT_PATH` / `TLS_KEY_PATH` | *(empty)* | Optional built-in TLS instead of a reverse proxy |
| `METRICS_ENABLED` | `true` | Toggle `/metrics` |

## HTTP endpoints

| Endpoint | Response |
| --- | --- |
| `GET /health` | `{"status":"ok","uptimeSeconds":…,"rooms":…,"peers":…,"connections":…}` — `503` while shutting down |
| `GET /metrics` | Prometheus text format (`svdconnect_*` counters and gauges) |

---

## Protocol

One WebSocket connection = one peer. All frames are UTF-8 JSON text frames; binary frames
are rejected. The server assigns `peerId` and `roomId`; a client can never choose them.

### Client → Server

#### `create_room`

```json
{ "type": "create_room", "requestId": "uuid", "maxPlayers": 4 }
```

`maxPlayers` is optional and is clamped to `MAX_PLAYERS_PER_ROOM`.

#### `join_room`

```json
{ "type": "join_room", "requestId": "uuid", "roomId": "A7KM4P", "password": "K8F2Q7MX" }
```

#### `leave_room` / `close_room`

```json
{ "type": "leave_room", "requestId": "uuid" }
{ "type": "close_room", "requestId": "uuid" }
```

`close_room` is host-only; a client receives `NOT_ROOM_HOST`.

#### `webrtc_offer` / `webrtc_answer`

```json
{
  "type": "webrtc_offer",
  "roomId": "A7KM4P",
  "fromPeerId": "uuid",
  "targetPeerId": "uuid",
  "sdp": "v=0\r\n..."
}
```

#### `ice_candidate`

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

#### `ping`

```json
{ "type": "ping", "requestId": "uuid" }
```

Application-level liveness check, independent of the WebSocket ping/pong frames the server
sends every `HEARTBEAT_INTERVAL_SECONDS`.

### Server → Client

#### `room_created`

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

This is the only time the plaintext password is transmitted — the server keeps only a
scrypt hash.

#### `room_joined`

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

#### `peer_joined` / `peer_left`

```json
{ "type": "peer_joined", "roomId": "A7KM4P", "peerId": "uuid", "peerCount": 2 }
{ "type": "peer_left", "roomId": "A7KM4P", "peerId": "uuid", "peerCount": 1, "reason": "left" }
```

`reason` is one of `left`, `disconnected`, `timeout`, `kicked`.

#### `host_disconnected` / `room_closed`

```json
{ "type": "host_disconnected", "roomId": "A7KM4P" }
{ "type": "room_closed", "roomId": "A7KM4P", "reason": "host_disconnected" }
```

`reason` is one of `host_closed`, `expired`, `host_disconnected`, `server_shutdown`.

#### Relayed signaling

`webrtc_offer`, `webrtc_answer` and `ice_candidate` are forwarded verbatim to
`targetPeerId`, except that `fromPeerId` is rewritten to the **server-verified** peer id of
the sender.

#### `pong`

```json
{ "type": "pong", "requestId": "uuid", "serverTime": "2026-08-06T11:20:51.075Z" }
```

#### `error`

```json
{
  "type": "error",
  "requestId": "uuid",
  "code": "INVALID_ROOM_CREDENTIALS",
  "message": "Room ID or password is invalid."
}
```

| Code | Meaning |
| --- | --- |
| `INVALID_MESSAGE` | Malformed JSON, wrong shape, or a binary frame |
| `MESSAGE_TOO_LARGE` | Frame exceeded `MAX_MESSAGE_SIZE_BYTES` |
| `UNSUPPORTED_MESSAGE_TYPE` | Unknown `type` |
| `INVALID_ROOM_CREDENTIALS` | Unknown room, expired room, hostless room **or** wrong password |
| `ROOM_FULL` | `maxPlayers` reached |
| `ALREADY_IN_ROOM` | This socket already belongs to a room |
| `NOT_IN_ROOM` | Action requires being in a room |
| `NOT_ROOM_HOST` | Host-only action attempted by a client |
| `PEER_NOT_FOUND` | `targetPeerId` is not in this room |
| `PEER_ID_MISMATCH` | `fromPeerId` is not the id issued to this socket |
| `ROOM_MISMATCH` | `roomId` is not the room this socket is in |
| `RATE_LIMITED` | Room-creation quota or a join already in flight |
| `TOO_MANY_CONNECTIONS` | Per-IP concurrent connection cap |
| `TOO_MANY_FAILED_ATTEMPTS` | Password lockout for this IP + room |
| `SERVER_SHUTTING_DOWN` | Graceful shutdown in progress |
| `INTERNAL_ERROR` | Unexpected server fault |

### Typical exchange

```text
host                         server                        client
 |  create_room ------------->  |                             |
 |  <------------ room_created  |                             |
 |                              |  <--------------- join_room |
 |                              |  room_joined -------------> |
 |  <------------- peer_joined  |                             |
 |  webrtc_offer ------------>  |  webrtc_offer ------------> |
 |                              |  <----------- webrtc_answer |
 |  <----------- webrtc_answer  |                             |
 |  ice_candidate ----------->  |  ice_candidate -----------> |
 |                              |  <----------- ice_candidate |
 |  <----------- ice_candidate  |                             |
 |                                                            |
 |  ==== DataChannel "stardew-connect" (direct, P2P) ======== |
```

---

## Room lifecycle

- A room is created with the host as its first peer and `expiresAt = now + ROOM_TTL_MINUTES`.
- **While no client has joined**, the room is deleted once `expiresAt` passes.
- **Once a client is connected**, the deadline is ignored — the room lives as long as the
  host does. When the last client leaves, the deadline is re-armed.
- Host disconnects → every client gets `host_disconnected` + `room_closed`, and the room is
  destroyed immediately. A room never exists without a host.
- Client disconnects → the host gets `peer_left`; the room stays.
- A sweep runs every `CLEANUP_INTERVAL_SECONDS`.
- The server pings every socket every `HEARTBEAT_INTERVAL_SECONDS`; a socket that has not
  ponged by the next tick is terminated and cleaned up like a normal disconnect.

Keeping the signaling socket open after the DataChannel is up is intentional: it lets new
players join, supports ICE restart, and detects host loss.

## Security model

- Room IDs use a 32-symbol unambiguous alphabet (no `0`, `O`, `1`, `I`); IDs, passwords and
  peer IDs all come from `crypto.randomBytes` / `crypto.randomUUID`.
- Passwords are stored as scrypt hashes (`N=16384, r=8, p=1`) and compared with
  `timingSafeEqual`. A join against a non-existent room still performs a throwaway
  verification so response timing does not reveal whether a room exists.
- Unknown room, expired room and wrong password all return the same
  `INVALID_ROOM_CREDENTIALS` error.
- Rate limits: room creation per IP, concurrent connections per IP, failed joins per
  IP+room, invalid messages per connection, and one in-flight join per connection.
- Message size is capped at the `ws` layer (`maxPayload`) *and* re-checked in the router;
  SDP and ICE candidate lengths have their own caps.
- Peer identity is server-assigned. `fromPeerId` is validated against the socket's own peer
  id, and every relay is confined to the sender's room.
- SDP bodies and ICE candidates are **never** logged; only their length is.
- In production the server refuses to start unless TLS is configured locally or
  `TRUST_PROXY=true` (TLS terminated upstream). Clients must use `wss://`.
- `ALLOWED_ORIGINS` is defence in depth for browser clients only — it is never treated as
  authentication.

## Production deployment

### Docker

```bash
cp .env.example .env
docker compose up -d --build
docker compose logs -f
```

The compose file sets `NODE_ENV=production` and `TRUST_PROXY=true`, i.e. it expects TLS to
be terminated by a reverse proxy in front of it.

### Reverse proxy (nginx)

```nginx
server {
    listen 443 ssl http2;
    server_name connect.example.com;

    ssl_certificate     /etc/letsencrypt/live/connect.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/connect.example.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Signaling sockets stay open for the whole session.
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }
}
```

Then point the mod at `wss://connect.example.com`.

### Without Docker

```bash
npm ci --omit=dev
npm run build
NODE_ENV=production TRUST_PROXY=true node dist/index.js
```

Use a process supervisor (systemd, pm2) and forward `SIGTERM` — the server drains rooms,
notifies peers with `room_closed` (`server_shutdown`) and closes sockets with `1001`.

## Tests

```bash
npm test
```

Covered: Room ID generation (format, ambiguous characters, uniformity), password
generation, hash/verify, room create/close, join with the right and wrong password, full
rooms, host disconnect, client disconnect, room expiry, cross-room relay attempts, spoofed
`fromPeerId`, rate limits, oversized frames, invalid-JSON flooding, per-IP connection caps
and the HTTP endpoints.

## Scaling beyond one instance

Rooms are in-process by design. To run several instances, replace the in-memory maps behind
`RoomManager` and `RateLimiter` with a shared backend (Redis) and add pub/sub for the relay
step — nothing else in the codebase assumes a single process.

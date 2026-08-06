# Stardew Connect - Current limitations

Version 0.1.0. This is an honest list of what the current build does and does not do.

## What works today

- Room creation and joining through the signaling server, with generated Room ID / password.
- Full WebRTC negotiation: one `RTCPeerConnection` per peer, one reliable ordered data
  channel named `stardew-connect`, trickle ICE in both directions.
- A host serving several clients at once, with per-peer state shown in the host menu.
- A framed, multiplexed data path (`NetworkMessageRouter`) with a control channel that
  measures round trip time over the data channel.
- Room lifecycle, heartbeats, rate limiting and cleanup on the server.

### How that was verified

| Check | Result |
| --- | --- |
| Server unit + integration suite (`npm test`) | 74 tests pass, including host/client disconnect, expiry, spoofed `fromPeerId`, cross-room relay, rate limits |
| `node scripts/manual-test.mjs` against a running server | Full host/join/offer/answer/ICE/leave/close flow passes |
| Mod protocol types against a live server (headless harness) | 20 checks pass - serialisation, parsing, error mapping, forward compatibility |
| Real `WebRtcTransport` + `SignalingClient`, one host and two clients (headless harness) | 22 checks pass - data channels open, targeted send, broadcast, 64 KiB payload intact, RTT measured, one client leaving does not disturb the other |
| Packaged release zip | Loads SIPSorcery and creates a peer connection, data channel and offer using only the assemblies in the mod folder |

> **Not yet verified in-game.** The menus compile and follow standard SMAPI/Stardew UI
> patterns, but this build has not been run inside a live Stardew Valley session in the
> development environment used to produce it. Treat the first launch as a smoke test:
> press the menu key, host a room, and check the SMAPI log.

## What is deliberately not implemented

### No gameplay synchronisation

This is the big one. The mod establishes the connection and gives you a working, framed,
peer-to-peer pipe - it does **not** yet send any Stardew Valley state over it. Nothing about
farm state, players, time or items is serialised or applied. Registering a handler on
`NetworkMessageRouter.GameChannel` is where that work starts.

In other words: version 0.1.0 is the transport, not the multiplayer.

### No TURN by default

Only STUN servers are configured out of the box, so two players who are both behind
symmetric NAT will fail to connect (the peer state stops at `Connecting` and then `Failed`).
Add a TURN server to `IceServers` to fix it. Short-lived TURN credentials are supported by
the config shape but there is no service that issues them yet.

### Peer-to-peer exposes IP addresses

Without a TURN relay, WebRTC lets the peers in a room learn each other's IP addresses. This
is inherent to the technology, not a defect, but players should be told.

### One signaling instance

Rooms live in the process's memory. Running several instances behind a load balancer needs
shared state (Redis) and sticky sessions; scaling the container to 3 replicas today would
mean a client can land on an instance that has never heard of the room.

### A dropped signaling socket ends the room

Auto-reconnect is switched off while a room is active, on purpose: a reconnected socket is a
new session with no room attached, and silently creating a replacement would give the host a
different Room ID while their friends still hold the old one. The player is told the room
closed and has to create a new one.

### Not implemented (by design, for later)

- host migration
- matchmaking and a friends list
- mod compatibility handshake, Stardew Valley / Stardew Connect version checks, mod list sync
- application-layer encryption on top of DTLS
- dedicated server mode
- reconnecting into an existing room after a network blip

The architecture keeps room for all of these - see the extension table in
[architecture.md](architecture.md).

## Smaller rough edges

- **Settings are read-only in game.** The Settings panel shows the effective configuration;
  changing it means editing `config.json` and restarting. There is no Generic Mod Config Menu
  integration yet.
- **Clipboard support is best-effort.** It goes through the game's own clipboard helper by
  reflection; if that is unavailable the copy buttons report failure instead of crashing.
- **The mod bundles ~6.5 MB of dependencies** (SIPSorcery and its transitive closure). That is
  the cost of a pure managed WebRTC stack.
- **Room ids are 6 characters** from a 32-symbol alphabet. That is fine combined with the
  password and the lockout, but it is not a large key space on its own - do not remove the
  password.
- **`MAX_CONNECTIONS_PER_IP=10`** will get in the way of a LAN party behind a single NAT.
- **Only English translations** are shipped (`i18n/default.json`).

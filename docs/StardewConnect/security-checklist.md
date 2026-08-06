# Stardew Connect - Pre-release security checklist

Work through this before publishing the mod or exposing a signaling server to the internet.

## Transport

- [ ] The public signaling URL is `wss://`, with a valid certificate from a real CA.
- [ ] `NODE_ENV=production` is set, so the server refuses to boot without TLS or
      `TRUST_PROXY=true`.
- [ ] `REQUIRE_SECURE_TRANSPORT` is left at `true`.
- [ ] The default `SignalingServerUrl` shipped in the mod is **not** a `ws://` address for a
      host other than localhost. (The mod logs a warning if it is.)
- [ ] The reverse proxy sets `X-Forwarded-For` and `X-Forwarded-Proto`, and `TRUST_PROXY` is
      only enabled when such a proxy really is in front - otherwise the per-IP limits can be
      spoofed by a header.

## Secrets

- [ ] No TURN credential, API key or password is committed anywhere in the repository or
      baked into the mod DLL.
- [ ] TURN credentials, if used, are short-lived and issued per session.
- [ ] The room password only ever exists: on the server as a scrypt hash, in the
      `room_created` frame, and in the host's `RoomState` while the room is open. A joining
      client clears it as soon as the join request is sent.
- [ ] No log line, at any level, contains a password, an SDP body or an ICE candidate body.
      (Search the diff for `Password` and `sdp` before shipping.)
- [ ] `.env` is not committed; only `.env.example` is.

## Authentication and identity

- [ ] `peerId` and `roomId` are only ever assigned by the server.
- [ ] Every relay checks `fromPeerId == connection.peerId` and `roomId == connection.roomId`.
- [ ] A client cannot promote itself to host; `close_room` is host-only.
- [ ] Unknown room, expired room and wrong password all return `INVALID_ROOM_CREDENTIALS`,
      and the "no such room" path still burns a scrypt verification so timing does not leak.
- [ ] Room ids come from `crypto.randomBytes` over a 32-symbol alphabet (~30 bits for 6
      characters) and passwords add ~40 bits. Guessing needs both, and the lockout below
      makes that impractical.

## Abuse resistance

- [ ] `MAX_ROOMS_PER_IP_WINDOW` / `ROOM_CREATION_WINDOW_MINUTES` are tuned for your audience.
- [ ] `MAX_CONNECTIONS_PER_IP` is set high enough for a shared household but not unlimited.
- [ ] `MAX_FAILED_JOIN_ATTEMPTS` + `FAILED_JOIN_LOCKOUT_MINUTES` are active; the lockout is
      per IP **and** room, so one attacker cannot lock a legitimate player out of every room.
- [ ] `MAX_MESSAGE_SIZE_BYTES`, `MAX_SDP_LENGTH` and `MAX_ICE_CANDIDATE_LENGTH` are enforced.
- [ ] A connection that sends repeated garbage is closed
      (`MAX_INVALID_MESSAGES_PER_CONNECTION`).
- [ ] Only one join attempt can be in flight per connection, so scrypt cannot be used as a
      CPU amplifier.
- [ ] Heartbeat ping/pong is on, and unresponsive sockets are terminated and cleaned up.

## Room lifecycle

- [ ] A room is destroyed the moment its host disconnects; it never outlives the host.
- [ ] Rooms waiting for their first player expire after `ROOM_TTL_MINUTES`.
- [ ] The cleanup sweep runs (`CLEANUP_INTERVAL_SECONDS`) and expired rooms notify their
      members with `room_closed`.
- [ ] Graceful shutdown notifies peers (`server_shutdown`) rather than dropping them silently.

## Privacy

- [ ] Players understand that a direct peer-to-peer connection reveals their IP address to
      the other players in the room - this is inherent to WebRTC without a TURN relay. Say so
      in the mod description.
- [ ] Forcing traffic through TURN (`iceTransportPolicy: relay`) is the mitigation if you
      ever need to hide addresses; it costs bandwidth on your relay.
- [ ] The server stores nothing after a room closes: no database, no files, no logs of room
      contents.

## Supply chain

- [ ] `npm audit --omit=dev` is clean for the server. (It is, as of 0.1.0.)
- [ ] SIPSorcery is current. Older 8.x releases carry a published advisory; 0.1.0 pins
      10.0.13.
- [ ] `dotnet list package --vulnerable --include-transitive` has been reviewed. As of
      0.1.0 it reports `System.Net.Http 4.3.0` and `System.Text.RegularExpressions 4.3.0`,
      pulled in through SIPSorcery's `NETStandard.Library` chain. **These are compile-time
      facades on .NET 6** - the implementations come from the shared framework and neither
      package DLL is present in the build output or the release zip. Re-check this whenever
      SIPSorcery is upgraded, and confirm the zip still contains no `System.Net.Http.dll` or
      `System.Text.RegularExpressions.dll`.
- [ ] The release zip contains exactly the assemblies the mod needs and nothing unexpected.
- [ ] Dependencies are pinned in `package-lock.json` and the `.csproj`.

## Before you tag a release

- [ ] `npm test` passes in `svd-connect-server`.
- [ ] `npm run typecheck` passes.
- [ ] `dotnet build -c Release` produces no warnings.
- [ ] `node scripts/manual-test.mjs wss://your-server` passes against the production server.
- [ ] A real host + client pair has connected over the internet, not just over loopback.
- [ ] The `manifest.json` version, the `.csproj` version and the changelog agree.

# Stardew Connect - Documentation

Peer-to-peer multiplayer rooms for Stardew Valley: a SMAPI mod plus a small WebRTC
signaling server. The server introduces two players to each other and then gets out of the
way - all game traffic travels directly between them.

| Document | What is in it |
| --- | --- |
| [architecture.md](architecture.md) | Component map, threading model, connection flow, room lifecycle, extension points |
| [protocol.md](protocol.md) | Every signaling message, every error code, and the peer-to-peer data channel framing |
| [deployment.md](deployment.md) | Running the server locally and in production, TLS, nginx/systemd/Docker, and how to point the mod at it |
| [debugging.md](debugging.md) | Console commands, how to isolate which layer failed, symptom table |
| [security-checklist.md](security-checklist.md) | What to verify before publishing |
| [limitations.md](limitations.md) | What works, how it was verified, and what is deliberately not built yet |

## The two projects

| Project | Path | Stack |
| --- | --- | --- |
| Mod | [`StardewConnect/`](../../StardewConnect) | C#, .NET 6, SMAPI 4.x, SIPSorcery |
| Signaling server | [`svd-connect-server/`](../../svd-connect-server) | Node.js 20+, TypeScript strict, `ws`, Zod |

## Sixty second start

```bash
cd svd-connect-server
npm install && cp .env.example .env && npm run dev
```

```bash
cd StardewConnect
dotnet build -c Release
```

Set `SignalingServerUrl` to `ws://localhost:8080` in the mod's `config.json`, launch the
game, press **F9**, and choose **Host Game**.

## Where the boundaries are

- The signaling server handles rooms, authentication and relaying SDP/ICE. It never touches
  gameplay data, and it stores nothing on disk.
- The mod keeps signaling, transport and UI apart: `ISignalingClient`, `IWebRtcTransport` and
  `RoomSession` are separate types, and swapping WebRTC for another transport means writing
  one class.
- Every network callback is marshalled onto the game thread before it touches game state.

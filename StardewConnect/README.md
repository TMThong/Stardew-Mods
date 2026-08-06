# Stardew Connect

A SMAPI mod that connects you directly with friends through private peer-to-peer rooms.
A small [signaling server](../svd-connect-server) introduces the players to each other;
after that, game traffic flows straight between them over a WebRTC data channel.

Full documentation: [`docs/StardewConnect`](../docs/StardewConnect).

---

## Requirements

- Stardew Valley 1.6+
- SMAPI 4.0.0+ (.NET 6)
- A signaling server you (or a friend) runs - see [deployment](../docs/StardewConnect/deployment.md)

## Install

1. Build the release zip:

   ```bash
   dotnet build StardewConnect.csproj -c Release
   ```

   The zip lands in `bin/Release/net6.0/`.

2. Extract it into `Stardew Valley/Mods/`.
3. Launch the game once so `config.json` is created, then set your signaling URL.

The mod ships SIPSorcery and its dependencies inside its own folder; do not delete the
extra `.dll` files next to `StardewConnect.dll`.

## Configuration

`Mods/StardewConnect/config.json`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `SignalingServerUrl` | `wss://localhost:8080` | Your signaling server. Use `wss://` for anything remote |
| `OpenMenuKey` | `F9` | Opens the Stardew Connect menu |
| `MaxPlayers` | `8` | Players per hosted room, including the host |
| `ConnectTimeoutSeconds` | `15` | WebSocket handshake timeout |
| `RequestTimeoutSeconds` | `20` | How long to wait for `room_created` / `room_joined` |
| `HeartbeatIntervalSeconds` | `20` | Application level ping interval |
| `AutoReconnectSignaling` | `true` | Reconnect while idle. Never re-creates a room |
| `MaxReconnectAttempts` | `5` | Before the session is marked failed |
| `VerboseLogging` | `false` | Logs message types. Never logs passwords or SDP |
| `UseMockWebRtcTransport` | `false` | Runs signaling with synthetic SDP/ICE, for debugging only |
| `IceServers` | Google STUN | STUN/TURN servers used for ICE |

Add a TURN server if players are behind symmetric NAT:

```json
"IceServers": [
  { "Urls": "stun:stun.l.google.com:19302", "Username": "", "Credential": "" },
  { "Urls": "turn:turn.example.com:3478", "Username": "user", "Credential": "secret" }
]
```

## Using it

**Host**

1. Press `F9`, choose **Host Game**.
2. The room menu shows the Room ID and password.
3. **Copy Invite** puts this on your clipboard:

   ```text
   Join my Stardew Connect room
   Room ID: A7KM4P
   Password: K8F2Q7MX
   ```

4. The peer list shows each client's WebRTC state and round trip time as they connect.
5. **Close Room** ends the session for everyone.

**Client**

1. Press `F9`, choose **Join Game**.
2. Type the Room ID and password (Tab switches fields, Enter joins).
3. The status line walks through *Joining room -> Negotiating WebRTC -> Connected*.

A room with nobody in it expires after 30 minutes by default. Once someone has joined, the
room lives as long as the host does.

## Console commands

| Command | Purpose |
| --- | --- |
| `sdc_status` | Print the full session state |
| `sdc_host` | Create a room without the menu |
| `sdc_join <roomId> <password>` | Join a room |
| `sdc_leave` | Leave, or close the room when hosting |

## Project layout

```text
StardewConnect/
├── ModEntry.cs                  wiring and lifetime only
├── Configuration/ModConfig.cs
├── Networking/
│   ├── ISignalingClient.cs      signaling contract
│   ├── SignalingClient.cs       ClientWebSocket, correlation, heartbeat, backoff
│   ├── IWebRtcTransport.cs      swappable peer-to-peer contract
│   ├── WebRtcTransport.cs       SIPSorcery implementation
│   ├── MockWebRtcTransport.cs   signaling-only test double
│   ├── PeerSession.cs
│   └── NetworkMessageRouter.cs  channel multiplexing over the data channel
├── Protocol/                    wire types (System.Text.Json)
├── Rooms/                       RoomSession orchestration, RoomState
├── UI/                          main / host / join menus
└── Utilities/                   MainThreadDispatcher, ClipboardHelper, Translations
```

## Status

Version 0.1.0 establishes the connection; it does **not** yet synchronise game state.
See [limitations](../docs/StardewConnect/limitations.md) for exactly what is and is not done,
and how each piece was verified.

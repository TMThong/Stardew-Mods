# Stardew Connect - Debugging guide

## Turn on the details first

`Mods/StardewConnect/config.json`:

```json
{ "VerboseLogging": true }
```

Then run SMAPI with trace logging (`smapi --console` or set the log level in
`smapi-internal/config.json`). Verbose mode logs every signaling message **type** - never
the password, never the SDP body.

Server side:

```env
LOG_LEVEL=debug
```

`debug` adds one line per relayed message with the peer ids and the payload *length*. SDP
and ICE bodies are never logged at any level, in any environment.

---

## In-game console commands

| Command | What it does |
| --- | --- |
| `sdc_status` | Dumps connection state, signaling state, room id, local peer id and every peer's WebRTC state |
| `sdc_host` | Creates a room without opening the menu |
| `sdc_join <roomId> <password>` | Joins a room |
| `sdc_leave` | Leaves (or closes, when hosting) |

`sdc_status` is the fastest way to tell *where* a connection died:

```text
Connection : NegotiatingWebRtc     <- signaling worked, ICE has not completed
Signaling  : Connected (wss://...)
Role       : Host
Room       : A7KM4P
Players    : 2/8
  peer 8c83e0bc: WaitingForAnswer   <- the client never answered
```

---

## Isolating the layer that failed

### 1. Is the server reachable?

```bash
curl -s https://connect.example.com/health
```

No answer means DNS, firewall or the proxy - the mod is not involved yet.

### 2. Does signaling work at all?

```bash
cd svd-connect-server
node scripts/manual-test.mjs wss://connect.example.com
```

This performs the entire host/join/offer/answer/ICE/leave/close choreography with plain
Node and prints every frame. If this passes, the server is fine and the problem is on the
game side.

### 3. Does the mod's signaling work, but WebRTC does not?

Set:

```json
{ "UseMockWebRtcTransport": true }
```

The mock runs the full signaling choreography with synthetic SDP and ICE. If rooms,
joining and peer lists work with the mock but not without it, the fault is ICE/NAT, not
your server or the protocol.

> The mock never delivers game data - it only proves signaling. Switch it back off
> afterwards.

---

## Symptom table

| Symptom | Likely cause | What to do |
| --- | --- | --- |
| "Could not reach the signaling server." | Wrong URL, TLS failure, firewall | `curl /health`; check `SignalingServerUrl` scheme is `wss://` |
| "Room ID or password is invalid." right after the host created the room | Room expired (30 min default), or a typo (`0`/`O`, `1`/`I` are never used in room ids) | Have the host use Copy Invite instead of reading it out |
| "Too many failed attempts." | `MAX_FAILED_JOIN_ATTEMPTS` lockout for that IP + room | Wait `FAILED_JOIN_LOCKOUT_MINUTES`; the lock is per room, not global |
| "Too many connections from your network." | Several players behind one NAT exceeding `MAX_CONNECTIONS_PER_IP` | Raise the limit on the server |
| Status stays `NegotiatingWebRtc` forever | ICE cannot find a path (symmetric NAT, both sides) | Add a TURN server to `IceServers` |
| Peer shows `Failed` with "ICE could not find a path" | Same as above, confirmed | TURN |
| Connects, then drops after ~30 s | A proxy is closing idle WebSockets | Raise `proxy_read_timeout`; the server pings every 30 s by default |
| `host_disconnected` immediately | The host's socket died; the room is destroyed on purpose | Check the host's SMAPI log for the signaling error |
| Mod fails to load with `FileNotFoundException` | The mod folder is missing a bundled dependency | Reinstall the release zip - it must contain `SIPSorcery.dll`, `BouncyCastle.Cryptography.dll`, `Microsoft.Extensions.Logging.Abstractions.dll`, `System.Net.IPNetwork.dll` and friends |
| SMAPI skips the mod: "its DLL couldn't be loaded", `FileLoadException: Could not load file or assembly '...'` | A bundled assembly collides with a different version the game already loaded | See "Assembly version collisions" below |
| SMAPI skips the mod: `Rewriting X.dll failed` / `Mono.Cecil.AssemblyResolutionException: Failed to resolve assembly` | A dependency exists only under `runtimes/<rid>/lib/`, where Cecil does not look | See "RID-specific assemblies" below |

---

## Assembly version collisions

Symptom in the SMAPI log:

```text
- Stardew Connect 0.1.0 because its DLL couldn't be loaded.
  (Error: FileLoadException: Could not load file or assembly
   'Microsoft.Extensions.DependencyInjection.Abstractions, Version=8.0.0.0, ...')
```

Cause: SMAPI eagerly `Assembly.LoadFrom()`s **every** DLL in the mod folder. Stardew Valley
itself ships some of the same assemblies and has already loaded them into the default load
context. Two files with the same simple name but different versions cannot coexist there, so
the load throws and SMAPI skips the whole mod.

.NET will happily bind a *higher* version than requested, but never a lower one - so the
rule is:

- the game ships a **newer** copy than we need -> do not bundle ours;
- the game ships an **older** copy than we need -> bundling ours breaks mod loading; check
  whether the dependency is actually reached at runtime, and if it is not, drop it;
- the game does not ship it at all -> bundle ours.

`Microsoft.Extensions.DependencyInjection.Abstractions` is exactly the middle case: the game
carries 5.0.0.0, SIPSorcery's package graph pulls 8.0.0.0, and SIPSorcery never binds to it
on the code paths this mod uses (a full DataChannel session was verified with only 5.0.0.0
present). It is therefore excluded from the bundle in `StardewConnect.csproj`:

```xml
<IgnoreModFilePaths>Microsoft.Extensions.DependencyInjection.Abstractions.dll</IgnoreModFilePaths>
```

### Auditing the bundle after a dependency upgrade

Run this after changing any NuGet reference; it lists every bundled assembly that also
exists in the game folder with a different version:

```powershell
$game = "C:\Path\To\Stardew Valley"
$out  = "StardewConnect\bin\Release\net6.0"
Get-ChildItem $out -Filter *.dll | ForEach-Object {
    $gamePath = Join-Path $game $_.Name
    if (Test-Path $gamePath) {
        $mine  = ([Reflection.AssemblyName]::GetAssemblyName($_.FullName)).Version
        $their = ([Reflection.AssemblyName]::GetAssemblyName($gamePath)).Version
        if ($mine -ne $their) { "COLLISION $($_.Name): mod=$mine game=$their" }
    }
}
```

Anything it prints will stop the mod from loading.

## RID-specific assemblies

Symptom in the SMAPI log:

```text
- Stardew Connect 0.1.0 because its DLL couldn't be loaded.
  (Error: System.Exception: Rewriting Makaretu.Dns.Multicast.dll failed.
   ---> Mono.Cecil.AssemblyResolutionException:
        Failed to resolve assembly: 'Tmds.LibC, Version=0.2.0.0, ...')
```

Before loading a mod, SMAPI rewrites each of its assemblies with Mono.Cecil, and Cecil has
to resolve **every** assembly reference to do that. Its resolver probes the mod folder root
only. NuGet, however, places platform-specific dependencies under
`runtimes/<rid>/lib/<tfm>/`, so they are shipped but invisible to the resolver.

Here `Tmds.LibC` arrives that way (Makaretu.Dns.Multicast -> Tmds.LibC, pulled in by
SIPSorcery's mDNS support). The build copies one copy to the mod folder root:

```xml
<Target Name="FlattenRidAssembliesForCecil" AfterTargets="Build" BeforeTargets="AfterBuild">
  <ItemGroup>
    <RidOnlyAssemblies Include="$(TargetDir)runtimes\**\lib\**\*.dll" />
  </ItemGroup>
  <Copy SourceFiles="@(RidOnlyAssemblies)" DestinationFolder="$(TargetDir)" SkipUnchangedFiles="true" />
</Target>
```

The assembly is Linux-only at runtime and never executes on Windows - it only has to be
*resolvable*.

### Checking the whole folder before shipping

The reliable pre-release gate is to do what SMAPI does: read every DLL in the packaged mod
folder with Mono.Cecil and resolve all of its references against the mod folder plus the
game folder. A ~60 line console app using `Mono.Cecil` and `DefaultAssemblyResolver` with
both folders added as search directories will reproduce this class of failure exactly,
before a player ever sees it. Run it against the extracted release zip; anything it reports
as unresolved is a mod that will not load.

## Reading the state machine

`ConnectionState` tells you exactly how far the session got:

```text
Disconnected -> ConnectingToSignaling -> CreatingRoom  -> WaitingForPlayers  (host)
Disconnected -> ConnectingToSignaling -> JoiningRoom   -> NegotiatingWebRtc  (client)
                                       -> NegotiatingWebRtc -> Connected
                                       -> Reconnecting / Failed
```

Per peer, `PeerConnectionState` goes
`New -> CreatingOffer -> WaitingForAnswer -> Connecting -> Connected`, and the host menu
shows this per client along with the data channel round trip time.

Where it stops tells you which step to investigate:

- stuck at `CreatingOffer` -> SIPSorcery could not create the peer connection; check the
  SMAPI log for a WebRTC error;
- stuck at `WaitingForAnswer` -> the client never received the offer or never answered;
  check the client's log and the server's `svdconnect_messages_relayed_total`;
- stuck at `Connecting` -> descriptions were exchanged but ICE has not connected -> NAT.

---

## Reconnect behaviour (and why the room does not come back)

The signaling client reconnects with exponential backoff (2s, 4s, 8s, 16s, 30s + jitter,
up to `MaxReconnectAttempts`) **only while no room is active**. Once a room exists,
auto-reconnect is switched off deliberately: a reconnected socket is a brand new session on
the server with no room attached, so silently recreating one would hand the player a
different Room ID while their friends still hold the old one.

If you see "Lost the connection to the signaling server; the room was closed.", that is the
intended behaviour, not a bug. Create a new room and share the new code.

---

## Server-side diagnostics

```bash
# live counters
curl -s localhost:8080/metrics | grep -E 'active_rooms|active_peers|join_failures'

# who is being rejected and why (debug level)
docker compose logs -f | grep -E 'join rejected|rejected spoofed|rejected cross-room'
```

The three warnings worth reading carefully:

- `join rejected` - includes the internal reason (`unknown_room`, `bad_password`, ...) that
  the client is deliberately *not* told;
- `rejected spoofed fromPeerId` - a client claimed a peer id that is not its own;
- `rejected cross-room relay` - a client tried to send into a room it is not in.

Occasional entries are normal (typos, races on disconnect). A stream of them from one
address is an attack, and the rate limiter should already be handling it.

---

## Running the automated checks

```bash
cd svd-connect-server
npm test            # 74 tests: unit + real-WebSocket integration
npm run typecheck
```

```bash
cd StardewConnect
dotnet build StardewConnect.csproj -c Release
```

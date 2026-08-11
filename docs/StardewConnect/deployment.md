# Stardew Connect - Deploying the signaling server

## Requirements

- Node.js 20+ (22 LTS recommended)
- A domain name and a TLS certificate (the mod should only talk `wss://` in production)
- No database

One small VPS is plenty: rooms are a few hundred bytes each and no media flows through the
server.

---

## Local development

```bash
cd svd-connect-server
npm install
cp .env.example .env
npm run dev
```

Then point the mod's `config.json` at `ws://localhost:8080`.

Smoke test in a second terminal:

```bash
npm run manual-test
```

---

## Production with Docker (recommended)

```bash
cd svd-connect-server
cp .env.example .env
# edit .env: NODE_ENV=production, TRUST_PROXY=true, limits to taste
docker compose up -d --build
docker compose logs -f
```

The compose file publishes `8080` on localhost and expects TLS to be terminated upstream.

### nginx in front

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

        # Signaling sockets stay open for a whole session.
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }
}
```

`TRUST_PROXY=true` is what makes the server read `X-Forwarded-For` - without it every
connection looks like it comes from the proxy and the per-IP limits become useless.

### Caddy alternative

```caddyfile
connect.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

---

## Production without Docker

```bash
cd svd-connect-server
npm ci --omit=dev
npm run build
NODE_ENV=production TRUST_PROXY=true node dist/index.js
```

systemd unit:

```ini
[Unit]
Description=svd-connect-server
After=network.target

[Service]
Type=simple
User=svdconnect
WorkingDirectory=/opt/svd-connect-server
EnvironmentFile=/opt/svd-connect-server/.env
ExecStart=/usr/bin/node dist/index.js
Restart=on-failure
RestartSec=5
# The server drains rooms and notifies peers on SIGTERM.
KillSignal=SIGTERM
TimeoutStopSec=15

[Install]
WantedBy=multi-user.target
```

### TLS terminated by the server itself

If you would rather not run a proxy:

```env
TLS_CERT_PATH=/etc/letsencrypt/live/connect.example.com/fullchain.pem
TLS_KEY_PATH=/etc/letsencrypt/live/connect.example.com/privkey.pem
```

The process must be able to read both files, and it must be restarted after each renewal.

> With `NODE_ENV=production` the server **refuses to start** unless TLS is configured or
> `TRUST_PROXY=true`. Set `REQUIRE_SECURE_TRANSPORT=false` only if you really know why.

---

## cPanel (LiteSpeed or Passenger)

cPanel's "Setup Node.js App" runs your code through a loader that does
`require(startupFile)` - `lsnode.js` on LiteSpeed, Passenger elsewhere. This package is
ESM (`"type": "module"`), so the default `dist/` build fails immediately with:

```text
Error [ERR_REQUIRE_ESM]: require() of ES Module .../index.js is not supported
    at startApplication (/usr/local/lsws/fcgi-bin/lsnode.js:48:15)
```

### Use the CommonJS build

```bash
npm ci
npm run build:cjs
```

`dist-cjs/` is **self-contained and deploy-ready**. The build writes a
`dist-cjs/package.json` that both overrides the parent package with `"type": "commonjs"`
and carries the runtime `dependencies`, so the host can install them.

Upload the **contents** of `dist-cjs/` - and nothing else - to your application root,
keeping the `protocol/`, `rooms/`, `security/`, `utilities/` and `websocket/` subfolders:

```text
sigServer/
├── package.json          <- from dist-cjs, do NOT replace it
├── index.js
├── config.js
├── protocol/
├── rooms/
├── security/
├── utilities/
└── websocket/
```

> Do not also upload the project's own `package.json`. It would overwrite the generated one,
> reintroduce `"type": "module"` and drop the `main` entry - and since it lives one level up
> from the compiled files, its dependency list is not what the host installs. Everything the
> deployment needs is already in `dist-cjs/package.json`.

| cPanel field | Value |
| --- | --- |
| Node.js version | 20 or newer |
| Application root | e.g. `sigServer` |
| Application startup file | `index.js` |
| Environment variables | `NODE_ENV=production`, `TRUST_PROXY=true` |

Then press **Run NPM Install** and start the app. Without that step the process dies with
`Error: Cannot find module 'zod'` - the compiled code is uploaded, but its two runtime
dependencies (`ws`, `zod`) are not.

Do not upload your local `node_modules`: cPanel's Node app manager replaces that folder with
a symlink into a per-application virtualenv, and a real directory there fights with it.

`TRUST_PROXY=true` is not optional here: without it every connection appears to come from
the web server's own address and all per-IP limits become meaningless.

### Alternative: a CommonJS shim

If you would rather keep the ESM build, put this next to it and point the startup file at
it instead:

```javascript
// app.cjs
"use strict";
import("./index.js").catch((error) => {
  console.error(error);
  process.exit(1);
});
```

`.cjs` is always CommonJS regardless of `"type": "module"`, and the dynamic `import()`
loads the ESM entry point. The CommonJS build above is preferred - it has one less moving
part and gives clearer stack traces.

### The wall that comes after the ESM error

Fixing the loader gets the process to boot. It does **not** guarantee WebSockets work.

LiteSpeed's LSAPI bridge (and Passenger's) is request oriented; an HTTP `Upgrade` to
WebSocket does not reliably pass through the Node app manager. On LiteSpeed the supported
route is a **WebSocket Proxy** entry mapping a URI to a host:port your app listens on,
configured in the LiteSpeed WebAdmin console (WHM/root) - on shared hosting you have to ask
the provider to add it.

Verify before assuming it works:

```bash
curl -s https://your-domain.com/health
```

```bash
node scripts/manual-test.mjs wss://your-domain.com
```

`/health` answering proves the process is alive and proxied. Only the second command proves
the upgrade path works: it runs the entire create → join → offer/answer/ICE → leave → close
choreography. If it hangs at the first frame, the host is not tunnelling WebSockets and no
application-side change can fix it.

### Why shared cPanel is a poor fit anyway

- **Entry process limits.** Every WebSocket occupies a web server worker and an LVE entry
  process for the whole session. Shared plans typically cap this around 20-40, so a couple of
  full rooms can take the rest of the account's sites down with `508 Resource Limit Reached`.
- **Idle reaping.** The app manager stops an app that has seen no requests for a few minutes.
  A restart wipes every room, because rooms live in RAM by design.
- **No control over timeouts.** Signaling sockets are meant to stay open for a whole play
  session.

A small VPS, or a container host such as Fly.io / Railway / Render, avoids all three and
costs about the same. On a cPanel **VPS with root**, skip the Node app manager entirely: run
the systemd unit below and add a LiteSpeed/Apache WebSocket proxy entry pointing at it.

## Health and monitoring

| Endpoint | Use |
| --- | --- |
| `GET /health` | Load balancer probe. `200` normally, `503` while draining |
| `GET /metrics` | Prometheus text format |

```bash
curl -s https://connect.example.com/health
curl -s https://connect.example.com/metrics | grep svdconnect_active_rooms
```

Metrics worth alerting on:

- `svdconnect_active_rooms`, `svdconnect_active_connections` - capacity
- `svdconnect_join_failures_total` - a sudden spike means someone is guessing room ids
- `svdconnect_rate_limited_total` - abuse, or limits set too tight
- `svdconnect_heartbeat_timeouts_total` - network trouble between players and the server

---

## Pointing the mod at your server

Edit `Stardew Valley/Mods/StardewConnect/config.json`:

```json
{
  "SignalingServerUrl": "wss://connect.example.com",
  "OpenMenuKey": "F9",
  "MaxPlayers": 8,
  "IceServers": [
    { "Urls": "stun:stun.l.google.com:19302", "Username": "", "Credential": "" }
  ]
}
```

The file is created on first launch. The mod logs a warning if the URL is a plaintext
`ws://` address pointing at anything other than localhost.

Adding a TURN server (needed for players behind symmetric NAT):

```json
"IceServers": [
  { "Urls": "stun:stun.l.google.com:19302", "Username": "", "Credential": "" },
  { "Urls": "turn:turn.example.com:3478", "Username": "user", "Credential": "secret" }
]
```

> Do not ship long-lived TURN credentials inside the mod. Issue short-lived credentials from
> your own service and write them into `config.json` at runtime, or add a small endpoint to
> the signaling server that hands them out per session.

---

## Capacity notes

- Memory per room is negligible; the practical limit is concurrent WebSocket connections.
- Rooms are per-process. Running several instances behind a load balancer needs sticky
  sessions **and** shared state (see the Redis note in the architecture document); do not
  simply scale the container to 3 replicas.
- The default `MAX_CONNECTIONS_PER_IP=10` is friendly to shared households but will bite a
  LAN party behind one NAT. Raise it deliberately, not by disabling the limit.

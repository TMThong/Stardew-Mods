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

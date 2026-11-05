# LAN / VPN install (no TLS)

For a private box reached by IP, where TLS is terminated nowhere. Verified on Debian 13.1 (LXC,
2 vCPU / 4 GB) at `http://192.168.1.66:8080`.

## What differs from a public install

| Setting | Value | Why |
|---|---|---|
| `HTTP_USEHTTPS` | `false` | otherwise HSTS is advertised and the app redirects to an HTTPS port it does not serve |
| `EMAIL_ENABLED` | `false` | invite links are displayed once to the admin instead of being emailed |
| app port | `8080:8080` | the release compose binds `127.0.0.1:8080` for a reverse proxy; a LAN install publishes it |
| cookies | unchanged (`SameAsRequest`) | no code change needed: over HTTP the cookie is issued without `Secure` |

Sign-in, invitations and board writes were confirmed over plain HTTP. Session cookies are readable
by anyone on the segment — acceptable on a trusted VLAN/VPN, not on a shared network.

## Proxmox / LXC notes

- Docker inside an LXC needs **nesting** enabled on the container.
- Debian's `docker.io` package in trixie does **not** ship the CLI: install `docker-cli` separately,
  plus `docker-compose` (2.26) and `containerd`.
- `compose.release.yaml`'s app healthcheck runs `/dev/tcp` through `CMD-SHELL`, which is **dash** in
  the aspnet image and fails, leaving a perfectly healthy app marked `unhealthy`. The probe now runs
  `bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080'`.
- The container's `/etc/resolv.conf` pointed at a gateway that did not answer DNS. Outbound TCP to
  `1.1.1.1:53` worked, so the fix was `nameserver 1.1.1.1`. Proxmox may rewrite the file on boot.
- `registry-1.docker.io` was **unreachable** from this network even with DNS fixed. Base images were
  transferred from a workstation:
  `docker save postgres:17-alpine redis:7.4-alpine | gzip | ssh root@host 'gunzip | docker load'`.
  The application image is transferred the same way.

## Deploy

```bash
mkdir -p /opt/tuesday/keys && chmod 700 /opt/tuesday
scp .env compose.yaml root@host:/opt/tuesday/      # compose.yaml = docker/compose.release.yaml with 8080:8080
scp keys/idp.pfx root@host:/opt/tuesday/keys/
ssh root@host 'cd /opt/tuesday && docker compose up -d'
curl -fsS http://host:8080/health/ready
```

`.env` holds `POSTGRES_PASSWORD`, `IDENTITY_CLIENT_SECRET`, `IDENTITY_SIGNING_PASSWORD`,
`BOOTSTRAP_ADMIN_*` and `TUESDAY_TAG`; keep it `chmod 600` and out of git.

First start applies all migrations and creates the bootstrap admin only while no tenant exists.

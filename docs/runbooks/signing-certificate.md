# Runbook: generate and rotate the OpenIddict signing certificate

**Why.** Outside Development the app refuses to start without a signing certificate:

```
System.InvalidOperationException: IdentityServer:SigningCertificatePath is required outside Development.
Supply a PFX so issued tokens survive a restart.
```

Development uses ephemeral certificates, which is why this only bites on a real deployment.

**What it covers.** One self-signed PFX used for both signing and encryption, mounted into the
container read-only. For a multi-host deployment, see "Rotation" below — the key ring must be
shared, not regenerated per replica.

---

## 1. Generate (first install)

Run on the host, in the folder that `docker/.env` points `IDENTITY_SIGNING_PFX` at (default
`./keys/idp.pfx`).

```bash
mkdir -p keys
openssl req -x509 -newkey rsa:2048 \
  -keyout keys/idp-key.pem -out keys/idp-cert.pem \
  -days 1825 -nodes \
  -subj "/CN=tuesday-idp" \
  -addext "extendedKeyUsage=serverAuth"
openssl pkcs12 -export \
  -out keys/idp.pfx \
  -inkey keys/idp-key.pem -in keys/idp-cert.pem \
  -passout pass:"$IDENTITY_SIGNING_PASSWORD"
```

**Expected result:** `keys/idp.pfx` exists and is non-empty (~2.5 KB for a 2048-bit cert).

```bash
ls -l keys/idp.pfx
openssl pkcs12 -in keys/idp.pfx -nokeys -passin pass:"$IDENTITY_SIGNING_PASSWORD" \
  | openssl x509 -noout -subject -dates
```

**Rollback line:** delete `keys/idp.pfx`; nothing else changed yet.

**Do not:** commit `keys/` to source control. `.gitignore` must cover it.

---

## 2. Wire it up

In `docker/.env`:

```ini
IDENTITY_SIGNING_PFX=./keys/idp.pfx
IDENTITY_SIGNING_PASSWORD=<the same password you exported with>
```

Then:

```bash
docker compose -f docker/compose.release.yaml --env-file docker/.env up -d --build
docker compose -f docker/compose.release.yaml --env-file docker/.env ps
```

**Expected result:** the `app` container stays `Up`, and readiness reports healthy.

```bash
curl -s http://127.0.0.1:8080/health/ready
```

**Expected result:** `{"status":"Healthy", ...}` containing a `database` check described as
`PostgreSQL answered SELECT 1.` A container that exits immediately with the
`SigningCertificatePath is required` message means the mount path and the compose path
disagree.

---

## 3. Verify tokens are signed by that certificate

```bash
curl -s -X POST http://127.0.0.1:8080/connect/token \
  -d "grant_type=client_credentials&client_id=tuesday-cli" \
  --data-urlencode "client_secret=$IDENTITY_CLIENT_SECRET" \
  -d "scope=boards"
```

**Expected result:** JSON with an `access_token`. Then request a protected endpoint with it:

```bash
curl -s -o /dev/null -w '%{http_code}\n' \
  -H "Authorization: Bearer <access_token>" http://127.0.0.1:8080/api/v1/boards
```

**Expected result:** `200`. A `400` reading `This server only accepts HTTPS requests.` is
correct behaviour on plain HTTP in Production: terminate TLS at IIS or a reverse proxy and call
`/connect/token` over HTTPS. That error is not a certificate fault.

**Restart proof.** Restart the container and replay the token request with the *old* token:

```bash
docker compose -f docker/compose.release.yaml --env-file docker/.env restart app
```

**Expected result:** the token still validates against `/api/v1/boards`. This is the whole
point of a real certificate — a Development build would fail here.

---

## 4. Rotation (annual, with overlap)

Order matters: issue the new key **beside** the old one, switch, wait out the access-token
lifetime (1 hour), then retire the old key.

1. Generate `keys/idp-next.pfx` exactly as in §1 with a new password on the same date every year.
2. Add the new certificate to the key ring for signing while keeping the old one for validation.
   With this build that means deploying the new PFX path and restarting one replica at a time;
   tokens issued by the old key stop validating when the last replica swaps, so do it outside
   working hours or accept up to one hour of re-authentication.
3. Confirm a fresh token works (§3).
4. After one hour, delete `keys/idp.pfx` and rename `idp-next.pfx` to `idp.pfx` at the next
   maintenance window.

**Rollback line:** keep the previous PFX for 24 hours. Restoring it and restarting returns the
system to the previous signing key; tokens issued in between will be rejected, and users sign
in again.

---

## Expected results, at a glance

| Step | Expected | If it differs |
|---|---|---|
| Generate | `idp.pfx` ~2.5 KB, subject `CN=tuesday-idp` | OpenSSL 3 required for `-addext`; drop it on OpenSSL 1.1 and accept no EKU |
| Start | container `Up`, `/health/ready` `Healthy` | `SigningCertificatePath is required` → mount path wrong |
| Start | no `At least one encryption key must be registered` | PFX unreadable → wrong password |
| Token | `200` from `/api/v1/boards` with a bearer token | `invalid_grant` → client secret wrong; HTTPS message → terminate TLS |
| Restart | old token still valid | Tokens rejected → Development build, not this path |

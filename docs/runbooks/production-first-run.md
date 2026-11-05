# First run in Production

Development runs the demo seeder, so it never needed this. A Production container does **not** seed
users, and until 0.9.3 it also skipped migrations — it started, passed `/health/ready`, and served
500s. This is the runbook for a real first start.

## 1. Certificate

OpenIddict will not start in Production without a signing certificate, by design: an ephemeral key
means every restart invalidates issued tokens.

```bash
openssl req -x509 -newkey rsa:2048 -nodes -days 365 \
  -keyout key.pem -out cert.pem -subj "/CN=tuesday-idp"
openssl pkcs12 -export -out idp.pfx -inkey key.pem -in cert.pem
```

Keep `idp.pfx` outside the repository, mount it read-only, and put its password in the environment
file rather than in `compose.release.yaml`.

## 2. Environment file

```ini
POSTGRES_DB=tuesday
POSTGRES_USER=tuesday
POSTGRES_PASSWORD=...
IDENTITY_CLIENT_SECRET=...
IDENTITY_SIGNING_PASSWORD=...
IDENTITY_SIGNING_PFX=/absolute/path/to/idp.pfx
TUESDAY_TAG=0.9.3
EMAIL_ENABLED=false

# First admin. Only used while the installation has no tenant.
BOOTSTRAP_ADMIN_EMAIL=ops@example.com
BOOTSTRAP_ADMIN_PASSWORD=...
```

## 3. Start

```bash
docker compose -f docker/compose.release.yaml -p tuesday-prod \
  --env-file docker/.env up -d
docker compose -p tuesday-prod logs -f app
```

Expect, in order:

1. `Applying migrations` under an advisory lock (every environment, not just Development).
2. `Bootstrapped tenant default with first admin ops@example.com.`
3. `Now listening on: http://0.0.0.0:8080`.

## 4. Verify

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8080/health/ready   # 200
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8080/login          # 200
```

Sign in with the bootstrap admin, confirm `/workspace` renders "My workspace", create a board (the
form on the empty workspace), open it, and add an item through the grid's "+ Add item" row. That write
is the real proof: cookies, antiforgery, the circuit and PostgreSQL all participated.

A board created here is scaffolded with three groups, four columns (Status, Owner, Due date, Notes)
and a Main table view, so it can accept work immediately. Before 0.9.3 a new board was blank, and the
grid had no way to add a first item — both were invisible in Development because the seeder always
provided 500 items.

Then confirm separation from the demo:

```bash
docker compose -p tuesday-prod exec postgres \
  psql -U tuesday -d tuesday -c 'select "Email" from "AspNetUsers";'
# exactly one row: the bootstrap admin. No admin@tuesday.local.
```

## 5. Steady state

Bootstrap is **one-shot by construction**: it returns immediately when any tenant exists, so leaving
the variables in place does not re-create anything — but remove them anyway, because they are a
plaintext password in a file.

If you start an empty instance without bootstrap variables, the log carries:

```
No Bootstrap:AdminEmail/AdminPassword configured. If this installation is empty it has no way to
sign in; set both and restart.
```

Set them and restart; no data is lost.

## Notes

- The OAuth client (`/connect/token`) is seeded in every environment; without it, token requests fail
  even though the API itself is healthy.
- Migrations run under `pg_advisory_lock(4281977001)`, so two replicas starting together do not race
  the schema.
- `Email__Enabled` defaults to `false`; recovery e-mail is not sent in this configuration.

using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;
using Tuesday.Infrastructure.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using Serilog;
using Tuesday.Infrastructure;
using Tuesday.Infrastructure.Identity;
using Tuesday.Infrastructure.Persistence;
using Tuesday.Infrastructure.Tenancy;
using Tuesday.Web.Components;
using Tuesday.Web.Health;
using Tuesday.Web.Http;
using Tuesday.Web.Security;

using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Fail fast at startup rather than at the first request. Missing configuration is an
// operator error, and it should surface before the process reports healthy.
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOpenApi();

// Every replica must share one key ring. The default is a file under the container's home
// directory, which dies with the container and differs per replica.
builder.Services.AddDataProtection()
    .SetApplicationName("tuesday")
    .PersistKeysToDatabase();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The SignalR backplane carries hub messages between replicas; the relay below carries the
// Blazor-circuit event, which never leaves the process it was raised in.
builder.Services.AddSignalR()
    .AddStackExchangeRedis(
        builder.Configuration["Redis:Configuration"] ?? "127.0.0.1:6379",
        options => options.Configuration.ChannelPrefix = RedisChannel.Literal("tuesday:signalr"));
builder.Services.AddSingleton<Tuesday.Web.Collaboration.BoardBroadcaster>();
builder.Services.AddSingleton<Tuesday.Application.Collaboration.IUpdateBroadcaster>(
    provider => provider.GetRequiredService<Tuesday.Web.Collaboration.BoardBroadcaster>());
builder.Services.AddSingleton<Tuesday.Web.Collaboration.AttachmentLinks>();
builder.Services.AddHostedService<Tuesday.Web.Collaboration.UpdateBroadcastRelay>();

// A fixed name matters behind a load balancer: the default name embeds a per-instance
// discriminator, so a form rendered by one replica is rejected by another even though the
// key ring is shared.
builder.Services.AddAntiforgery(options => options.Cookie.Name = "tuesday.antiforgery");

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("api", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

// SignInManager signs into IdentityConstants.ApplicationScheme, so the cookie handler
// must be registered for that scheme rather than a private one.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/denied";
        options.Cookie.Name = "tuesday.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // Re-validates the security stamp on a live cookie, which is what makes a role
        // downgrade take effect without waiting for the session to expire.
        options.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
    })
    // SignInManager and the stamp validator sign these schemes out even when 2FA is
    // unused; without the handlers, a stale cookie turns every request into a 500.
    .AddCookie(IdentityConstants.ExternalScheme)
    .AddCookie(IdentityConstants.TwoFactorUserIdScheme)
    .AddCookie(IdentityConstants.TwoFactorRememberMeScheme);

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromSeconds(30));

// ---------- OAuth 2.0 authorization server (plan P1: OpenIddict) ----------
// Integrations obtain tokens with the user's own credentials instead of a hand-issued key,
// and the same token handler validates the existing bearer API surface.
builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<TuesdayDbContext>())
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("connect/token");

        // Password grant for the CLI, refresh for long-running integrations, client
        // credentials for machine jobs.
        options.AllowPasswordFlow()
            .AllowRefreshTokenFlow()
            .AllowClientCredentialsFlow();

        options.RegisterScopes(
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.Email,
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.Profile,
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.Roles,
            Tuesday.Web.Security.IdentityServerSetup.CustomScopes.Boards);

        options.SetAccessTokenLifetime(TimeSpan.FromHours(1));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(14));

        if (builder.Environment.IsDevelopment())
        {
            // Development keys are ephemeral; production must supply a certificate so a
            // restart does not invalidate every issued token.
            options.AddDevelopmentEncryptionCertificate()
                .AddDevelopmentSigningCertificate();

            // Local development runs on plain HTTP; production keeps the HTTPS-only default.
            options.UseAspNetCore().DisableTransportSecurityRequirement();
        }
        else
        {
            // No development certificates outside Development - an operator must provide a
            // real pair from the key ring. Without this branch the server throws at first
            // request instead of at start-up, which is exactly what the container showed.
            var certificatePath = builder.Configuration["IdentityServer:SigningCertificatePath"];

            if (string.IsNullOrWhiteSpace(certificatePath))
            {
                throw new InvalidOperationException(
                    "IdentityServer:SigningCertificatePath is required outside Development. " +
                    "Supply a PFX so issued tokens survive a restart.");
            }

            var certificatePassword = builder.Configuration["IdentityServer:SigningCertificatePassword"];
            var signing = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                certificatePassword,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);

            options.AddSigningCertificate(signing)
                .AddEncryptionCertificate(signing);
        }

        options.UseAspNetCore()
            .EnableTokenEndpointPassthrough()
            .EnableAuthorizationEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddTuesdayAuthorization();
builder.Services.AddScoped<TenantContextBootstrapper>();

// Traces are exported to the console in P0 so the pipeline is visible without an
// external collector; the OTLP exporter replaces this in the deployment phase.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, tags: ["ready"], timeout: TimeSpan.FromSeconds(2))
    .AddCheck<RedisHealthCheck>(RedisHealthCheck.Name, tags: ["ready"], timeout: TimeSpan.FromSeconds(2))
    .AddCheck<StorageHealthCheck>(StorageHealthCheck.Name, tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<CorrelationIdMiddleware>();

// Routing has to be explicit here: NotFoundStatusMiddleware reads the selected endpoint to
// recognise the catch-all route, and the implicit UseRouting inserted by WebApplication runs
// too late for that.
app.UseRouting();
app.UseMiddleware<Tuesday.Web.Security.SecurityHeadersMiddleware>();
// Unknown HTML paths are served by the catch-all route on NotFound.razor, which sets a real
// 404 while still rendering a page. UseStatusCodePagesWithReExecute is deliberately not used:
// Blazor SSR answers an unmatched route with Content-Length 0, and the middleware treats an
// empty body as "already handled", so it silently produced a 404 with no page at all.

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

// Placed after every other middleware so routing has definitely selected the endpoint.
app.UseMiddleware<Tuesday.Web.Http.NotFoundStatusMiddleware>();

app.MapStaticAssets();

// Liveness answers "is the process up" and must not depend on MySQL or Redis.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteJson
});

// Readiness answers "can this instance serve traffic" and checks every dependency.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteJson
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<Tuesday.Web.Collaboration.BoardHub>("/hubs/board");

// Second factor for the sign-in page (see TotpEndpoints).
app.MapTotpChallenge();

// ---------- OAuth 2.0 token endpoint ----------

app.MapPost("/connect/token", async (
    HttpContext http,
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    CancellationToken cancellationToken) =>
{
    var request = Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(http)
        ?? throw new InvalidOperationException("The OpenIddict server request is unavailable.");

    if (!request.IsPasswordGrantType() && !request.IsClientCredentialsGrantType())
    {
        return Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.UnsupportedGrantType,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                    "Only the password and client_credentials grants are enabled."
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    ClaimsIdentity identity;

    if (request.IsClientCredentialsGrantType())
    {
        // A machine token acts as the tenant's first admin: there is no interactive user,
        // and an anonymous principal would be refused by every query filter.
        var tenantId = await FirstTenantIdAsync(http.RequestServices, cancellationToken);

        if (tenantId is null)
        {
            return Results.Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidClient,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The tenant is not provisioned, so no machine principal exists."
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        identity = new ClaimsIdentity(
            Microsoft.IdentityModel.Tokens.TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, request.ClientId)
            .SetClaim(OpenIddictConstants.Claims.Name, "Client credentials")
            .SetClaim("tenant_id", tenantId.Value.ToString("D", CultureInfo.InvariantCulture));
    }
    else
    {
        var user = request.Username is null ? null : await users.FindByNameAsync(request.Username);

        if (user is null || request.Password is null
            || !await users.CheckPasswordAsync(user, request.Password))
        {
            return Results.Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The username or password is incorrect."
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        if (await users.IsLockedOutAsync(user))
        {
            return Results.Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "This account is locked."
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        // The password grant is a second door into the same account; it must honour the
        // same second factor the sign-in page does.
        if (user.TwoFactorEnabled)
        {
            return Results.Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "This account requires a second factor; use an API token or the web sign-in."
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        identity = new ClaimsIdentity(
            Microsoft.IdentityModel.Tokens.TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, await users.GetUserIdAsync(user))
            .SetClaim(OpenIddictConstants.Claims.Name, await users.GetUserNameAsync(user) ?? string.Empty)
            .SetClaim(OpenIddictConstants.Claims.Email, await users.GetEmailAsync(user) ?? string.Empty);

        if (http.RequestServices.GetRequiredService<Tuesday.Infrastructure.Tenancy.TenantResolver>()
                is { } resolver)
        {
            var membership = await resolver.FindMembershipAsync(
                Guid.Parse(await users.GetUserIdAsync(user), CultureInfo.InvariantCulture),
                cancellationToken);

            if (membership is not null)
            {
                identity.SetClaim("tenant_id", membership.TenantId.ToString("D", CultureInfo.InvariantCulture));
                identity.SetClaim(OpenIddictConstants.Claims.Role, membership.Role.ToString());
            }
        }
    }

    identity.SetScopes(request.GetScopes());
    identity.SetDestinations(claim => claim.Type == "tenant_id"
        ? [OpenIddictConstants.Destinations.AccessToken]
        : [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken]);

    return Results.SignIn(new ClaimsPrincipal(identity), null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}).RequireRateLimiting("api");

// ---------- public API (P5) ----------

static string? BearerOf(HttpContext context)
{
    var header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? header["Bearer ".Length..].Trim()
        : null;
}

/// <summary>
/// Two kinds of bearer credential reach the API: an OpenIddict access token (validated by
/// the OpenIddict handler) and a hand-issued API token. Returning null means "no caller".
/// </summary>
static async Task<(Guid UserId, Guid TenantId, Tuesday.Domain.Tenancy.TenantRole Role)?> CallerAsync(
    HttpContext context,
    Tuesday.Infrastructure.Automations.ApiTokenService tokens,
    CancellationToken cancellationToken)
{
    var opaque = await tokens.AuthenticateAsync(BearerOf(context), cancellationToken);

    if (opaque is not null)
    {
        return opaque;
    }

    var bearer = BearerOf(context);

    if (string.IsNullOrWhiteSpace(bearer))
    {
        return null;
    }

    var result = await context.AuthenticateAsync(Tuesday.Web.Security.IdentityServerSetup.ValidationScheme);

    if (!result.Succeeded || result.Principal is null)
    {
        return null;
    }

    var subject = result.Principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
    var tenant = result.Principal.FindFirst("tenant_id")?.Value;

    if (!Guid.TryParse(subject, out var userId) || !Guid.TryParse(tenant, out var tenantId))
    {
        return null;
    }

    var role = result.Principal.FindFirst(OpenIddictConstants.Claims.Role)?.Value;

    return (userId, tenantId,
        Enum.TryParse<Tuesday.Domain.Tenancy.TenantRole>(role, out var parsed)
            ? parsed
            : Tuesday.Domain.Tenancy.TenantRole.Member);
}

// Per-token daily budget for the public API. Runs before the endpoints so a refused call
// costs nothing beyond the counter increment.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    if (!path.StartsWithSegments("/api/v1"))
    {
        await next(context);
        return;
    }

    var authorization = context.Request.Headers.Authorization.ToString();

    if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var quota = context.RequestServices.GetRequiredService<Tuesday.Infrastructure.Automations.ApiQuotaService>();
        var tokenHash = Tuesday.Domain.Automation.ApiToken.HashOf(authorization["Bearer ".Length..].Trim());
        var decision = await quota.ConsumeAsync(tokenHash, context.RequestAborted);

        context.Response.Headers["X-RateLimit-Limit"] = quota.DailyLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Remaining"] = decision.Remaining.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!decision.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(new
            {
                error = "The daily API quota for this token is exhausted.",
                retry_in_seconds = decision.RetryAfterSeconds
            });
            return;
        }
    }

    await next(context);
});

app.MapOpenApi();

app.MapGet("/api/v1/boards", async (
    HttpContext http,
    Tuesday.Infrastructure.Automations.ApiTokenService tokens,
    Tuesday.Application.Tenancy.ITenantContext tenant,
    Tuesday.Infrastructure.Queries.WorkspaceQueries boards,
    CancellationToken cancellationToken) =>
{
    var identity = await CallerAsync(http, tokens, cancellationToken);

    if (identity is null)
    {
        return Results.Unauthorized();
    }

    var (userId, tenantId, role) = identity.Value;
    ((Tuesday.Application.Tenancy.ITenantContextSetter)tenant).Resolve(tenantId, userId, role);

    var visible = await boards.GetVisibleBoardsAsync(userId, role, cancellationToken);

    return Results.Ok(new
    {
        boards = visible.Select(board => new
        {
            id = board.Id,
            name = board.Name,
            type = board.Type.ToString(),
            canEdit = board.CanEdit
        })
    });
}).RequireRateLimiting("api");

app.MapGet("/api/v1/boards/{boardId:guid}/items", async (
    Guid boardId,
    HttpContext http,
    int? take,
    string? cursor,
    Tuesday.Infrastructure.Automations.ApiTokenService tokens,
    Tuesday.Application.Tenancy.ITenantContext tenant,
    Tuesday.Infrastructure.Queries.WorkspaceQueries boards,
    Tuesday.Infrastructure.Queries.BoardQueries queries,
    CancellationToken cancellationToken) =>
{
    var identity = await CallerAsync(http, tokens, cancellationToken);

    if (identity is null)
    {
        return Results.Unauthorized();
    }

    var (userId, tenantId, role) = identity.Value;
    ((Tuesday.Application.Tenancy.ITenantContextSetter)tenant).Resolve(tenantId, userId, role);

    if (await boards.FindBoardAsync(boardId, userId, role, cancellationToken) is null)
    {
        return Results.NotFound();
    }

    long? afterSeq = null;

    if (!string.IsNullOrWhiteSpace(cursor))
    {
        if (!Tuesday.Application.Boards.PageCursor.TryDecode(cursor, out var seq))
        {
            return Results.BadRequest(new { error = "The cursor is not valid." });
        }

        afterSeq = seq;
    }

    var limit = Math.Clamp(take ?? 50, 1, 500);
    var page = await queries.GetItemPageAsync(boardId, afterSeq, limit, cancellationToken);

    return Results.Ok(new
    {
        boardId,
        items = page.Items.Select(item => new
        {
            id = item.Id,
            name = item.Name,
            groupId = item.GroupId,
            parentId = item.ParentId,
            values = item.Cells.ToDictionary(cell => cell.Key.ToString("D"), cell => cell.Value)
        }),
        nextCursor = page.HasMore && page.NextSeq is { } next ? Tuesday.Application.Boards.PageCursor.Encode(next) : null,
        hasMore = page.HasMore
    });
}).RequireRateLimiting("api");

app.MapPost("/api/v1/boards/{boardId:guid}/items", async (
    Guid boardId,
    HttpContext http,
    Tuesday.Infrastructure.Automations.ApiTokenService tokens,
    Tuesday.Application.Tenancy.ITenantContext tenant,
    Tuesday.Infrastructure.Queries.WorkspaceQueries boards,
    Tuesday.Infrastructure.Boards.ItemEditor editor,
    Tuesday.Infrastructure.Persistence.TuesdayDbContext dbContext,
    Tuesday.Web.Api.CreateItemRequest request,
    CancellationToken cancellationToken) =>
{
    var identity = await CallerAsync(http, tokens, cancellationToken);

    if (identity is null)
    {
        return Results.Unauthorized();
    }

    var (userId, tenantId, role) = identity.Value;
    ((Tuesday.Application.Tenancy.ITenantContextSetter)tenant).Resolve(tenantId, userId, role);

    if (await boards.FindBoardAsync(boardId, userId, role, cancellationToken) is null)
    {
        return Results.NotFound();
    }

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "An item needs a name." });
    }

    // Omitting the group is the common case for an agent: put the item in the first group.
    var groupId = request.GroupId ?? await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(
            dbContext.ItemGroups
                .Where(group => group.BoardId == boardId)
                .OrderBy(group => group.Position)
                .Select(group => (Guid?)group.Id),
            cancellationToken);

    if (groupId is null)
    {
        return Results.BadRequest(new { error = "The board has no groups to add an item to." });
    }

    var write = await editor.AddAsync(boardId, groupId.Value, request.Name.Trim(), null, cancellationToken);

    return write.Succeeded && write.ItemId is { } itemId
        ? Results.Created($"/api/v1/boards/{boardId:D}/items/{itemId:D}", new { id = itemId, name = request.Name.Trim(), groupId })
        : Results.BadRequest(new { error = write.Error ?? "The item was refused." });
}).RequireRateLimiting("api");

app.MapPost("/api/v1/boards/{boardId:guid}/items/{itemId:guid}/updates", async (
    Guid boardId,
    Guid itemId,
    HttpContext http,
    Tuesday.Infrastructure.Automations.ApiTokenService tokens,
    Tuesday.Application.Tenancy.ITenantContext tenant,
    Tuesday.Infrastructure.Queries.WorkspaceQueries boards,
    Tuesday.Infrastructure.Collaboration.UpdateService updates,
    Tuesday.Infrastructure.Automations.IdempotencyService idempotency,
    Tuesday.Web.Api.UpdateRequest request,
    CancellationToken cancellationToken) =>
{
    var identity = await CallerAsync(http, tokens, cancellationToken);

    if (identity is null)
    {
        return Results.Unauthorized();
    }

    var (userId, tenantId, role) = identity.Value;
    ((Tuesday.Application.Tenancy.ITenantContextSetter)tenant).Resolve(tenantId, userId, role);

    if (await boards.FindBoardAsync(boardId, userId, role, cancellationToken) is null)
    {
        return Results.NotFound();
    }

    const string endpoint = "POST /api/v1/boards/{boardId}/items/{itemId}/updates";
    var key = http.Request.Headers["Idempotency-Key"].ToString();

    if (key.Length > 200)
    {
        return Results.BadRequest(new { error = "Idempotency-Key must be 200 characters or fewer." });
    }

    var requestHash = Tuesday.Infrastructure.Automations.IdempotencyService.HashOf(
        System.Text.Json.JsonSerializer.Serialize(request));

    if (!string.IsNullOrWhiteSpace(key))
    {
        var outcome = await idempotency.BeginAsync(tenantId, userId, key, endpoint, requestHash, cancellationToken);

        if (outcome.State == Tuesday.Infrastructure.Automations.IdempotencyState.Conflict)
        {
            return Results.Conflict(new
            {
                error = "This Idempotency-Key was already used for a different request body."
            });
        }

        if (outcome.State == Tuesday.Infrastructure.Automations.IdempotencyState.Replay)
        {
            http.Response.Headers["Idempotent-Replayed"] = "true";
            return Results.Content(
                outcome.ResponseJson ?? "{}",
                "application/json",
                statusCode: outcome.StatusCode);
        }
    }

    var result = await updates.PostAsync(boardId, itemId, userId, request.Body, cancellationToken);

    if (!result.Succeeded)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    var body = System.Text.Json.JsonSerializer.Serialize(new { id = result.Id });

    if (!string.IsNullOrWhiteSpace(key))
    {
        await idempotency.CompleteAsync(
            tenantId, userId, key, endpoint, requestHash, StatusCodes.Status201Created, body, cancellationToken);
    }

    return Results.Created($"/api/v1/updates/{result.Id:D}", new { id = result.Id });
}).RequireRateLimiting("api");

app.MapPost("/api/v1/boards/{boardId:guid}/webhooks", async (
    Guid boardId,
    HttpContext http,
    Tuesday.Infrastructure.Automations.ApiTokenService tokens,
    Tuesday.Application.Tenancy.ITenantContext tenant,
    Tuesday.Infrastructure.Automations.WebhookService hooks,
    Tuesday.Web.Api.WebhookRequest request,
    CancellationToken cancellationToken) =>
{
    var identity = await CallerAsync(http, tokens, cancellationToken);

    if (identity is null)
    {
        return Results.Unauthorized();
    }

    var (userId, tenantId, role) = identity.Value;

    if (role != Tuesday.Domain.Tenancy.TenantRole.Admin)
    {
        return Results.Forbid();
    }

    ((Tuesday.Application.Tenancy.ITenantContextSetter)tenant).Resolve(tenantId, userId, role);

    var result = await hooks.RegisterAsync(tenantId, boardId, request.Url ?? string.Empty, userId, cancellationToken);

    return result.Succeeded
        ? Results.Created($"/api/v1/webhooks/{result.Id:D}", new { id = result.Id })
        : Results.BadRequest(new { error = result.Error });
}).RequireRateLimiting("api");

app.MapPost("/api/v1/boards/{boardId:guid}/webhooks/test", async (
    Guid boardId,
    HttpContext http,
    Tuesday.Infrastructure.Automations.ApiTokenService tokens,
    Tuesday.Application.Tenancy.ITenantContext tenant,
    Tuesday.Infrastructure.Automations.WebhookService hooks,
    CancellationToken cancellationToken) =>
{
    var identity = await CallerAsync(http, tokens, cancellationToken);

    if (identity is null)
    {
        return Results.Unauthorized();
    }

    var (userId, tenantId, role) = identity.Value;
    ((Tuesday.Application.Tenancy.ITenantContextSetter)tenant).Resolve(tenantId, userId, role);

    await hooks.DispatchAsync(boardId, "webhook.test", new { hello = "world" }, cancellationToken);

    return Results.Ok(new { dispatched = true });
}).RequireRateLimiting("api");

app.MapPost("/api/v1/webhooks/{webhookId:guid}/enable", async (
    Guid webhookId,
    HttpContext http,
    Tuesday.Infrastructure.Automations.ApiTokenService tokens,
    Tuesday.Application.Tenancy.ITenantContext tenant,
    Tuesday.Infrastructure.Automations.WebhookService hooks,
    CancellationToken cancellationToken) =>
{
    var identity = await CallerAsync(http, tokens, cancellationToken);

    if (identity is null)
    {
        return Results.Unauthorized();
    }

    var (userId, tenantId, role) = identity.Value;

    if (role != Tuesday.Domain.Tenancy.TenantRole.Admin)
    {
        return Results.Forbid();
    }

    ((Tuesday.Application.Tenancy.ITenantContextSetter)tenant).Resolve(tenantId, userId, role);

    return await hooks.SetEnabledAsync(webhookId, true, cancellationToken)
        ? Results.Ok(new { enabled = true })
        : Results.NotFound();
}).RequireRateLimiting("api");

app.MapGet("/attachments/{id:guid}", async (
    Guid id,
    string? t,
    Tuesday.Web.Collaboration.AttachmentLinks links,
    Tuesday.Infrastructure.Collaboration.AttachmentService files,
    Tuesday.Infrastructure.Queries.WorkspaceQueries boards,
    Tuesday.Application.Tenancy.ITenantContext tenant) =>
{
    var ticket = links.Read(t);

    if (ticket is null || ticket.AttachmentId != id)
    {
        return Results.NotFound();
    }

    if (tenant.UserId is not { } userId || tenant.Role is not { } role)
    {
        return Results.NotFound();
    }

    if (await boards.FindBoardAsync(ticket.BoardId, userId, role) is null)
    {
        return Results.NotFound();
    }

    var download = await files.OpenAsync(id, ticket.BoardId, ticket.ItemId);

    return download is null
        ? Results.NotFound()
        : Results.File(download.FullPath, download.ContentType, download.FileName, enableRangeProcessing: true);
}).RequireAuthorization();

// Sign-out is a POST so a link prefetcher cannot end a session. The antiforgery token
// is posted by the layout's form; the endpoint is authenticated, so the blast radius of
// a forged request is a forced sign-out.
app.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync().ConfigureAwait(false);
    return Results.Redirect("/");
}).RequireAuthorization();

if (app.Environment.IsDevelopment())
{
    await ApplyMigrationsAndSeedAsync(app).ConfigureAwait(false);
}

await app.RunAsync().ConfigureAwait(false);

static async Task ApplyMigrationsAndSeedAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var services = scope.ServiceProvider;

    var dbContext = services.GetRequiredService<TuesdayDbContext>();

    // Two hosts starting at once (a second replica, or parallel test hosts) would otherwise
    // both try to create the same tables and insert the same seed rows. A session-scoped
    // advisory lock serialises start-up work; the id is arbitrary but must be stable.
    const string lockSql = "SELECT pg_advisory_lock(4_281_977_001)";
    const string unlockSql = "SELECT pg_advisory_unlock(4_281_977_001)";

    await dbContext.Database.OpenConnectionAsync().ConfigureAwait(false);

    try
    {
        await dbContext.Database.ExecuteSqlRawAsync(lockSql).ConfigureAwait(false);

        await dbContext.Database.MigrateAsync().ConfigureAwait(false);

        var password = app.Configuration["Seed:AdminPassword"] ?? "Tuesday!Demo1";
        var seeder = services.GetRequiredService<DevelopmentSeeder>();
        await seeder.SeedAsync(password).ConfigureAwait(false);

        // The OAuth client lives in the same database as the users it authenticates.
        await Tuesday.Web.Security.IdentityServerSetup
            .SeedClientsAsync(app.Services, app.Configuration)
            .ConfigureAwait(false);
    }
    finally
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(unlockSql).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The connection closing releases the lock anyway.
        }

        await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
    }
}


/// <summary>
/// Client credentials are machine tokens, and every query filter needs a tenant. The
/// single-tenant on-prem deployment has exactly one, so the first row is the right answer.
/// </summary>
static async Task<Guid?> FirstTenantIdAsync(IServiceProvider services, CancellationToken cancellationToken)
{
    var factory = services.GetRequiredService<IDbContextFactory<TuesdayDbContext>>();
    await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

    return await db.Tenants.IgnoreQueryFilters().AsNoTracking()
        .OrderBy(tenant => tenant.Id)
        .Select(tenant => (Guid?)tenant.Id)
        .FirstOrDefaultAsync(cancellationToken)
        .ConfigureAwait(false);
}

/// <summary>Exposed so integration tests can host the real pipeline.</summary>
public partial class Program;

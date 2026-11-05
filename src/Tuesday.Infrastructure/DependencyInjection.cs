using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tuesday.Application.Tenancy;
using Tuesday.Infrastructure.Caching;
using Tuesday.Infrastructure.Identity;
using Tuesday.Infrastructure.Persistence;
using Tuesday.Infrastructure.Storage;
using Tuesday.Infrastructure.Tenancy;

namespace Tuesday.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default is required. Set it via environment variable or appsettings.");
        }

        var redisConfiguration = configuration["Redis:Configuration"];
        if (string.IsNullOrWhiteSpace(redisConfiguration))
        {
            throw new InvalidOperationException(
                "Redis:Configuration is required. Set it via environment variable or appsettings.");
        }

        services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));
        services.AddSingleton(new RedisConnectionProvider(redisConfiguration));

        services.AddDbContext<TuesdayDbContext>(options => ConfigureDatabase(options, connectionString));

        // Interactive circuits can run two handlers at once; a scoped context cannot
        // survive that. Write and grid-read paths take a fresh context per operation.
        services.AddDbContextFactory<TuesdayDbContext>(
            options => ConfigureDatabase(options, connectionString),
            ServiceLifetime.Scoped);

        // One resolved tenant per request; both interfaces point at the same instance so
        // the middleware can write what the query filter reads.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextSetter>(provider => provider.GetRequiredService<TenantContext>());

        services.AddScoped<TenantResolver>();
        services.AddScoped<DevelopmentSeeder>();
        services.AddScoped<Tuesday.Infrastructure.Queries.WorkspaceQueries>();
        services.AddScoped<Tuesday.Infrastructure.Queries.BoardQueries>();
        services.AddScoped<Tuesday.Infrastructure.Values.ColumnValueWriter>();
        services.AddScoped<Tuesday.Infrastructure.Boards.ItemEditor>();
        services.AddScoped<Tuesday.Infrastructure.Boards.SubitemColumnService>();
        services.AddScoped<Tuesday.Infrastructure.Automations.IdempotencyService>();
        services.AddScoped<Tuesday.Infrastructure.Automations.ApiQuotaService>();
        services.AddScoped<Tuesday.Infrastructure.Values.BoardColumnService>();
        services.AddScoped<Tuesday.Infrastructure.Views.ViewService>();
        services.AddScoped<Tuesday.Infrastructure.Views.DependencyService>();

        services.AddSingleton<Tuesday.Application.Collaboration.IUpdateBodySanitizer,
            Tuesday.Infrastructure.Collaboration.UpdateBodySanitizer>();

        services.Configure<Tuesday.Application.Collaboration.EmailOptions>(configuration.GetSection("Email"));
        services.AddScoped<Tuesday.Application.Collaboration.IEmailSender,
            Tuesday.Infrastructure.Collaboration.SmtpEmailSender>();
        services.AddScoped<Tuesday.Infrastructure.Collaboration.NotificationService>();
        services.AddScoped<Tuesday.Infrastructure.Collaboration.UpdateService>();
        services.AddScoped<Tuesday.Infrastructure.Collaboration.SearchService>();
        services.AddScoped<Tuesday.Infrastructure.Collaboration.AttachmentService>();

        services.AddHttpClient("webhooks", client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // A redirect could bounce a checked URL into the private network.
                AllowAutoRedirect = false
            });
        services.AddScoped<Tuesday.Infrastructure.Automations.AutomationService>();
        services.AddScoped<Tuesday.Infrastructure.Automations.WebhookService>();
        services.AddScoped<Tuesday.Infrastructure.Automations.ApiTokenService>();

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<TuesdayDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        return services;
    }

    /// <summary>
    /// One place that knows which EF provider is in use. Anything provider-specific belongs
    /// here or in the model builder, never spread across services.
    /// </summary>
    private static void ConfigureDatabase(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            npgsql.CommandTimeout(30);
        });
    }
}

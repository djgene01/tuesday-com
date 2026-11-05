using Serilog;
using Tuesday.Infrastructure;
using Tuesday.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// The worker shares the web host's configuration contract and fails just as loudly
// when a connection string is missing.
builder.Services.AddInfrastructure(builder.Configuration);

// AddInfrastructure registers Identity (the seeder and queries use the user store), and
// Identity's sign-in services need these two; a plain Host does not supply them, which is
// exactly how a worker-only deployment dies at start-up rather than at first use.
builder.Services.AddAuthentication();
builder.Services.AddDataProtection();

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<ScheduledAutomationWorker>();

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);

namespace Tuesday.Worker;

/// <summary>
/// P0 heartbeat only. The outbox relay, automation dispatcher and webhook delivery
/// jobs replace this in P5; keeping it now proves the host, logging and configuration
/// all work in a second process.
/// </summary>
public sealed class Worker(ILogger<Worker> logger, IHostEnvironment environment) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Tuesday worker started in {EnvironmentName} on {MachineName}",
            environment.EnvironmentName,
            System.Environment.MachineName);

        using var timer = new PeriodicTimer(HeartbeatInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                logger.LogInformation("Worker heartbeat at {Timestamp:o}", DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Tuesday worker stopping.");
        }
    }
}

using StackExchange.Redis;

namespace Tuesday.Infrastructure.Caching;

/// <summary>
/// Holds the single Redis multiplexer. Connection is lazy so a cache outage degrades
/// the app instead of preventing startup, and is shared across circuits.
/// </summary>
public sealed class RedisConnectionProvider : IAsyncDisposable
{
    private readonly Lazy<IConnectionMultiplexer> _multiplexer;

    public RedisConnectionProvider(string configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            throw new ArgumentException("A Redis configuration string is required.", nameof(configuration));
        }

        _multiplexer = new Lazy<IConnectionMultiplexer>(
            () => ConnectionMultiplexer.Connect(configuration),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IConnectionMultiplexer Multiplexer => _multiplexer.Value;

    public bool IsConnected => _multiplexer.IsValueCreated && _multiplexer.Value.IsConnected;

    public async ValueTask DisposeAsync()
    {
        if (!_multiplexer.IsValueCreated)
        {
            return;
        }

        await _multiplexer.Value.CloseAsync().ConfigureAwait(false);
        _multiplexer.Value.Dispose();
    }
}

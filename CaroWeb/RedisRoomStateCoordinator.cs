using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

public sealed class RedisRoomStateCoordinator
{
    private const string StateKey = "caro:rooms:state";
    private const string LockKey = "caro:rooms:state-lock";
    private static readonly TimeSpan LockLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(15);
    private const string ReleaseLockScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
    private readonly IDatabase _database;
    private readonly ILogger<RedisRoomStateCoordinator> _logger;

    public RedisRoomStateCoordinator(
        IConnectionMultiplexer connection,
        ILogger<RedisRoomStateCoordinator> logger)
    {
        _database = connection.GetDatabase();
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        var lockToken = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        while (!await _database.StringSetAsync(LockKey, lockToken, LockLifetime, When.NotExists))
        {
            if (DateTimeOffset.UtcNow - startedAt >= LockWait)
                throw new TimeoutException("Timed out waiting for the shared Caro room state lock.");

            await Task.Delay(50);
        }

        try
        {
            var serializedState = await _database.StringGetAsync(StateKey);
            GameHub.RestoreSharedState(serializedState.IsNullOrEmpty ? null : serializedState.ToString());
            try
            {
                return await operation();
            }
            finally
            {
                await _database.StringSetAsync(StateKey, GameHub.ExportSharedState());
            }
        }
        finally
        {
            try
            {
                await _database.ScriptEvaluateAsync(
                    ReleaseLockScript,
                    new RedisKey[] { LockKey },
                    new RedisValue[] { lockToken });
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to release the Redis room-state lock.");
                throw;
            }
        }
    }

    public async Task ExecuteAsync(Func<Task> operation) =>
        await ExecuteAsync(async () =>
        {
            await operation();
            return true;
        });
}

public sealed class RedisRoomStateFilter(RedisRoomStateCoordinator coordinator) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next) =>
        await coordinator.ExecuteAsync(() => next(invocationContext).AsTask());

    public async ValueTask OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, ValueTask> next) =>
        await coordinator.ExecuteAsync(async () => await next(context));

    public async ValueTask OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, ValueTask> next) =>
        await coordinator.ExecuteAsync(async () => await next(context, exception));
}

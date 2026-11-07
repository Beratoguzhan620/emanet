namespace Emanet.Api.Features.Bookings;

public sealed class SweeperOptions
{
    public const string Section = "Sweeper";

    /// <summary>Off in tests, which run passes by hand.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed record SweepPass(int Expired, int Lapsed, int Settled);

/// <summary>
/// Lets requests lapse when the owner does not answer, finishes rentals whose time is up (no damage reported, a
/// claim unanswered, an item never picked up), and finishes any money movement a request could not (the
/// provider was down, the process stopped halfway). Safe on several instances: each booking is settled under a lock
/// and every step is keyed.
/// </summary>
public sealed partial class BookingSweeper(IServiceScopeFactory scopes, ILogger<BookingSweeper> logger)
{
    public async Task<SweepPass> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var expired = await bookings.ExpireAsync(ct);
        var lapsed = await bookings.LapseAsync(ct);
        var settled = await bookings.SettleOutstandingAsync(ct);
        if (expired + lapsed + settled > 0)
        {
            LogPass(logger, expired, lapsed, settled);
        }

        return new SweepPass(expired, lapsed, settled);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sweep: {Expired} requests lapsed, {Lapsed} rentals finished by the clock, {Settled} bookings settled")]
    private static partial void LogPass(ILogger logger, int expired, int lapsed, int settled);
}

public sealed partial class SweeperWorker(BookingSweeper sweeper, Microsoft.Extensions.Options.IOptions<SweeperOptions> options, ILogger<SweeperWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            try
            {
                await sweeper.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Sweep failed; trying again at the next interval")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

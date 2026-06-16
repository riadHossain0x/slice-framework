using global::Hangfire;

namespace Slice.BackgroundJobs.Hangfire;

/// <summary>Maps <see cref="IBackgroundJobManager"/> onto Hangfire's client.</summary>
public sealed class HangfireBackgroundJobManager(IBackgroundJobClient client) : IBackgroundJobManager
{
    public Task<string> EnqueueAsync<TArgs>(TArgs args, TimeSpan? delay = null)
    {
        var id = delay is { } d && d > TimeSpan.Zero
            ? client.Schedule<HangfireJobDispatcher>(j => j.ExecuteAsync(args), d)
            : client.Enqueue<HangfireJobDispatcher>(j => j.ExecuteAsync(args));
        return Task.FromResult(id);
    }
}

/// <summary>
/// Maps <see cref="IRecurringJobManager"/> onto Hangfire. The interval is rounded to Hangfire's
/// cron granularity (minutely / hourly / daily).
/// </summary>
public sealed class HangfireRecurringJobManager(global::Hangfire.IRecurringJobManager recurring) : IRecurringJobManager
{
    public void AddOrUpdate<TArgs>(string jobId, TArgs args, TimeSpan interval)
    {
        var cron = interval.TotalMinutes <= 1 ? Cron.Minutely()
            : interval.TotalHours < 1 ? Cron.Hourly()
            : Cron.Daily();
        recurring.AddOrUpdate<HangfireJobDispatcher>(jobId, j => j.ExecuteAsync(args), cron);
    }
}

namespace Slice.BackgroundJobs;

/// <summary>A unit of background work parameterised by a serialisable args type.</summary>
public interface IBackgroundJob<in TArgs>
{
    Task ExecuteAsync(TArgs args, CancellationToken ct);
}

/// <summary>Enqueues fire-and-forget background work (optionally delayed).</summary>
public interface IBackgroundJobManager
{
    Task<string> EnqueueAsync<TArgs>(TArgs args, TimeSpan? delay = null);
}

/// <summary>Registers recurring background work that runs on an interval.</summary>
public interface IRecurringJobManager
{
    void AddOrUpdate<TArgs>(string jobId, TArgs args, TimeSpan interval);
}

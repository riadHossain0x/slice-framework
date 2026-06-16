using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slice.BackgroundJobs;
using Slice.BackgroundJobs.Postgres;

namespace Slice.Postgres.Tests;

public sealed record SendEmailArgs(string To);

public sealed class JobReceiver
{
    public TaskCompletionSource<string> Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class SendEmailJob(JobReceiver receiver) : IBackgroundJob<SendEmailArgs>
{
    public Task ExecuteAsync(SendEmailArgs args, CancellationToken ct)
    {
        receiver.Ran.TrySetResult(args.To);
        return Task.CompletedTask;
    }
}

[Collection("postgres")]
public sealed class PostgresBackgroundJobsTests(PostgresFixture fx)
{
    [Fact]
    public async Task Enqueued_durable_job_is_picked_up_and_executed()
    {
        var receiver = new JobReceiver();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(receiver);
        builder.Services.AddTransient<IBackgroundJob<SendEmailArgs>, SendEmailJob>();
        builder.Services.AddSlicePostgresBackgroundJobs(fx.ConnectionString);

        using var host = builder.Build();
        await host.StartAsync();   // schema + worker

        var jobs = host.Services.GetRequiredService<IBackgroundJobManager>();
        Assert.IsType<PostgresBackgroundJobManager>(jobs);
        await jobs.EnqueueAsync(new SendEmailArgs("ada@x.com"));

        var done = await Task.WhenAny(receiver.Ran.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(receiver.Ran.Task, done);
        Assert.Equal("ada@x.com", await receiver.Ran.Task);

        await host.StopAsync();
    }
}

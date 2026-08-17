using Microsoft.Extensions.DependencyInjection;
using Slice.Domain.Events;
using Slice.EventBus;

namespace Slice.EventBus.Tests;

/// <summary>
/// Guards the DI-lifetime contract of <see cref="LocalEventBus"/>. Every provider here is built with
/// <c>ValidateScopes = true</c> — the same validation ASP.NET Core turns on in Development — because
/// that is what surfaces the bug these tests exist for: the bus is a singleton, so resolving handlers
/// from its own injected provider would resolve them (and their scoped dependencies) from the ROOT
/// container. Without validation that is not an error, just a captive dependency that outlives the
/// request, which is why it went unnoticed.
/// </summary>
public class LocalEventBusTests
{
    private sealed record Pinged(string Note) : IDomainEvent;

    private sealed record Ponged : IDomainEvent;

    /// <summary>Only <see cref="ThrowingHandler"/> handles this, so the assembly scan can't leak a
    /// throwing handler into the other tests.</summary>
    private sealed record Boom : IDomainEvent;

    /// <summary>A scoped dependency, i.e. the shape of the DbContext a real handler injects.</summary>
    private sealed class ScopedThing : IDisposable
    {
        public static int Created;
        public static int Disposed;
        public readonly int Id;

        public ScopedThing() => Id = Interlocked.Increment(ref Created);

        public void Dispose() => Interlocked.Increment(ref Disposed);

        public static void Reset() { Created = 0; Disposed = 0; }
    }

    private sealed class Recorder
    {
        public readonly List<string> Calls = [];
        public readonly List<int> ScopedIds = [];
    }

    private sealed class PingedHandler(ScopedThing scoped, Recorder recorder) : IDomainEventHandler<Pinged>
    {
        public Task HandleAsync(Pinged @event, CancellationToken ct)
        {
            recorder.Calls.Add($"ping:{@event.Note}");
            recorder.ScopedIds.Add(scoped.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class SecondPingedHandler(Recorder recorder) : IDomainEventHandler<Pinged>
    {
        public Task HandleAsync(Pinged @event, CancellationToken ct)
        {
            recorder.Calls.Add($"ping2:{@event.Note}");
            return Task.CompletedTask;
        }
    }

    private sealed class PongedHandler(ScopedThing scoped, Recorder recorder) : IDomainEventHandler<Ponged>
    {
        public Task HandleAsync(Ponged @event, CancellationToken ct)
        {
            recorder.Calls.Add("pong");
            recorder.ScopedIds.Add(scoped.Id);
            return Task.CompletedTask;
        }
    }

    private static (ServiceProvider Provider, Recorder Recorder) Build()
    {
        ScopedThing.Reset();
        var recorder = new Recorder();

        var services = new ServiceCollection();
        services.AddSingleton<ILocalEventBus, LocalEventBus>();
        services.AddScoped<ScopedThing>();
        services.AddSingleton(recorder);
        services.AddDomainEventHandlers(typeof(LocalEventBusTests).Assembly);

        // ValidateScopes is the whole point — see the class comment.
        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        return (provider, recorder);
    }

    [Fact]
    public async Task Publish_invokes_a_handler_that_depends_on_a_scoped_service()
    {
        // Before the fix this threw: "Cannot resolve ... from root provider because it requires
        // scoped service 'ScopedThing'".
        var (provider, recorder) = Build();
        await using (provider)
        {
            var bus = provider.GetRequiredService<ILocalEventBus>();

            await bus.PublishAsync(new Pinged("a"));

            Assert.Contains("ping:a", recorder.Calls);
        }
    }

    [Fact]
    public async Task Publish_resolves_handlers_even_when_called_from_the_root_provider()
    {
        // The bus is a singleton, so in a real app it is very often reached from a root-resolved
        // singleton (DomainEventInterceptor). Resolving the bus itself from the root must still work.
        var (provider, recorder) = Build();
        await using (provider)
        {
            Assert.Same(provider.GetRequiredService<ILocalEventBus>(),
                        provider.GetRequiredService<ILocalEventBus>());

            await provider.GetRequiredService<ILocalEventBus>().PublishAsync(new Pinged("root"));

            Assert.Contains("ping:root", recorder.Calls);
        }
    }

    [Fact]
    public async Task Every_registered_handler_for_the_event_runs()
    {
        var (provider, recorder) = Build();
        await using (provider)
        {
            await provider.GetRequiredService<ILocalEventBus>().PublishAsync(new Pinged("b"));

            Assert.Contains("ping:b", recorder.Calls);
            Assert.Contains("ping2:b", recorder.Calls);
        }
    }

    [Fact]
    public async Task Each_publish_call_gets_its_own_scope()
    {
        var (provider, recorder) = Build();
        await using (provider)
        {
            var bus = provider.GetRequiredService<ILocalEventBus>();

            await bus.PublishAsync(new Pinged("first"));
            await bus.PublishAsync(new Pinged("second"));

            Assert.Equal(2, recorder.ScopedIds.Count);
            Assert.NotEqual(recorder.ScopedIds[0], recorder.ScopedIds[1]);
            Assert.Equal(2, ScopedThing.Created);
        }
    }

    [Fact]
    public async Task A_batch_publish_shares_one_scope_across_its_events()
    {
        var (provider, recorder) = Build();
        await using (provider)
        {
            await provider.GetRequiredService<ILocalEventBus>()
                .PublishAsync([new Pinged("batch"), new Ponged()]);

            Assert.Equal(["ping:batch", "ping2:batch", "pong"], recorder.Calls);

            // Pinged and Ponged handlers each took a ScopedThing; one scope means one instance.
            Assert.Equal(2, recorder.ScopedIds.Count);
            Assert.Equal(recorder.ScopedIds[0], recorder.ScopedIds[1]);
            Assert.Equal(1, ScopedThing.Created);
        }
    }

    [Fact]
    public async Task The_publish_scope_is_disposed_when_the_publish_completes()
    {
        var (provider, _) = Build();
        await using (provider)
        {
            await provider.GetRequiredService<ILocalEventBus>().PublishAsync(new Pinged("dispose"));

            Assert.Equal(1, ScopedThing.Created);
            Assert.Equal(1, ScopedThing.Disposed);
        }
    }

    [Fact]
    public async Task An_event_with_no_registered_handler_is_a_no_op()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILocalEventBus, LocalEventBus>();
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        await provider.GetRequiredService<ILocalEventBus>().PublishAsync(new Pinged("nobody"));
    }

    [Fact]
    public async Task Publish_rejects_a_null_event()
    {
        var (provider, _) = Build();
        await using (provider)
        {
            var bus = provider.GetRequiredService<ILocalEventBus>();

            await Assert.ThrowsAsync<ArgumentNullException>(() => bus.PublishAsync((IDomainEvent)null!));
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => bus.PublishAsync((IEnumerable<IDomainEvent>)null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => bus.PublishAsync([null!]));
        }
    }

    [Fact]
    public async Task A_handler_exception_propagates_to_the_publisher()
    {
        var (provider, _) = Build();
        await using (provider)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.GetRequiredService<ILocalEventBus>().PublishAsync(new Boom()));
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<Boom>
    {
        public Task HandleAsync(Boom @event, CancellationToken ct)
            => throw new InvalidOperationException("handler blew up");
    }
}

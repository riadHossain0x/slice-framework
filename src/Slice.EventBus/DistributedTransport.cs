using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.DependencyInjection;
using Slice.Domain.Events;

namespace Slice.EventBus;

/// <summary>Overrides the wire name of a distributed event (default = full type name).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DistributedEventNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public sealed record DistributedEventRegistration(Type EventType, string EventName);

/// <summary>Maps distributed-event CLR types ⇄ wire names so brokers can carry a stable name.</summary>
public interface IDistributedEventTypeRegistry
{
    string GetName(Type eventType);
    Type? ResolveType(string eventName);
}

public sealed class DistributedEventTypeRegistry : IDistributedEventTypeRegistry, ISingletonDependency
{
    private readonly Dictionary<Type, string> _byType = new();
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);

    public DistributedEventTypeRegistry(IEnumerable<DistributedEventRegistration> registrations)
    {
        foreach (var r in registrations)
        {
            _byType[r.EventType] = r.EventName;
            _byName[r.EventName] = r.EventType;
        }
    }

    public string GetName(Type eventType) => _byType.GetValueOrDefault(eventType) ?? eventType.FullName!;
    public Type? ResolveType(string eventName) => _byName.GetValueOrDefault(eventName);
}

/// <summary>Sends a distributed event "outward" (default = local loopback; broker adapters override).</summary>
public interface IDistributedEventPublisher
{
    Task PublishAsync(IDistributedEvent @event, string messageId, CancellationToken ct = default);
}

/// <summary>Default publisher — dispatches to local handlers immediately (single process).</summary>
public sealed class LocalDistributedEventPublisher(IDistributedEventBus bus) : IDistributedEventPublisher, IScopedDependency
{
    public Task PublishAsync(IDistributedEvent @event, string messageId, CancellationToken ct = default)
        => bus.PublishAsync(@event, ct);
}

/// <summary>Idempotency gate for incoming broker messages (default = no dedup).</summary>
public interface IInboxStore
{
    /// <summary>Returns true if this message is new (proceed); false if already processed (skip).</summary>
    Task<bool> TryMarkProcessedAsync(string messageId, CancellationToken ct = default);
}

public sealed class NullInboxStore : IInboxStore, ISingletonDependency
{
    public Task<bool> TryMarkProcessedAsync(string messageId, CancellationToken ct = default) => Task.FromResult(true);
}

/// <summary>In-memory inbox (single node / tests).</summary>
public sealed class InMemoryInboxStore : IInboxStore
{
    private readonly ConcurrentDictionary<string, byte> _seen = new();
    public Task<bool> TryMarkProcessedAsync(string messageId, CancellationToken ct = default)
        => Task.FromResult(_seen.TryAdd(messageId, 0));
}

/// <summary>Consumer side: dedup → resolve type → deserialize → dispatch to local handlers.</summary>
public interface IDistributedEventConsumer
{
    Task ConsumeAsync(string eventName, string messageId, byte[] payload, CancellationToken ct = default);
}

public sealed class DistributedEventConsumer(
    IDistributedEventTypeRegistry registry, IDistributedEventBus bus, IInboxStore inbox)
    : IDistributedEventConsumer, IScopedDependency
{
    public async Task ConsumeAsync(string eventName, string messageId, byte[] payload, CancellationToken ct = default)
    {
        if (!await inbox.TryMarkProcessedAsync(messageId, ct))
            return; // duplicate

        var type = registry.ResolveType(eventName);
        if (type is null)
            return; // unknown event — ignore

        var @event = (IDistributedEvent)JsonSerializer.Deserialize(payload, type)!;
        await bus.PublishAsync(@event, ct);
    }
}

public static class DistributedEventRegistrationExtensions
{
    /// <summary>Registers distributed-event types from an assembly into the wire-name registry.</summary>
    public static IServiceCollection AddDistributedEvents(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false } || !typeof(IDistributedEvent).IsAssignableFrom(type))
                continue;
            var attr = (DistributedEventNameAttribute?)Attribute.GetCustomAttribute(type, typeof(DistributedEventNameAttribute));
            services.AddSingleton(new DistributedEventRegistration(type, attr?.Name ?? type.FullName!));
        }
        return services;
    }
}

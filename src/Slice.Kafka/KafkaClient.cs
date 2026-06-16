using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Kafka;

/// <summary>Connection settings shared by Kafka producers and consumers.</summary>
public sealed class KafkaConnectionOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    /// <summary>Hook to tweak the underlying client config (security, acks, etc.).</summary>
    public Action<ClientConfig>? ConfigureClient { get; set; }

    internal void Apply(ClientConfig config)
    {
        config.BootstrapServers = BootstrapServers;
        ConfigureClient?.Invoke(config);
    }
}

/// <summary>Shared, lazily-created Kafka producer (thread-safe, reused across publishes).</summary>
public interface IKafkaProducerPool
{
    IProducer<string, byte[]> Get();
}

public sealed class KafkaProducerPool(KafkaConnectionOptions options) : IKafkaProducerPool, IDisposable
{
    private readonly Lazy<IProducer<string, byte[]>> _producer = new(() =>
    {
        var config = new ProducerConfig();
        options.Apply(config);
        return new ProducerBuilder<string, byte[]>(config).Build();
    });

    public IProducer<string, byte[]> Get() => _producer.Value;

    public void Dispose()
    {
        if (_producer.IsValueCreated)
        {
            _producer.Value.Flush(TimeSpan.FromSeconds(5));
            _producer.Value.Dispose();
        }
    }
}

/// <summary>Creates configured Kafka consumers for a given group.</summary>
public interface IKafkaConsumerFactory
{
    IConsumer<string, byte[]> Create(string groupId);
}

public sealed class KafkaConsumerFactory(KafkaConnectionOptions options) : IKafkaConsumerFactory
{
    public IConsumer<string, byte[]> Create(string groupId)
    {
        var config = new ConsumerConfig
        {
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };
        options.Apply(config);
        return new ConsumerBuilder<string, byte[]>(config).Build();
    }
}

public static class SliceKafkaRegistration
{
    /// <summary>Registers the Kafka connection options, producer pool and consumer factory.</summary>
    public static IServiceCollection AddSliceKafka(this IServiceCollection services, Action<KafkaConnectionOptions> configure)
    {
        var options = new KafkaConnectionOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddSingleton<IKafkaProducerPool, KafkaProducerPool>();
        services.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();
        return services;
    }
}

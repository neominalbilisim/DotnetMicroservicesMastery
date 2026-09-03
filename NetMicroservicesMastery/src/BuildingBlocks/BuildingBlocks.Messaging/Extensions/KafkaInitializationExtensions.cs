using BuildingBlocks.Messaging.Kafka;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.Extensions;

/// <summary>
/// Kafka topic'lerini initialize etmek için WebApplication extension'ları.
/// </summary>
public static class KafkaInitializationExtensions
{
    /// <summary>
    /// Verilen topic'leri Kafka'da otomatik olarak oluşturur (startup sırasında).
    /// 
    /// Kullanım (Program.cs içinde, MassTransit kurulumundan SONRA):
    ///   var app = builder.Build();
    ///   await app.InitializeKafkaTopicsAsync(
    ///       KafkaTopics.OrderCreated,
    ///       KafkaTopics.ProcessPaymentCommand,
    ///       KafkaTopics.PaymentCompleted);
    ///   await app.RunAsync();
    /// </summary>
    public static async Task InitializeKafkaTopicsAsync(
        this WebApplication app,
        params string[] topicNames)
    {
        var initializer = app.Services.GetRequiredService<KafkaTopicInitializer>();
        await initializer.InitializeAsync(topicNames);
    }

    /// <summary>
    /// Verilen topic'leri özel partition ve replication factor'ü ile oluşturur.
    /// </summary>
    public static async Task InitializeKafkaTopicsAsync(
        this WebApplication app,
        int numPartitions,
        short replicationFactor,
        params string[] topicNames)
    {
        var initializer = app.Services.GetRequiredService<KafkaTopicInitializer>();
        await initializer.InitializeAsync(topicNames, numPartitions, replicationFactor);
    }
}

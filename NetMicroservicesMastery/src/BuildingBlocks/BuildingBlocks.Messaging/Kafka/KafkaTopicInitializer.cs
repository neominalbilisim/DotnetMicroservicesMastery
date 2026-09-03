using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging.Kafka;

/// <summary>
/// Kafka topic'lerini otomatik olarak initialize eder. Uygulama startup'ında
/// çağrılarak, tanımlanan tüm topic'lerin Kafka broker'ında (henüz yoksa)
/// oluşturulmasını sağlar.
///
/// Kullanım (Program.cs içinde):
///   var app = builder.Build();
///   await app.Services.GetRequiredService<KafkaTopicInitializer>()
///       .InitializeAsync(cancellationToken: app.Lifetime.ApplicationStarted);
///   await app.RunAsync();
/// </summary>
public class KafkaTopicInitializer
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaTopicInitializer> _logger;

    public KafkaTopicInitializer(
        IConfiguration configuration,
        ILogger<KafkaTopicInitializer> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Verilen topic'leri Kafka'da oluşturur (varsa skip eder).
    /// </summary>
    public async Task InitializeAsync(
        string[] topicNames,
        int numPartitions = 3,
        short replicationFactor = 1,
        CancellationToken cancellationToken = default)
    {
        if (!topicNames.Any())
        {
            _logger.LogWarning("Initialize edilecek topic listesi boş.");
            return;
        }

        try
        {
            var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

            _logger.LogInformation("Kafka topic'leri initialize ediliyor... (Bootstrap: {BootstrapServers})", bootstrapServers);

            using var adminClient = new AdminClientBuilder(
                new AdminClientConfig
                {
                    BootstrapServers = bootstrapServers,
                    // Admin client'ın timeout ayarları (optional)
                    
                }).Build();

            // Var olan topic'leri kontrol et
            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
            var existingTopics = metadata.Topics.Select(t => t.Topic).ToHashSet();

            // Oluşturulması gereken topic'leri filtrele
            var topicsToCreate = topicNames
                .Where(t => !existingTopics.Contains(t))
                .ToList();

            if (!topicsToCreate.Any())
            {
                _logger.LogInformation("Tüm topic'ler zaten mevcut.");
                return;
            }

            // Topic specification'ları hazırla
            var topicSpecs = topicsToCreate
                .Select(t => new TopicSpecification
                {
                    Name = t,
                    NumPartitions = numPartitions,
                    ReplicationFactor = replicationFactor,
                    // Modül 3 - log retention policy (opsiyonel)
                    Configs = new Dictionary<string, string>
                    {
                        { "retention.ms", "604800000" }, // 7 gün
                        { "min.insync.replicas", "1" }
                    }
                })
                .ToList();

            // Topic'leri oluştur
            _logger.LogInformation("Oluşturulacak topic'ler: {Topics}", string.Join(", ", topicsToCreate));

            await adminClient.CreateTopicsAsync(topicSpecs, new CreateTopicsOptions { ValidateOnly = false });

            _logger.LogInformation("✓ {Count} topic başarıyla oluşturuldu.", topicsToCreate.Count);
        }
        catch (CreateTopicsException ex)
        {
            // Topic zaten varsa, hata fırlatma (beklenen durum)
            foreach (var result in ex.Results)
            {
                if (result.Error.Code == ErrorCode.TopicAlreadyExists)
                {
                    _logger.LogInformation("Topic zaten mevcut (skip): {Topic}", result.Topic);
                }
                else
                {
                    _logger.LogError("Topic oluşturma hatası ({Topic}): {Error}",
                        result.Topic, result.Error.Reason);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kafka topic initialization sırasında hata oluştu.");
            throw;
        }
    }
}

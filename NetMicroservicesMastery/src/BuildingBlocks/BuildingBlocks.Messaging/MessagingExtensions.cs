using System;
using BuildingBlocks.Messaging.Kafka;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Modül 3/4 - "Dağıtık Mesajlaşma Entegrasyonu" (Kafka Rider + opsiyonel
/// RabbitMQ). Tek bir extension noktası altında BİRDEN FAZLA transport
/// (Kafka + RabbitMQ) desteklendiği için "AddDistributedMessaging" adı
/// kullanılıyor (eski adı: AddKafkaMessaging — artık sadece Kafka'ya özel değil).
///
/// Kullanım — sadece Kafka (Choreography örnekleri, in-memory placeholder bus ile):
///   builder.Services.AddDistributedMessaging(builder.Configuration,
///       configureRider: rider => rider.AddProducer&lt;string, OrderCreatedEvent&gt;(KafkaTopics.OrderCreated));
///
/// Kullanım — Kafka + RabbitMQ birlikte (Modül 4 Saga Orchestration - Order/Payment/
/// Inventory'nin Saga.Api ile RabbitMQ üzerinden haberleştiği, Kafka Rider'ın diğer
/// tüm özellikler için ayrıca çalışmaya devam ettiği senaryo). configureRabbitMq,
/// kuyruk isimlerini ELLE (explicit) sabitlemek için kullanılır — MassTransit'in
/// varsayılan (consumer sınıf adına dayalı) otomatik isimlendirmesine GÜVENMİYORUZ,
/// çünkü Saga.Api'nin gönderdiği mesajların HANGİ kuyruğa gideceği (queue:xxx)
/// ELLE belirleniyor (bkz. SagaQueues.cs) — iki taraf da AYNI ismi kullanmalı:
///   builder.Services.AddDistributedMessaging(builder.Configuration,
///       useRabbitMq: true,
///       configureBus: x => x.AddConsumer&lt;OrderSagaCompletedConsumer&gt;(),
///       configureRabbitMq: (context, cfg) => cfg.ReceiveEndpoint(SagaQueues.OrderSagaCompleted,
///           e => e.ConfigureConsumer&lt;OrderSagaCompletedConsumer&gt;(context)),
///       configureRider: rider => rider.AddProducer&lt;string, OrderCreatedEvent&gt;(KafkaTopics.OrderCreated));
/// </summary>
public static class MessagingExtensions
{
    public static IServiceCollection AddDistributedMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureBus = null,
        Action<IRiderRegistrationConfigurator>? configureRider = null,
        Action<IRiderRegistrationContext, IKafkaFactoryConfigurator>? configureTopics = null,
        bool useRabbitMq = false,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureRabbitMq = null)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

        services.AddMassTransit(x =>
        {
            configureBus?.Invoke(x);

            if (useRabbitMq)
            {
                // Modül 4 - Saga Orchestration: bu servis, Saga.Api ile RabbitMQ
                // üzerinden Command/Event alışverişi yapıyor. Bu yüzden temel
                // bus artık sahte bir in-memory bus DEĞİL, GERÇEK bir RabbitMQ
                // bağlantısıdır — RabbitMQ, MassTransit'in Saga/State Machine
                // desteğinin NATIVE ve kanıtlanmış çalıştığı transport'tur
                // (Kafka Rider'da bu destek yoktur/güvenilir değildir).
                ConfigureRabbitMq(x, configuration, configureRabbitMq);
            }
            else
            {
                // Kafka Rider, MassTransit'in TEMEL bus altyapısının (IBus) YANINDA
                // çalışır, yerine geçmez. Sadece x.AddRider(...) tanımlayıp temel
                // bus'ı hiç yapılandırmazsanız, MassTransit'in iç mekanizması
                // (ConsumeContext/scoped filter'lar IBus'a bağımlıdır) çalışma
                // zamanında "Unable to resolve service for type 'MassTransit.IBus'"
                // hatası verir. Burada gerçek bir transport GEREKMEZ — sadece
                // IBus'ın var olması için hafif bir in-memory bus yeterlidir.
                x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
            }

            x.AddRider(rider =>
            {
                configureRider?.Invoke(rider);

                rider.UsingKafka((context, k) =>
                {
                    k.Host(bootstrapServers);
                    configureTopics?.Invoke(context, k);
                });
            });
        });

        // Kafka topic initializer'ı DI'ya register et
        services.AddSingleton<KafkaTopicInitializer>();

        return services;
    }

    /// <summary>
    /// Modül 4 - Sadece RabbitMQ kullanan (Kafka Rider'ı GEREKTİRMEYEN) saf
    /// servisler için. Saga.Api bunu kullanır — Saga State Machine, kuyruk
    /// adı EXPLICIT olarak configureRabbitMq üzerinden (SagaQueues.OrderSagaOrchestrator)
    /// bağlanır (bkz. Saga.Api/Program.cs).
    /// </summary>
    public static IServiceCollection AddRabbitMqMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator> configureBus,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureRabbitMq = null)
    {
        services.AddMassTransit(x =>
        {
            configureBus(x);
            ConfigureRabbitMq(x, configuration, configureRabbitMq);
        });

        return services;
    }

    private static void ConfigureRabbitMq(
        IBusRegistrationConfigurator x,
        IConfiguration configuration,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureRabbitMq)
    {
        var host = configuration["RabbitMq:Host"] ?? "localhost";
        var port = configuration.GetValue<ushort>("RabbitMq:Port", 5672);
        var username = configuration["RabbitMq:Username"] ?? "guest";
        var password = configuration["RabbitMq:Password"] ?? "guest";

        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(host, port, "/", h =>
            {
                h.Username(username);
                h.Password(password);
            });

            // ÖNCE explicit (elle isimlendirilmiş) endpoint'ler kaydedilir —
            // bunlar Saga.Api'nin queue:xxx adresleme şemasıyla BİREBİR
            // eşleşmelidir. SONRA ConfigureEndpoints(context) çağrılır; bu,
            // zaten explicit olarak bağlanmış consumer'ları TEKRAR (farklı bir
            // isimle) otomatik yapılandırmaz — sadece kalan (varsa) diğer
            // consumer'lar için konvansiyonel isimlendirme uygular.
            configureRabbitMq?.Invoke(context, cfg);

            cfg.ConfigureEndpoints(context);
        });
    }
}

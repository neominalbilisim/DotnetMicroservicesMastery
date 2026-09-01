using System;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Messaging.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Payment.Api.Consumers;

/// <summary>
/// Modül 3 - Payment.Api, "order-created" topic'inin BAĞIMSIZ bir
/// consumer'ıdır (Inventory.Api'den habersiz — Publish/Subscribe modelinin
/// özü budur: Order.Api kaç consumer olduğunu bilmez/umursamaz).
///
/// Retry: Polly (ResiliencePipeline) ile yapılır — MassTransit'in
/// UseMessageRetry/Fault{T} mekanizması yerine. Neden: Fault{T} mesajları
/// MassTransit'in TEMEL (in-memory) bus'ı üzerinden yayınlanır; in-memory
/// transport'un KALICILIĞI yoktur ve bu senaryoda güvenilir iletilmediği
/// gözlemlendi. Polly'yi tercih etmemizin nedeni el yazımı bir retry
/// döngüsü yerine — bu proje zaten Modül 2'de (BuildingBlocks.Resilience)
/// HTTP çağrıları için Polly kullanıyor; burada da AYNI, test edilmiş,
/// jitter/backoff destekli kütüphaneyi kullanmak, kendi retry mantığımızı
/// yeniden icat etmekten (best practice değil) daha doğrudur.
///
/// DLQ üretimi (retry'lar tükenince) ise MassTransit'in Fault{T}'i yerine
/// BURADA, doğrudan Kafka'ya produce ederek yapılır — tamamen Kafka
/// Rider'ın kendi akışı içinde kalır, cross-bus bağımlılığı yoktur.
///
/// TODO (Modül 4): Burada gerçek bir ödeme talebi oluşturma iş akışı
/// (Saga/Orchestration ile) başlatılacak.
/// </summary>
public class OrderCreatedConsumer(
    ILogger<OrderCreatedConsumer> logger,
    ITopicProducer<string, OrderCreatedDeadLetterMessage> deadLetterProducer) : IConsumer<OrderCreatedEvent>
{
    private const int MaxRetryAttempts = 3;

    private readonly ResiliencePipeline _retryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = MaxRetryAttempts,
            Delay = TimeSpan.FromSeconds(5),
            // Sabit aralık (mevcut davranışla tutarlı); jitter otomatik eklenir
            // (Polly v8 varsayılanı) — "retry storm" riskini azaltır.
            BackoffType = DelayBackoffType.Constant,
            OnRetry = args =>
            {
                logger.LogWarning(
                    "[Payment.Api] OrderCreatedEvent işlenemedi (deneme {Attempt}/{Max}): {Error}",
                    args.AttemptNumber + 1, MaxRetryAttempts, args.Outcome.Exception?.Message);
                return ValueTask.CompletedTask;
            }
        })
        .Build();

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var message = context.Message;

        try
        {
            await _retryPipeline.ExecuteAsync(
                static (msg, _) =>
                {
                    ProcessMessage(msg);
                    return ValueTask.CompletedTask;
                },
                message,
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            // Polly'nin retry stratejisi TÜM denemeleri tükettiğinde son
            // exception'ı yeniden fırlatır — burada yakalanır ve DLQ'ya yazılır.
            await SendToDeadLetterAsync(message, ex);
        }
    }

    /// <summary>Gerçek iş mantığı (henüz Modül 4'e kadar sadece simülasyon).</summary>
    private static void ProcessMessage(OrderCreatedEvent message)
    {
        // Modül 3 - DLQ TEST KANCASI: CustomerId "FAIL" olarak gönderilirse
        // bilinçli olarak hata fırlatılır (3 deneme de başarısız olacaktır).
        if (message.CustomerId == "FAIL")
        {
            throw new InvalidOperationException(
                $"[Payment.Api] Simüle edilmiş işleme hatası (test amaçlı) — OrderId={message.OrderId}");
        }

        // Konsolda gözden kaçmaması için belirgin bir işaretle de yazdırılır.
        Console.WriteLine($"🟢 [Payment.Api] EVENT ALINDI — OrderCreatedEvent: OrderId={message.OrderId}, Müşteri={message.CustomerId}, Tutar={message.TotalAmount}");

        // TODO (Modül 4): Ödeme talebi oluşturma iş mantığı burada eklenecek.
    }

    private async Task SendToDeadLetterAsync(OrderCreatedEvent message, Exception exception)
    {
        Console.WriteLine($"🔴 [Payment.Api] DEAD LETTER — OrderCreatedEvent {MaxRetryAttempts + 1} denemenin TÜMÜNDE BAŞARISIZ oldu: OrderId={message.OrderId}, Hata={exception.Message}");
        logger.LogError(
            "[Payment.Api] OrderCreatedEvent DLQ'ya yönlendirildi: OrderId={OrderId}, Hata={Reason}",
            message.OrderId, exception.Message);

        var dlqMessage = new OrderCreatedDeadLetterMessage(
            EventId: Guid.NewGuid(),
            OccurredOnUtc: DateTime.UtcNow,
            OrderId: message.OrderId,
            CustomerId: message.CustomerId,
            TotalAmount: message.TotalAmount,
            FailureReason: exception.Message,
            ConsumerName: "Payment.Api");

        await deadLetterProducer.Produce(dlqMessage.PartitionKey, dlqMessage);
    }
}

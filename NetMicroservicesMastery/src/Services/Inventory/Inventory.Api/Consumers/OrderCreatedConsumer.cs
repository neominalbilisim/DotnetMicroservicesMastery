using System;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Messaging.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Inventory.Api.Consumers;

/// <summary>
/// Modül 3 - Inventory.Api de aynı "order-created" event'inin BAĞIMSIZ bir
/// consumer'ıdır — Payment.Api'nin varlığından habersizdir. Bu, Event
/// (Publish/Subscribe) modelinin somut kanıtıdır: TEK bir event, BİRDEN
/// FAZLA servis tarafından, birbirinden habersiz şekilde tüketilebilir.
///
/// Retry: Polly (ResiliencePipeline) ile yapılır — bkz. Payment.Api'deki
/// aynı desen ve gerekçe (MassTransit'in UseMessageRetry/Fault{T}
/// mekanizması yerine, bu projede zaten Modül 2'den beri kullanılan Polly
/// tercih edildi).
///
/// TODO (Modül 4): Burada gerçek bir stok rezervasyonu iş akışı
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
            BackoffType = DelayBackoffType.Constant,
            OnRetry = args =>
            {
                logger.LogWarning(
                    "[Inventory.Api] OrderCreatedEvent işlenemedi (deneme {Attempt}/{Max}): {Error}",
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
                $"[Inventory.Api] Simüle edilmiş işleme hatası (test amaçlı) — OrderId={message.OrderId}");
        }

        // Konsolda gözden kaçmaması için belirgin bir işaretle de yazdırılır.
        Console.WriteLine($"🟣 [Inventory.Api] EVENT ALINDI — OrderCreatedEvent: OrderId={message.OrderId}, Tutar={message.TotalAmount} — stok rezervasyonu simüle ediliyor.");

        // TODO (Modül 4): Stok rezervasyonu iş mantığı burada eklenecek.
    }

    private async Task SendToDeadLetterAsync(OrderCreatedEvent message, Exception exception)
    {
        Console.WriteLine($"🔴 [Inventory.Api] DEAD LETTER — OrderCreatedEvent {MaxRetryAttempts + 1} denemenin TÜMÜNDE BAŞARISIZ oldu: OrderId={message.OrderId}, Hata={exception.Message}");
        logger.LogError(
            "[Inventory.Api] OrderCreatedEvent DLQ'ya yönlendirildi: OrderId={OrderId}, Hata={Reason}",
            message.OrderId, exception.Message);

        var dlqMessage = new OrderCreatedDeadLetterMessage(
            EventId: Guid.NewGuid(),
            OccurredOnUtc: DateTime.UtcNow,
            OrderId: message.OrderId,
            CustomerId: message.CustomerId,
            TotalAmount: message.TotalAmount,
            FailureReason: exception.Message,
            ConsumerName: "Inventory.Api");

        await deadLetterProducer.Produce(dlqMessage.PartitionKey, dlqMessage);
    }
}

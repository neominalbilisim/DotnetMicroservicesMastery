using System;
using System.Threading.Tasks;
using BuildingBlocks.Messaging.Contracts.Commands;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Payment.Api.Consumers;

/// <summary>
/// Modül 3 - "Kurumsal Mesajlaşma Konseptleri: Command ve Event Ayrımı".
/// Bu, bir COMMAND consumer'ıdır — OrderCreatedConsumer (Event consumer)
/// ile KARIŞTIRILMAMALIDIR. Fark:
///
///   - OrderCreatedEvent ("order-created" topic): Payment.Api VE Inventory.Api
///     BAĞIMSIZ olarak dinler (Publish/Subscribe — Order.Api kaç dinleyici
///     olduğunu bilmez).
///   - ProcessPaymentCommand ("process-payment-command" topic): SADECE
///     Payment.Api dinler — Order.Api, bu mesajı özellikle Payment.Api'nin
///     işlemesini bekleyerek "gönderir" (Send semantiği; Inventory.Api bu
///     topic'i hiç dinlemez).
///
/// TODO (Modül 4): Burada gerçek bir ödeme işleme akışı (CQRS Command
/// Handler + Saga/Orchestration ile) başlatılacak.
/// </summary>
public class ProcessPaymentCommandConsumer(ILogger<ProcessPaymentCommandConsumer> logger) : IConsumer<ProcessPaymentCommand>
{
    public Task Consume(ConsumeContext<ProcessPaymentCommand> context)
    {
        var command = context.Message;

        Console.WriteLine($"🟡 [Payment.Api] COMMAND ALINDI — ProcessPaymentCommand: OrderId={command.OrderId}, Tutar={command.Amount} — ödeme işleme simüle ediliyor.");

        logger.LogInformation(
            "[Payment.Api] ProcessPaymentCommand alındı: OrderId={OrderId}, Tutar={Amount}",
            command.OrderId, command.Amount);

        // TODO (Modül 4): Gerçek ödeme işleme mantığı burada eklenecek.

        return Task.CompletedTask;
    }
}

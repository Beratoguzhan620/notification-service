using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core.Outbox;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace NotificationService.Infrastructure.Delivery;

/// <summary>
/// Consumes the delivery queue with manual acknowledgements. Every message ends in exactly one of:
/// ack (done or duplicate), a copy in a retry queue and then ack, or a reject that sends it to the dead queue.
/// A message is never acknowledged before its outcome is stored.
/// </summary>
public sealed partial class DeliveryConsumer(
    RabbitMqConnection connection,
    Publisher publisher,
    IServiceScopeFactory scopes,
    IOptions<DeliveryOptions> options,
    ILogger<DeliveryConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan PauseBeforeRequeue = TimeSpan.FromSeconds(1);

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var open = await connection.GetAsync(stoppingToken);
        _channel = await open.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: options.Value.Concurrency),
            stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: options.Value.Prefetch, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(_channel, delivery, stoppingToken);
        await _channel.BasicConsumeAsync(Queues.Deliver, autoAck: false, consumer, stoppingToken);
        LogStarted(logger, options.Value.Concurrency);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down: unacknowledged messages go back to the queue when the channel closes.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // A host may be stopped more than once; only the first stop closes the channel.
        var channel = Interlocked.Exchange(ref _channel, null);
        if (channel is null)
        {
            return;
        }

        try
        {
            await channel.CloseAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or AlreadyClosedException)
        {
            // The connection went first; the broker requeues whatever was not acknowledged.
        }

        await channel.DisposeAsync();
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        // The body buffer belongs to the client library and is reused after this handler returns.
        var body = delivery.Body.ToArray();
        var properties = delivery.BasicProperties;

        using var activity = Tracing.StartLinked($"process {Queues.Deliver}", ActivityKind.Consumer,
            MessageHeaders.ReadString(properties.Headers, MessageHeaders.TraceParent));
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", Queues.Deliver);
        activity?.SetTag("messaging.message.id", properties.MessageId);
        activity?.SetTag("messaging.rabbitmq.message.attempt", MessageHeaders.ReadInt(properties.Headers, MessageHeaders.Attempt));

        if (!Guid.TryParse(properties.MessageId, out var messageId) || Read(body) is not { } message)
        {
            LogUnreadable(logger, properties.MessageId);
            activity?.SetStatus(ActivityStatusCode.Error, "unreadable message");
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
            return;
        }

        Outcome outcome;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<NotificationProcessor>();
            outcome = await processor.ProcessAsync(messageId, message.NotificationId, stoppingToken);
        }
        catch (Exception ex)
        {
            // The database is unreachable or the worker is stopping. Put the message back; nothing was recorded.
            if (!stoppingToken.IsCancellationRequested)
            {
                LogProcessingFailed(logger, ex, messageId);
                await Task.Delay(PauseBeforeRequeue, CancellationToken.None);
            }

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
            return;
        }

        activity?.SetTag("notification.id", message.NotificationId.ToString());
        activity?.SetTag("notification.outcome", outcome.Kind.ToString());
        if (outcome.Kind is OutcomeKind.Retry or OutcomeKind.Dead)
        {
            activity?.SetStatus(ActivityStatusCode.Error, outcome.Reason);
        }

        switch (outcome.Kind)
        {
            case OutcomeKind.Done or OutcomeKind.Duplicate:
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
                break;

            case OutcomeKind.Retry or OutcomeKind.Busy:
                await RetryLaterAsync(channel, delivery, body, outcome);
                break;

            default:
                // The delivery queue dead-letters rejected messages into the dead queue.
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
                break;
        }
    }

    /// <summary>A copy goes to the retry queue for the delay; only once RabbitMQ confirmed it is the original acknowledged.</summary>
    private async Task RetryLaterAsync(IChannel channel, BasicDeliverEventArgs delivery, byte[] body, Outcome outcome)
    {
        var original = delivery.BasicProperties;
        var headers = original.Headers is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(original.Headers);
        headers[MessageHeaders.Attempt] = MessageHeaders.ReadInt(original.Headers, MessageHeaders.Attempt) + 1;
        if (outcome.Reason is not null)
        {
            headers[MessageHeaders.Reason] = outcome.Reason.Length > 200 ? outcome.Reason[..200] : outcome.Reason;
        }

        var copy = new BasicProperties
        {
            MessageId = original.MessageId,
            Type = original.Type,
            ContentType = original.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = original.Timestamp,
            Headers = headers,
        };

        try
        {
            await publisher.PublishAsync(Queues.Retry(outcome.RetryAfter ?? options.Value.EffectiveRetryDelays[0]), copy, body, CancellationToken.None);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogRetryPublishFailed(logger, ex, original.MessageId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
    }

    private static DeliverNotification? Read(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize<DeliverNotification>(body, JsonSerializerOptions.Web) is { NotificationId: var id } message && id != Guid.Empty
                ? message
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming the delivery queue, {Concurrency} at a time")]
    private static partial void LogStarted(ILogger logger, int concurrency);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {MessageId} cannot be read; it goes to the dead queue")]
    private static partial void LogUnreadable(ILogger logger, string? messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {MessageId} could not be processed; it goes back to the queue")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception, Guid messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {MessageId} could not be moved to a retry queue; it goes back to the queue")]
    private static partial void LogRetryPublishFailed(ILogger logger, Exception exception, string? messageId);
}

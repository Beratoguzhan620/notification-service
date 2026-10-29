using System.Collections.Concurrent;
using System.Diagnostics;
using NotificationService.Infrastructure.Telemetry;
using NotificationService.IntegrationTests.Support;

namespace NotificationService.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TracingTests(ApiFactory factory) : IDisposable
{
    private readonly ConcurrentBag<Activity> _finished = [];
    private readonly ActivityListener _listener = new();

    [Fact]
    public async Task One_trace_follows_a_notification_from_the_request_to_the_mailbox()
    {
        _listener.ShouldListenTo = source => source.Name is Tracing.SourceName or "Microsoft.AspNetCore";
        _listener.Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded;
        _listener.ActivityStopped = activity => _finished.Add(activity);
        ActivitySource.AddActivityListener(_listener);

        var traceId = ActivityTraceId.CreateRandom();
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");

        var accepted = await client.SendOkAsync(Notifications.OrderShipped(email: Mailpit.NewAddress()));
        client.DefaultRequestHeaders.Remove("traceparent");
        await client.WaitForStatusAsync(accepted.Id, "Delivered");
        var spans = await Waiting.UntilAsync(
            () => Task.FromResult(_finished.Where(a => a.TraceId == traceId).ToList()),
            found => found.Any(a => a.DisplayName == "deliver Email") && found.Any(a => a.DisplayName.StartsWith("process", StringComparison.Ordinal)));

        var publish = Assert.Single(spans, a => a.DisplayName == "publish notifications.deliver");
        var process = Assert.Single(spans, a => a.DisplayName == "process notifications.deliver");
        var deliver = Assert.Single(spans, a => a.DisplayName == "deliver Email");
        Assert.Equal(ActivityKind.Producer, publish.Kind);
        Assert.Equal(publish.SpanId, process.ParentSpanId);
        Assert.Equal(process.SpanId, deliver.ParentSpanId);
        Assert.Equal(accepted.Id.ToString(), process.GetTagItem("notification.id"));
        Assert.Equal("Done", process.GetTagItem("notification.outcome"));
    }

    public void Dispose() => _listener.Dispose();
}

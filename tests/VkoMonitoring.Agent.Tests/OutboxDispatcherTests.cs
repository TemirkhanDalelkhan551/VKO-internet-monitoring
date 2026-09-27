using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Agent.Core.Services;

namespace VkoMonitoring.Agent.Tests;

public sealed class OutboxDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_RemovesMeasurementOnlyAfterSuccessfulSend()
    {
        var measurement = CreateMeasurement();
        var outbox = new InMemoryOutbox(measurement);
        var dispatcher = new OutboxDispatcher(outbox, new SuccessfulApiClient());

        var result = await dispatcher.DispatchAsync(CancellationToken.None);

        Assert.Equal(1, result.SentCount);
        Assert.Empty(result.QuarantinedMeasurements);
        Assert.Empty(await outbox.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DispatchAsync_KeepsMeasurementWhenSendFails()
    {
        var measurement = CreateMeasurement();
        var outbox = new InMemoryOutbox(measurement);
        var dispatcher = new OutboxDispatcher(outbox, new FailingApiClient());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            dispatcher.DispatchAsync(CancellationToken.None));

        Assert.Single(await outbox.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DispatchAsync_QuarantinesPermanentRejectionAndContinuesWithNextMeasurement()
    {
        var rejected = CreateMeasurement();
        var delivered = CreateMeasurement();
        var outbox = new InMemoryOutbox(rejected, delivered);
        var dispatcher = new OutboxDispatcher(
            outbox,
            new QueueApiClient(
                MeasurementDeliveryResult.PermanentlyRejected(400),
                MeasurementDeliveryResult.Delivered));

        var result = await dispatcher.DispatchAsync(CancellationToken.None);

        Assert.Equal(1, result.SentCount);
        var quarantined = Assert.Single(result.QuarantinedMeasurements);
        Assert.Equal(rejected.EventId, quarantined.EventId);
        Assert.Equal(400, quarantined.ResponseStatusCode);
        Assert.Equal([rejected.EventId], outbox.QuarantinedEventIds);
        Assert.Empty(await outbox.GetPendingAsync(CancellationToken.None));
    }

    private static InternetMeasurement CreateMeasurement() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
        100, 50, 20, 5, 0, ConnectionStatus.Online, null, "1.0.0");

    private sealed class InMemoryOutbox(params InternetMeasurement[] measurements) : IMeasurementOutbox
    {
        private readonly List<InternetMeasurement> _items = [.. measurements];
        public List<Guid> QuarantinedEventIds { get; } = [];

        public Task EnqueueAsync(InternetMeasurement measurement, CancellationToken cancellationToken)
        {
            _items.Add(measurement);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InternetMeasurement>> GetPendingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InternetMeasurement>>(_items.ToArray());

        public Task MarkAsSentAsync(Guid eventId, CancellationToken cancellationToken)
        {
            _items.RemoveAll(item => item.EventId == eventId);
            return Task.CompletedTask;
        }

        public Task QuarantineAsync(Guid eventId, CancellationToken cancellationToken)
        {
            _items.RemoveAll(item => item.EventId == eventId);
            QuarantinedEventIds.Add(eventId);
            return Task.CompletedTask;
        }
    }

    private sealed class SuccessfulApiClient : IMeasurementApiClient
    {
        public Task<MeasurementDeliveryResult> SendAsync(
            InternetMeasurement measurement,
            CancellationToken cancellationToken) =>
            Task.FromResult(MeasurementDeliveryResult.Delivered);
    }

    private sealed class FailingApiClient : IMeasurementApiClient
    {
        public Task<MeasurementDeliveryResult> SendAsync(
            InternetMeasurement measurement,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("API unavailable");
    }

    private sealed class QueueApiClient(params MeasurementDeliveryResult[] results) : IMeasurementApiClient
    {
        private readonly Queue<MeasurementDeliveryResult> _results = new(results);

        public Task<MeasurementDeliveryResult> SendAsync(
            InternetMeasurement measurement,
            CancellationToken cancellationToken) =>
            Task.FromResult(_results.Dequeue());
    }
}

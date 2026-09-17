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

        var sentCount = await dispatcher.DispatchAsync(CancellationToken.None);

        Assert.Equal(1, sentCount);
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

    private static InternetMeasurement CreateMeasurement() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
        100, 50, 20, 5, 0, ConnectionStatus.Online, null, "1.0.0");

    private sealed class InMemoryOutbox(params InternetMeasurement[] measurements) : IMeasurementOutbox
    {
        private readonly List<InternetMeasurement> _items = [.. measurements];

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
    }

    private sealed class SuccessfulApiClient : IMeasurementApiClient
    {
        public Task SendAsync(InternetMeasurement measurement, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FailingApiClient : IMeasurementApiClient
    {
        public Task SendAsync(InternetMeasurement measurement, CancellationToken cancellationToken) =>
            throw new HttpRequestException("API unavailable");
    }
}

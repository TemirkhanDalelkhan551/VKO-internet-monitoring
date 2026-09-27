namespace VkoMonitoring.Api.Services;

public sealed class SpeedTestConcurrencyGate(int maximumConcurrency)
{
    private readonly SemaphoreSlim _semaphore = new(
        maximumConcurrency > 0
            ? maximumConcurrency
            : throw new ArgumentOutOfRangeException(nameof(maximumConcurrency)));

    public bool TryAcquire(out IDisposable? lease)
    {
        if (!_semaphore.Wait(0))
        {
            lease = null;
            return false;
        }

        lease = new Lease(_semaphore);
        return true;
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}

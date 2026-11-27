namespace MaxCube.Infrastructure.Connectivity;

/// <summary>
/// Prozessweite Sperre für den festen lokalen UDP-Port 23272. Discovery, Netzwerkkonfiguration
/// und Reboot binden alle denselben Port (Hardwarebeleg 25.11.2026: der Cube antwortet nur an
/// den Absenderport 23272). Ohne diese Sperre erzeugt ein paralleler Aufruf sofort eine
/// <c>SocketException</c> mit <c>AddressAlreadyInUse</c>, z. B. beim Klick auf „Netzwerk lesen“,
/// während der automatische Cube-Reiter-Read noch liest.
/// </summary>
internal static class UdpPortGate
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    public static async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Semaphore.Release();
            }
        }
    }
}

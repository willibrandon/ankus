using System.Net.Sockets;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Reserves available fixed server ports for tests that restart a listener at the same address.
/// </summary>
internal static class TestPortReservations
{
    /// <summary>
    /// Reserves a port for a test that deliberately stops and restarts a listener at the same address.
    /// </summary>
    /// <returns>The reservation to release at the server handoff.</returns>
    internal static PortReservation Create()
        => Create(static port => PortReservation.Create(port));

    /// <summary>
    /// Selects a usable candidate while preserving occupied or operating-system-reserved ports.
    /// </summary>
    /// <param name="reserve">The operation that binds a candidate and retains its listener.</param>
    /// <returns>A successfully bound reservation.</returns>
    internal static PortReservation Create(Func<int, PortReservation> reserve)
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            try
            {
                return reserve(Random.Shared.Next(20000, 28000));
            }
            catch (SocketException error) when (error.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied && attempt < 31)
            {
                // Windows exclusions and exclusive listeners can reject a candidate with AccessDenied.
                // This is candidate discovery; explicit PostgreSQL port requests still fail unchanged.
            }
        }

        throw new InvalidOperationException("Could not reserve a fixed PostgreSQL test port.");
    }
}

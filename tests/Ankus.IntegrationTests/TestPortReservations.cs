using System.Net.Sockets;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Reserves fixed server ports outside the platforms' default ephemeral client-port ranges.
/// </summary>
internal static class TestPortReservations
{
    /// <summary>
    /// Reserves a port for a test that deliberately stops and restarts a listener at the same address.
    /// </summary>
    /// <returns>The reservation to release at the server handoff.</returns>
    internal static PortReservation Create()
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            try
            {
                return PortReservation.Create(Random.Shared.Next(20000, 28000));
            }
            catch (SocketException error) when (error.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < 31)
            {
                // Select an unused test port before startup, preserving any existing listener.
            }
        }

        throw new InvalidOperationException("Could not reserve a fixed PostgreSQL test port.");
    }
}

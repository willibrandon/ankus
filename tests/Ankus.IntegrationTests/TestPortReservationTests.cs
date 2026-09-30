using System.Net;
using System.Net.Sockets;
using Ankus.Testing;

namespace Ankus.IntegrationTests;

/// <summary>
/// Verifies candidate allocation under operating-system port exclusions and occupied listeners.
/// </summary>
/// <param name="context">The per-test cancellation context.</param>
[TestClass]
public sealed class TestPortReservationTests(TestContext context)
{
    /// <summary>
    /// Unavailable candidates lead to a real retained listener that accepts a TCP connection.
    /// </summary>
    /// <param name="failure">The binding error for an unusable candidate.</param>
    [TestMethod]
    [DataRow(SocketError.AccessDenied)]
    [DataRow(SocketError.AddressAlreadyInUse)]
    public async Task UnavailableCandidateSelectsAndOwnsUsablePort(SocketError failure)
    {
        int attempts = 0;
        using PortReservation reservation = TestPortReservations.Create(port =>
        {
            Assert.IsInRange(20000, 27999, port);
            attempts++;
            if (attempts == 1)
            {
                throw new SocketException((int)failure);
            }

            return PortReservation.Create();
        });
        Assert.AreEqual(2, attempts);
        TcpListener listener = reservation.TakeListener();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, reservation.Port, context.CancellationToken);
            using TcpClient accepted = await listener.AcceptTcpClientAsync(context.CancellationToken);
            Assert.IsTrue(accepted.Connected);
            Assert.AreEqual(reservation.Port, Assert.IsInstanceOfType<IPEndPoint>(listener.LocalEndpoint).Port);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Persistent unavailability remains bounded and preserves the final operating-system error.
    /// </summary>
    /// <param name="failure">The binding error returned for every candidate.</param>
    [TestMethod]
    [DataRow(SocketError.AccessDenied)]
    [DataRow(SocketError.AddressAlreadyInUse)]
    public void UnavailableCandidatesStopAndPreserveFinalError(SocketError failure)
    {
        int attempts = 0;
        var final = new SocketException((int)failure);
        SocketException error = Assert.ThrowsExactly<SocketException>(() =>
        {
            using PortReservation reservation = TestPortReservations.Create(_ =>
            {
                attempts++;
                throw final;
            });
        });
        Assert.AreEqual(32, attempts);
        Assert.AreSame(final, error);
    }

    /// <summary>
    /// Errors unrelated to an unavailable candidate are immediately propagated.
    /// </summary>
    [TestMethod]
    public void NetworkFailureDoesNotRetry()
    {
        int attempts = 0;
        var failure = new SocketException((int)SocketError.NetworkDown);
        SocketException error = Assert.ThrowsExactly<SocketException>(() =>
        {
            using PortReservation reservation = TestPortReservations.Create(_ =>
            {
                attempts++;
                throw failure;
            });
        });
        Assert.AreEqual(1, attempts);
        Assert.AreSame(failure, error);
    }
}

using System.IO.Pipes;

namespace Ankus.IntegrationTests;

/// <summary>
/// Exercises real test-controller sockets with short, deep and multibyte temporary paths.
/// </summary>
/// <param name="context">The cooperative test cancellation context.</param>
[TestClass]
public sealed class PackagePipeDirectoryTests(TestContext context)
{
    /// <summary>
    /// Exchanges bytes through the selected directory while retaining the caller's build directory.
    /// </summary>
    /// <param name="layout">The temporary-directory layout.</param>
    [TestMethod]
    [OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
    [DataRow("short")]
    [DataRow("deep")]
    [DataRow("unicode")]
    public async Task SocketDirectorySupportsLongTemporaryPaths(string layout)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string root = IntegrationEnvironment.PhysicalDirectory(Directory.CreateDirectory(
            Path.Combine(profile, ".apt-" + Guid.NewGuid().ToString("N")[..12])));
        string temporary = layout == "short" ? root : Directory.CreateDirectory(Path.Combine(root,
            layout == "deep" ? new string('a', 80) : new string('é', 24))).FullName;
        string? selected = null;
        try
        {
            selected = PackagePipeDirectory.Create(temporary, profile);
            Assert.AreEqual(layout == "short", selected == temporary);
            string pipe = Path.Combine(selected, Guid.NewGuid().ToString("N"));
            using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(server.WaitForConnectionAsync(context.CancellationToken),
                client.ConnectAsync(context.CancellationToken));
            await client.WriteAsync(new byte[] { 42 }, context.CancellationToken);
            byte[] received = new byte[1];
            await server.ReadExactlyAsync(received, context.CancellationToken);
            Assert.AreEqual((byte)42, received[0]);
            Assert.IsTrue(Directory.Exists(temporary));
        }
        finally
        {
            if (selected is not null && selected != temporary)
            {
                Directory.Delete(selected, recursive: true);
            }

            Directory.Delete(root, recursive: true);
        }
    }
}

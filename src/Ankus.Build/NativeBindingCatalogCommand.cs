using System.Diagnostics;
using System.Text.Json;

namespace Ankus.Build;

/// <summary>
/// Regenerates native declaration catalogs from the pinned read-only pgrx reference.
/// </summary>
internal static class NativeBindingCatalogCommand
{
    /// <summary>
    /// Writes catalogs to the supplied directory, or checks their exact existing contents.
    /// </summary>
    /// <param name="arguments">The pgrx checkout, output directory, and optional --check flag.</param>
    internal static async Task RunAsync(string[] arguments)
    {
        if (arguments.Length is < 2 or > 3 || arguments.Length == 3 && arguments[2] != "--check")
        {
            throw new ArgumentException("Expected binding-catalogs <pgrx-checkout> <output-directory> [--check].", nameof(arguments));
        }

        const string Revision = "70383e884582d1bcc7cd681d10886b995a2830cb";
        var options = new JsonSerializerOptions { WriteIndented = true };
        for (int major = 13; major <= 19; major++)
        {
            string source = await ReadSourceAsync(arguments[0], Revision, major);
            NativeBindingCatalog catalog = NativeBindingParser.Parse(source, major);
            string path = Path.Combine(arguments[1], $"pg{major}.json");
            string content = JsonSerializer.Serialize(catalog, options) + "\n";
            if (!File.Exists(path) || File.ReadAllText(path) != content)
            {
                if (arguments.Length == 3)
                {
                    throw new InvalidOperationException($"Native binding catalog is stale: {path}");
                }

                Directory.CreateDirectory(arguments[1]);
                await File.WriteAllTextAsync(path, content);
            }

            NativeBindingType[] nodes = [.. catalog.Types.Values.Where(static type => type.IsNode)];
            Console.WriteLine($"PG{major}: {catalog.Tags.Count} tags, {nodes.Length} nodes, {nodes.Sum(static type => type.Fields.Count)} node fields.");
        }
    }

    /// <summary>
    /// Reads committed bindings without fetching or modifying the reference checkout.
    /// </summary>
    /// <param name="checkout">The existing pgrx checkout.</param>
    /// <param name="revision">The exact committed source revision.</param>
    /// <param name="major">The PostgreSQL major whose declarations are required.</param>
    /// <returns>The committed Rust bindings.</returns>
    private static async Task<string> ReadSourceAsync(string checkout, string revision, int major)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { "-C", Path.GetFullPath(checkout), "show", $"{revision}:pgrx-pg-sys/src/include/pg{major}.rs" })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start git.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Cannot read PostgreSQL {major}: {(await errors).Trim()}");
        }

        return await output;
    }
}

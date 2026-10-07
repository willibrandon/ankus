#:property TargetFramework=net10.0

using System.Diagnostics;
using System.Globalization;

if (args.Length is < 2 or > 3 || args.Length == 3 && args[2] != "--check" || !File.Exists("Ankus.slnx") ||
    !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) || major is < 13 or > 19)
{
    Console.Error.WriteLine(
        "Run from the repository root: dotnet run --file eng/Ankus.Headers.cs -- <major 13-19> <pg_config> [--check]");
    return 1;
}

var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
foreach (string argument in new[]
{
    "run", "--project", "src/Ankus.Build", "-c", "Release", "--",
    "binding-header-manifest", major.ToString(CultureInfo.InvariantCulture), args[1], $"src/Ankus.Build/Bindings/pg{major}.h",
})
{
    start.ArgumentList.Add(argument);
}

if (args.Length == 3)
{
    start.ArgumentList.Add("--check");
}

using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the Ankus build tool.");
await process.WaitForExitAsync();
return process.ExitCode;

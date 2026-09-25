#:property TargetFramework=net10.0

using System.Diagnostics;

if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--check" || !File.Exists("Ankus.slnx"))
{
    Console.Error.WriteLine("Run from the repository root: dotnet run --file eng/Ankus.Bindings.cs -- <pgrx-checkout> [--check]");
    return 1;
}

var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
foreach (string argument in new[]
{
    "run", "--project", "src/Ankus.Build", "-c", "Release", "--",
    "binding-catalogs", Path.GetFullPath(args[0]), "src/Ankus.Build/Bindings",
})
{
    start.ArgumentList.Add(argument);
}

if (args.Length == 2)
{
    start.ArgumentList.Add("--check");
}

using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the Ankus build tool.");
await process.WaitForExitAsync();
return process.ExitCode;

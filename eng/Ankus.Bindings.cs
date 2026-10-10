#:property TargetFramework=net10.0

using System.Diagnostics;

bool headers = args.Length == 3 && args[0] == "--headers";
if (!headers && (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--check") || !File.Exists("Ankus.slnx"))
{
    Console.Error.WriteLine("Run from the repository root: dotnet run --file eng/Ankus.Bindings.cs -- <pgrx-checkout> [--check]");
    Console.Error.WriteLine("Or compare header-derived types: dotnet run --file eng/Ankus.Bindings.cs -- --headers <pg_config> <output>");
    return 1;
}

var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
string[] command = headers
    ? ["binding-catalog-headers", Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), "--compare"]
    : ["binding-catalogs", Path.GetFullPath(args[0]), "src/Ankus.Build/Bindings"];
foreach (string argument in (string[])["run", "--project", "src/Ankus.Build", "-c", "Release", "--", .. command])
{
    start.ArgumentList.Add(argument);
}

if (!headers && args.Length == 2)
{
    start.ArgumentList.Add("--check");
}

using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the Ankus build tool.");
await process.WaitForExitAsync();
return process.ExitCode;

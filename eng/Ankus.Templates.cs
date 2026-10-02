#:property TargetFramework=net10.0
#:property PackAsTool=false
#:property PublishAot=false

using System.Text;

if (args.Length != 4)
{
    throw new ArgumentException("Specify the shared template root, configuration, staging directory and package version.");
}

string sourceRoot = Path.GetFullPath(args[0]);
string configuration = File.ReadAllText(args[1]);
string destination = Path.GetFullPath(args[2]);
string version = args[3];
Directory.CreateDirectory(destination);
bool[] variants = [false, true];
foreach (bool worker in variants)
{
    string name = worker ? "ankus-worker" : "ankus";
    string root = Path.Combine(destination, "content", name);
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }

    string[] sources = worker ? ["Extension", "BackgroundWorker"] : ["Extension"];
    foreach (string source in sources)
    {
        string directory = Path.Combine(sourceRoot, source);
        foreach (string path in Directory.EnumerateFiles(directory, "*.template", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(directory, path)[..^".template".Length];
            string target = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string text = File.ReadAllText(path)
                .Replace("__VERSION__", version, StringComparison.Ordinal)
                .Replace("__PRELOAD__", worker ? "true" : "false", StringComparison.Ordinal)
                .Replace("__WORKER_STATE__", "ankus.__WORKER_ID__", StringComparison.Ordinal);
            File.WriteAllText(target, text, new UTF8Encoding(false));
        }
    }

    string configDirectory = Path.Combine(root, ".template.config");
    Directory.CreateDirectory(configDirectory);
    File.WriteAllText(Path.Combine(configDirectory, "template.json"), configuration
        .Replace("__SHORT_NAME__", name, StringComparison.Ordinal)
        .Replace("__IDENTITY__", worker ? "BackgroundWorker" : "Extension", StringComparison.Ordinal)
        .Replace("__DISPLAY_NAME__", worker ? "Ankus PostgreSQL background worker" : "Ankus PostgreSQL extension", StringComparison.Ordinal),
        new UTF8Encoding(false));
}

File.WriteAllText(Path.Combine(destination, "complete.txt"), version, new UTF8Encoding(false));

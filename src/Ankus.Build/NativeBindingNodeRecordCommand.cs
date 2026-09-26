using System.Text.Json;
using Ankus.PgConfig;

namespace Ankus.Build;

/// <summary>
/// Collects the full selected-header node closure for the SDK's single managed companion.
/// </summary>
internal static class NativeBindingNodeRecordCommand
{
    /// <summary>
    /// Measures complete declaration identities using Clang independently of the native layout compiler.
    /// </summary>
    internal static async Task<NativeRecordGraph> RunAsync(NativeBindingCatalog catalog, string[] arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int major = catalog.PostgresMajor;
        PostgresInstallation installation = arguments[1].Length == 0
            ? await PostgresInstallation.DiscoverAsync(major, cancellationToken)
            : await PostgresInstallation.CreateAsync(arguments[1], cancellationToken);
        string compiler = arguments.Length >= 8 && arguments[7].Length != 0 ? arguments[7]
            : OperatingSystem.IsWindows() ? "clang-cl.exe" : "clang";
        // Windows process working directories cannot exceed MAX_PATH, even when managed file APIs support longer paths.
        string directory = Directory.CreateTempSubdirectory("ankus-node-").FullName;
        try
        {
            NativeBindingNodeRoots roots = NativeBindingNodeRecords.CreateRoots(catalog, NativeBindingResources.ReadHeaders(major));
            string source = NativeBindingHeaderParser.GenerateSource(NativeBindingHeaderTarget.GenerateSource(roots.Source, major), roots.Requests);
            string file = Path.Combine(directory, "native-node-types.c");
            await File.WriteAllTextAsync(file, source, cancellationToken);
            string[] frontend = ["", arguments[0], arguments[1], directory, compiler,
                arguments.Length >= 5 ? arguments[4] : "", arguments.Length >= 6 ? arguments[5] : "", arguments.Length >= 7 ? arguments[6] : ""];
            string observations = Path.Combine(directory, "native-node-target.json");
            await NativeBindingHeaderCommand.InspectAsync(installation, frontend, file, observations, directory, cancellationToken);
            NativeHeaderTarget target;
            await using (FileStream stream = File.OpenRead(observations))
            {
                using JsonDocument document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 512 }, cancellationToken);
                target = NativeBindingHeaderTarget.Read(document.RootElement, major);
            }

            string library = arguments.Length == 9 && arguments[8].Length != 0 ? Path.GetFullPath(arguments[8])
                : await NativeBindingRecordCommand.FindLibraryAsync(NativeBindingRecordCommand.FindCompiler(compiler), target.ClangMajor, cancellationToken);
            string ast = Path.Combine(directory, "native-node-types.ast");
            await NativeBindingHeaderCommand.InspectAsync(installation, frontend, file, Path.Combine(directory, "native-node-compile.txt"),
                directory, cancellationToken, dumpAst: false, serializedAst: ast);
            var request = new NativeRecordRequest(ast, library, target, roots.Requests.ToDictionary(static value => value.Name, StringComparer.Ordinal));
            NativeRecordGraph graph = await NativeBindingRecordWorker.InspectAsync(request, directory, cancellationToken);
            Console.WriteLine($"PG{major}: collected {graph.Declarations.Count} node dependency declarations and {graph.Types.Count} native types.");
            return graph;
        }
        finally
        {
            // Workers and compiler streams have joined before releasing their large temporary AST artifacts.
            Directory.Delete(directory, recursive: true);
        }
    }
}

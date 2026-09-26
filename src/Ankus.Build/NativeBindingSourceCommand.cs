using System.Globalization;
using System.Security;
using System.Text;

namespace Ankus.Build;

/// <summary>
/// Emits managed declarations only after compiling and validating the selected native header contract.
/// </summary>
internal static class NativeBindingSourceCommand
{
    private const string Project = """
        <Project>
          <PropertyGroup>
            <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
            <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
          </PropertyGroup>
          <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
          <PropertyGroup>
            <TargetFramework>$(AnkusBindingTargetFramework)</TargetFramework>
            <AssemblyName>$(AnkusBindingAssemblyName)</AssemblyName>
            <RootNamespace>Ankus.Postgres</RootNamespace>
            <OutputType>Library</OutputType>
            <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
            <Nullable>enable</Nullable>
            <IsAotCompatible>true</IsAotCompatible>
            <EnableNETAnalyzers>true</EnableNETAnalyzers>
            <AnalysisLevel>latest-recommended</AnalysisLevel>
            <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <MSTestAnalysisMode>All</MSTestAnalysisMode>
            <GenerateDocumentationFile>true</GenerateDocumentationFile>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
            <Deterministic>true</Deterministic>
            <Version>1.0.0</Version>
            <AssemblyVersion>1.0.0.0</AssemblyVersion>
            <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
            <PathMap>__ANKUS_PATH_MAP__</PathMap>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="native-binding.g.cs" />
            <Reference Include="Ankus.Runtime">
              <HintPath>$(AnkusRuntimeAssembly)</HintPath>
              <Private>false</Private>
            </Reference>
          </ItemGroup>
          <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
        </Project>
        """;

    /// <summary>
    /// Measures the selected headers and writes the managed companion source and assembly identity.
    /// </summary>
    /// <param name="arguments">The same selected installation and toolchain arguments as the layout command.</param>
    /// <param name="cancellationToken">Cancels probing and source emission.</param>
    internal static async Task RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        NativeBindingLayout layout = await NativeBindingLayoutCommand.RunAsync(arguments, cancellationToken);
        NativeBindingCatalog catalog = NativeBindingResources.ReadCatalog(layout.PostgresVersion / 10000);
        NativeBindingSource binding = NativeBindingCSharp.Generate(catalog, layout);
        string output = Path.GetFullPath(arguments[2]);
        await WriteIfChangedAsync(Path.Combine(output, "native-binding.g.cs"), binding.Source, cancellationToken);
        await WriteIfChangedAsync(Path.Combine(output, "native-binding.assembly-name"), binding.AssemblyName + "\n", cancellationToken);
        await WriteIfChangedAsync(Path.Combine(output, "native-binding.identity"), binding.AbiIdentity + "\n", cancellationToken);
        string pathMap = string.Join(',', new[] { output, PhysicalDirectory(new DirectoryInfo(output)) }
            .Distinct(StringComparer.Ordinal).Select(static path => path.Replace(",", ",,", StringComparison.Ordinal)
                .Replace("=", "==", StringComparison.Ordinal) + "=/_/Ankus.Postgres"));
        string project = Project.Replace("__ANKUS_PATH_MAP__", SecurityElement.Escape(EscapeProperty(pathMap)), StringComparison.Ordinal);
        await WriteIfChangedAsync(Path.Combine(output, "Ankus.NativeBindings.csproj"), project.ReplaceLineEndings("\n") + "\n", cancellationToken);
        Console.WriteLine($"Managed bindings: {binding.AssemblyName}");
    }

    private static string PhysicalDirectory(DirectoryInfo directory)
    {
        DirectoryInfo resolved = (DirectoryInfo?)directory.ResolveLinkTarget(returnFinalTarget: true) ?? directory;
        return resolved.Parent is DirectoryInfo parent
            ? Path.Combine(PhysicalDirectory(parent), resolved.Name)
            : resolved.FullName;
    }

    private static string EscapeProperty(string value)
    {
        var escaped = new StringBuilder();
        foreach (char character in value)
        {
            if (character is '%' or '$' or '@' or ';' or '\'' or '(' or ')' or '*' or '?')
            {
                escaped.Append('%').Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                escaped.Append(character);
            }
        }

        return escaped.ToString();
    }

    private static async Task WriteIfChangedAsync(string path, string value, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || await File.ReadAllTextAsync(path, cancellationToken) != value)
        {
            await File.WriteAllTextAsync(path, value, cancellationToken);
        }
    }
}

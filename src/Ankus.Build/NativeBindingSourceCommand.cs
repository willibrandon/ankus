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
            <PathMap>$(MSBuildProjectDirectory)=/_/Ankus.Postgres</PathMap>
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
        await WriteIfChangedAsync(Path.Combine(output, "Ankus.NativeBindings.csproj"), Project.ReplaceLineEndings("\n") + "\n", cancellationToken);
        Console.WriteLine($"Managed bindings: {binding.AssemblyName}");
    }

    private static async Task WriteIfChangedAsync(string path, string value, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || await File.ReadAllTextAsync(path, cancellationToken) != value)
        {
            await File.WriteAllTextAsync(path, value, cancellationToken);
        }
    }
}

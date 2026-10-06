using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Defaults, independent overrides and exact string bytes produce a header-selected native declaration.
    /// </summary>
    /// <param name="declaration">The optional assembly attribute.</param>
    /// <param name="projectVersion">The evaluated project version.</param>
    /// <param name="expectedName">The exact C literal for the module name.</param>
    /// <param name="expectedVersion">The exact C literal for the module version.</param>
    [TestMethod]
    [DataRow("", null, "\\x47\\x65\\x6e\\x65\\x72\\x61\\x74\\x6f\\x72\\x54\\x65\\x73\\x74", "\\x30\\x2e\\x30\\x2e\\x30\\x2e\\x30")]
    [DataRow("", "2.3.4-rc.1", "\\x47\\x65\\x6e\\x65\\x72\\x61\\x74\\x6f\\x72\\x54\\x65\\x73\\x74", "\\x32\\x2e\\x33\\x2e\\x34\\x2d\\x72\\x63\\x2e\\x31")]
    [DataRow("[assembly: Ankus.PgModule(Name = \"A\")]", "1", "\\x41", "\\x31")]
    [DataRow("[assembly: Ankus.PgModule(Version = \"v\")]", "1", "\\x47\\x65\\x6e\\x65\\x72\\x61\\x74\\x6f\\x72\\x54\\x65\\x73\\x74", "\\x76")]
    [DataRow("[assembly: Ankus.PgModule(Name = \"é\\\"\\\\\\n9\", Version = \"v😀\")]", "1", "\\xc3\\xa9\\x22\\x5c\\x0a\\x39", "\\x76\\xf0\\x9f\\x98\\x80")]
    [DataRow("[assembly: Ankus.PgModule(Name = \"\", Version = \"\")]", "1", "", "")]
    [DataRow("[assembly: Ankus.PgModule(Name = null, Version = null)]", "1", "\\x47\\x65\\x6e\\x65\\x72\\x61\\x74\\x6f\\x72\\x54\\x65\\x73\\x74", "\\x31")]
    public void NativeModuleIdentityPreservesDefaultsAndOverrides(string declaration, string? projectVersion,
        string expectedName, string expectedVersion)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(declaration + """
            public static class Functions
            {
                [Ankus.PgFunction] public static int Answer() => 42;
            }
            """, options: new ModuleOptions(projectVersion));

        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("#if PG_VERSION_NUM >= 180000\nPG_MODULE_MAGIC_EXT(\n    .name = \"" + expectedName +
            "\",\n    .version = \"" + expectedVersion + "\");\n#else\nPG_MODULE_MAGIC;\n#endif", native);
        Assert.AreEqual(1, native.Split("PG_MODULE_MAGIC_EXT", StringSplitOptions.None).Length - 1);
        Assert.Contains("Pg_magic_func\n", ManifestValue(compilation, "Ankus.Exports"));
    }

    /// <summary>
    /// An explicit module identity creates a native module even without SQL functions.
    /// </summary>
    [TestMethod]
    public void NativeModuleIdentityAloneEmitsManifest()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("[assembly: Ankus.PgModule]");
        Assert.IsEmpty(diagnostics);
        Assert.AreEqual("-- No installable objects declared.\n", InstallationBody(compilation));
        Assert.Contains("PG_MODULE_MAGIC_EXT", ManifestValue(compilation, "Ankus.NativeSource"));
        Assert.Contains("Pg_magic_func\n", ManifestValue(compilation, "Ankus.Exports"));
    }

    /// <summary>
    /// Invalid C-string values produce an actionable diagnostic on the exact authored expression.
    /// </summary>
    /// <param name="property">The identity member.</param>
    /// <param name="literal">The malformed managed string expression.</param>
    /// <param name="expected">The independently required field transport error.</param>
    [TestMethod]
    [DataRow("Name", "\"a\\0b\"", "ANKUS350")]
    [DataRow("Version", "\"a\\0b\"", "ANKUS352")]
    [DataRow("Name", "\"\\ud800\"", "ANKUS351")]
    [DataRow("Version", "\"\\ud800\"", "ANKUS353")]
    [DataRow("Name", "\"\\udc00\"", "ANKUS351")]
    [DataRow("Version", "\"\\udc00\"", "ANKUS353")]
    public void NativeModuleIdentityRejectsTruncationAndReplacement(string property, string literal, string expected)
    {
        string source = "[assembly: Ankus.PgModule(" + property + " = " + literal + ")]";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(expected, diagnostic.Id);
        Assert.Contains("PgModule." + property, diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.AreEqual(literal, diagnostic.Location.SourceTree!.GetText(context.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        Assert.EndsWith("#module-identity-diagnostics", diagnostic.Descriptor.HelpLinkUri);
        Assert.DoesNotContain(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" &&
            attribute.ConstructorArguments[0].Value is "Ankus.NativeSource", compilation.Assembly.GetAttributes());
    }

    /// <summary>
    /// Supplies the evaluated SDK version property independently of the generator's implementation.
    /// </summary>
    private sealed class ModuleOptions(string? version) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new ModuleGlobalOptions(version);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    /// <summary>
    /// Supplies only the project version option.
    /// </summary>
    private sealed class ModuleGlobalOptions(string? version) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = version ?? string.Empty;
            return key == "build_property.Version" && version is not null;
        }
    }
}

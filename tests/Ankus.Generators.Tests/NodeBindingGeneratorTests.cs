using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// A measured companion identity reaches the native capability independently of the callback's SQL behavior.
    /// </summary>
    [TestMethod]
    public void NativeNodeCapabilityUsesTheCompilationsMeasuredBindingIdentity()
    {
        const string identity = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            namespace Ankus.Postgres
            {
                public static class NativeBinding
                {
                    public const string Identity = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
                }
            }

            public static class Functions
            {
                [Ankus.PgFunction]
                public static int Value() => 1;
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("static const char ankus_node_binding_identity[] = \"" + identity + "\";", native);
        Assert.Contains("request->value != PG_VERSION_NUM / 10000", native);
        Assert.Contains("memcmp((const void *) request->data, ankus_node_binding_identity", native);
    }

    /// <summary>
    /// A lookalike declaration cannot inject C source or supply a malformed ABI identity to the native capability.
    /// </summary>
    /// <param name="identity">An invalid declaration identity.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("short")]
    [DataRow("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    [DataRow("\"; bad_native_source(); //")]
    public void NativeNodeCapabilityRejectsMalformedBindingConstants(string identity)
    {
        string literal = SymbolDisplay.FormatLiteral(identity, quote: true);
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            namespace Ankus.Postgres
            {
                public static class NativeBinding
                {
                    public const string Identity = {{literal}};
                }
            }

            public static class Functions
            {
                [Ankus.PgFunction]
                public static int Value() => 1;
            }
            """);
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("static const char ankus_node_binding_identity[] = \"\";", native);
        Assert.DoesNotContain("bad_native_source();", native);
    }
}

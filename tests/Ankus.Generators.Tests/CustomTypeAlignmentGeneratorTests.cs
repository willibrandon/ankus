using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Defaults and explicit storage selections emit the corresponding PostgreSQL type contract.
    /// </summary>
    /// <param name="setting">The optional alignment argument.</param>
    /// <param name="expected">The SQL alignment spelling.</param>
    [TestMethod]
    [DataRow("", "int4")]
    [DataRow("Alignment = Ankus.PgTypeAlignment.FourBytes", "int4")]
    [DataRow("Alignment = Ankus.PgTypeAlignment.EightBytes", "double")]
    public void CustomTypeAlignmentSelectsStorage(string setting, string expected)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgType({{setting}})]
            public readonly record struct Value(long Number);
            """);
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains("ALIGNMENT = " + expected + ", STORAGE = extended);", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Out-of-range attribute values fail before PostgreSQL sees invalid type DDL.
    /// </summary>
    /// <param name="value">An undefined enum value.</param>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(2)]
    [DataRow(8)]
    public void CustomTypeAlignmentRejectsInvalidValues(int value)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgType(Alignment = (Ankus.PgTypeAlignment)({{value}}))]
            public readonly record struct Value(long Number);
            """);
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS419", diagnostic.Id);
        Assert.Contains("PgTypeAlignment.FourBytes", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain("CREATE TYPE", ManifestValue(compilation, "Ankus.Sql"));
    }
}

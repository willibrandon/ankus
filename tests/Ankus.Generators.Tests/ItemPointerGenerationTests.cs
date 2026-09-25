using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Compiles all scalar and array tid shapes into their exact SQL contracts.
    /// </summary>
    /// <param name="managed">The managed signature.</param>
    /// <param name="sqlType">The PostgreSQL signature.</param>
    [TestMethod]
    [DataRow("Ankus.PgItemPointer", "tid")]
    [DataRow("Ankus.PgItemPointer?", "tid")]
    [DataRow("Ankus.PgItemPointer[]", "tid[]")]
    [DataRow("Ankus.PgItemPointer?[]?", "tid[]")]
    [DataRow("Ankus.PgArray<Ankus.PgItemPointer?>", "tid[]")]
    public void ItemPointerSignaturesCompile(string managed, string sqlType)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            #nullable enable
            public static class Functions
            {
                [Ankus.PgFunction] public static {{managed}} Echo({{managed}} value) => value;
            }
            """);
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("\"value\" " + sqlType, sql);
        Assert.Contains("RETURNS " + sqlType + " AS", sql);
    }

    /// <summary>
    /// Uses selected-header field conversion rather than a managed ItemPointerData memory layout.
    /// </summary>
    [TestMethod]
    public void ItemPointerScalarUsesFieldWiseConversion()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction] public static Ankus.PgItemPointer Echo(Ankus.PgItemPointer value) => value;
            }
            """);
        Assert.IsEmpty(diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("ankus_read_item_pointer(PG_GETARG_DATUM(0), &arguments[0]);", native);
        Assert.Contains("datum = ankus_write_item_pointer(&result);", native);
        Assert.Contains("case TIDOID: return ankus_write_item_pointer(value);", native);
        Assert.Contains("case TIDOID: ankus_read_item_pointer(datum, value); break;", native);
        Assert.Contains("ItemPointerGetBlockNumberNoCheck(pointer)", native);
        Assert.Contains("palloc(sizeof(ItemPointerData))", native);
    }
}

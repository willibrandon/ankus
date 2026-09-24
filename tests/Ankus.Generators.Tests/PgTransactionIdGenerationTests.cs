using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Generates scalar, nullable, vector, and shaped-array xid contracts without treating xid as oid.
    /// </summary>
    /// <param name="managed">The managed transaction ID shape.</param>
    /// <param name="sqlType">The expected SQL type.</param>
    [TestMethod]
    [DataRow("Ankus.PgTransactionId", "xid")]
    [DataRow("Ankus.PgTransactionId?", "xid")]
    [DataRow("Ankus.PgTransactionId?[]?", "xid[]")]
    [DataRow("Ankus.PgArray<Ankus.PgTransactionId?>", "xid[]")]
    public void TransactionIdSignaturesCompile(string managed, string sqlType)
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
    /// Uses PostgreSQL's transaction ID datum macros at the generated scalar boundary.
    /// </summary>
    [TestMethod]
    public void TransactionIdScalarUsesNativeXidConversion()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public static class Functions
            {
                [Ankus.PgFunction]
                public static Ankus.PgTransactionId Echo(Ankus.PgTransactionId value) => value;
            }
            """);
        Assert.IsEmpty(diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        Assert.Contains("PG_GETARG_TRANSACTIONID(0)", native);
        Assert.Contains("TransactionIdGetDatum(result.integral)", native);
        Assert.Contains("case XIDOID: return TransactionIdGetDatum(value->integral);", native);
        Assert.Contains("case XIDOID: value->integral = DatumGetTransactionId(datum); break;", native);
    }
}

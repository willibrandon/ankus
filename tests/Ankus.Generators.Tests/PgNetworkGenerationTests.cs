using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Network wrappers compile with exact SQL type contracts and nullable/vector adapters.
    /// </summary>
    [TestMethod]
    [DataRow("Ankus.PgInet", "inet")]
    [DataRow("Ankus.PgInet?", "inet")]
    [DataRow("Ankus.PgCidr", "cidr")]
    [DataRow("Ankus.PgCidr?", "cidr")]
    [DataRow("System.Net.IPAddress", "inet")]
    [DataRow("System.Net.IPAddress?", "inet")]
    [DataRow("System.Net.IPNetwork", "cidr")]
    [DataRow("System.Net.IPNetwork?", "cidr")]
    [DataRow("Ankus.PgArray<Ankus.PgInet?>", "inet[]")]
    [DataRow("Ankus.PgArray<Ankus.PgCidr?>?", "cidr[]")]
    [DataRow("System.Net.IPAddress?[]?", "inet[]")]
    [DataRow("System.Net.IPNetwork?[]", "cidr[]")]
    public void NetworkSignaturesCompile(string type, string sqlType)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            #nullable enable
            public static class Functions
            {
                [Ankus.PgFunction] public static {{type}} Echo({{type}} value) => value;
            }
            """);
        Assert.IsEmpty(diagnostics);
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static item => item.Severity == DiagnosticSeverity.Error));
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("\"value\" " + sqlType, sql);
        Assert.Contains("RETURNS " + sqlType + " AS", sql);
    }
}

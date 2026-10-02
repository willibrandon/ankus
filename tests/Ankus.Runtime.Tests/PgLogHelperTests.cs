namespace Ankus.Runtime.Tests;

public sealed partial class NativeLogTests
{
    /// <summary>
    /// Every terminal convenience overload advertises its verified nonreturning behavior to compiler flow analysis.
    /// </summary>
    [TestMethod]
    public void TerminalHelpersExposeNonreturningContracts()
    {
        string[] names = [nameof(PgLog.Error), nameof(PgLog.Fatal), nameof(PgLog.Panic)];
        Type[] parameters = [typeof(string), typeof(PgDiagnostic)];
        foreach (string name in names)
        {
            foreach (Type parameter in parameters)
            {
                System.Reflection.MethodInfo? method = typeof(PgLog).GetMethod(name, [parameter]);
                Assert.IsNotNull(method);
                Assert.IsTrue(method.IsDefined(typeof(System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute), inherit: false));
            }
        }
    }

    /// <summary>
    /// Severity helpers retain native discriminators, literal Unicode, explicit SQLSTATE and structured field ownership.
    /// </summary>
    /// <param name="level">The helper's PostgreSQL severity.</param>
    /// <param name="nativeLevel">The independently expected ABI discriminator.</param>
    [TestMethod]
    [DataRow(PgLogLevel.Debug5, 0)]
    [DataRow(PgLogLevel.Debug4, 1)]
    [DataRow(PgLogLevel.Debug3, 2)]
    [DataRow(PgLogLevel.Debug2, 3)]
    [DataRow(PgLogLevel.Debug1, 4)]
    [DataRow(PgLogLevel.Log, 5)]
    [DataRow(PgLogLevel.ServerOnly, 6)]
    [DataRow(PgLogLevel.Info, 7)]
    [DataRow(PgLogLevel.Notice, 8)]
    [DataRow(PgLogLevel.Warning, 9)]
    public void SeverityHelpersPreserveDiagnostics(PgLogLevel level, int nativeLevel)
    {
        using var fixture = new LogFixture();
        using var scope = new LogScope(LogPointer);
        ReportHelper(level, "literal 100% %s café 🐘");
        Assert.AreEqual("literal 100% %s café 🐘", fixture.Fields[(int)NativeDiagnosticField.Message]);
        Assert.AreEqual(0, fixture.SqlState);
        Assert.AreEqual(1, fixture.ReportReleases);
        var diagnostic = new PgDiagnostic("structured") { SqlState = "22023", Detail = "detail café", Hint = "" };
        ReportHelper(level, diagnostic);
        Assert.AreSequenceEqual([(1, 0, nativeLevel), (1, 1, nativeLevel), (1, 0, nativeLevel), (1, 1, nativeLevel)], fixture.Calls);
        Assert.AreEqual("structured", fixture.Fields[(int)NativeDiagnosticField.Message]);
        Assert.AreEqual("detail café", fixture.Fields[(int)NativeDiagnosticField.Detail]);
        Assert.AreEqual("", fixture.Fields[(int)NativeDiagnosticField.Hint]);
        Assert.AreEqual(50_856_066, fixture.SqlState);
        Assert.AreEqual(4, fixture.ReportReleases);
    }

    /// <summary>
    /// Filtered severity helpers avoid encoding invalid text while retaining capability validation and null checks.
    /// </summary>
    /// <param name="level">The helper's nonterminal severity.</param>
    [TestMethod]
    [DataRow(PgLogLevel.Debug5)]
    [DataRow(PgLogLevel.Debug4)]
    [DataRow(PgLogLevel.Debug3)]
    [DataRow(PgLogLevel.Debug2)]
    [DataRow(PgLogLevel.Debug1)]
    [DataRow(PgLogLevel.Log)]
    [DataRow(PgLogLevel.ServerOnly)]
    [DataRow(PgLogLevel.Info)]
    [DataRow(PgLogLevel.Notice)]
    [DataRow(PgLogLevel.Warning)]
    public void SeverityHelpersHonorFilteringAndCapability(PgLogLevel level)
    {
        using var fixture = new LogFixture { Enabled = false };
        Assert.ThrowsExactly<InvalidOperationException>(() => ReportHelper(level, "detached"));
        using var scope = new LogScope(LogPointer);
        Assert.ThrowsExactly<ArgumentNullException>(() => ReportHelper(level, (string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ReportHelper(level, (PgDiagnostic)null!));
        ReportHelper(level, "invalid\ud800");
        Assert.AreEqual((1, 0, (int)level), Assert.ContainsSingle(fixture.Calls));
        Assert.AreEqual(0, fixture.ReportReleases);
    }

    /// <summary>
    /// Terminal helpers preserve default SQLSTATE, exact explicit fields and fatal requests before managed unwinding.
    /// </summary>
    /// <param name="level">ERROR, FATAL or PANIC.</param>
    [TestMethod]
    [DataRow(PgLogLevel.Error)]
    [DataRow(PgLogLevel.Fatal)]
    [DataRow(PgLogLevel.Panic)]
    public void TerminalHelpersPreserveUnwinding(PgLogLevel level)
    {
        using var fixture = new LogFixture { Enabled = false };
        Assert.ThrowsExactly<InvalidOperationException>(() => ReportHelper(level, "detached"));
        Assert.IsEmpty(fixture.Calls);
        using var scope = new LogScope(LogPointer);
        Assert.ThrowsExactly<ArgumentNullException>(() => ReportHelper(level, (string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ReportHelper(level, (PgDiagnostic)null!));
        Assert.ThrowsExactly<ArgumentException>(() => ReportHelper(level, new PgDiagnostic("invalid") { SqlState = "00000" }));
        Assert.IsEmpty(fixture.Calls);
        if (level == PgLogLevel.Error)
        {
            PgException failure = Assert.ThrowsExactly<PgException>(() => ReportHelper(level, "default error"));
            Assert.AreEqual("XX000", failure.SqlState);
            Assert.AreEqual("default error", failure.Message);
        }
        else
        {
            PgTerminalException failure = Assert.ThrowsExactly<PgTerminalException>(() => ReportHelper(level, "default terminal"));
            Assert.AreEqual(level, failure.Level);
            Assert.IsNull(failure.Diagnostic.SqlState);
            Assert.AreEqual("default terminal", failure.Message);
        }

        var diagnostic = new PgDiagnostic("explicit error") { SqlState = "22023", Detail = "detail", Hint = "hint" };
        if (level == PgLogLevel.Error)
        {
            PgException failure = Assert.ThrowsExactly<PgException>(() => ReportHelper(level, diagnostic));
            Assert.AreEqual("22023", failure.SqlState);
            Assert.AreEqual("detail", failure.Detail);
            Assert.AreEqual("hint", failure.Hint);
            Assert.IsEmpty(fixture.Calls);
        }
        else
        {
            PgTerminalException failure = Assert.ThrowsExactly<PgTerminalException>(() => ReportHelper(level, diagnostic));
            Assert.AreEqual(level, failure.Level);
            Assert.AreSame(diagnostic, failure.Diagnostic);
            Assert.AreSequenceEqual([(1, 2, (int)level), (1, 2, (int)level)], fixture.Calls);
        }
    }

    /// <summary>
    /// Invokes the literal-text overload for the selected helper.
    /// </summary>
    private static void ReportHelper(PgLogLevel level, string message)
    {
        switch (level)
        {
            case PgLogLevel.Debug5:
                PgLog.Debug5(message);
                break;
            case PgLogLevel.Debug4:
                PgLog.Debug4(message);
                break;
            case PgLogLevel.Debug3:
                PgLog.Debug3(message);
                break;
            case PgLogLevel.Debug2:
                PgLog.Debug2(message);
                break;
            case PgLogLevel.Debug1:
                PgLog.Debug1(message);
                break;
            case PgLogLevel.Log:
                PgLog.Log(message);
                break;
            case PgLogLevel.ServerOnly:
                PgLog.ServerOnly(message);
                break;
            case PgLogLevel.Info:
                PgLog.Info(message);
                break;
            case PgLogLevel.Notice:
                PgLog.Notice(message);
                break;
            case PgLogLevel.Warning:
                PgLog.Warning(message);
                break;
            case PgLogLevel.Error:
                PgLog.Error(message);
                break;
            case PgLogLevel.Fatal:
                PgLog.Fatal(message);
                break;
            case PgLogLevel.Panic:
                PgLog.Panic(message);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(level));
        }
    }

    /// <summary>
    /// Invokes the structured overload for the selected helper.
    /// </summary>
    private static void ReportHelper(PgLogLevel level, PgDiagnostic diagnostic)
    {
        switch (level)
        {
            case PgLogLevel.Debug5:
                PgLog.Debug5(diagnostic);
                break;
            case PgLogLevel.Debug4:
                PgLog.Debug4(diagnostic);
                break;
            case PgLogLevel.Debug3:
                PgLog.Debug3(diagnostic);
                break;
            case PgLogLevel.Debug2:
                PgLog.Debug2(diagnostic);
                break;
            case PgLogLevel.Debug1:
                PgLog.Debug1(diagnostic);
                break;
            case PgLogLevel.Log:
                PgLog.Log(diagnostic);
                break;
            case PgLogLevel.ServerOnly:
                PgLog.ServerOnly(diagnostic);
                break;
            case PgLogLevel.Info:
                PgLog.Info(diagnostic);
                break;
            case PgLogLevel.Notice:
                PgLog.Notice(diagnostic);
                break;
            case PgLogLevel.Warning:
                PgLog.Warning(diagnostic);
                break;
            case PgLogLevel.Error:
                PgLog.Error(diagnostic);
                break;
            case PgLogLevel.Fatal:
                PgLog.Fatal(diagnostic);
                break;
            case PgLogLevel.Panic:
                PgLog.Panic(diagnostic);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(level));
        }
    }
}

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Visible inherited role methods cannot silently disappear when an optional capability interface is absent.
    /// </summary>
    /// <param name="role">The inherited optional role.</param>
    /// <param name="accessibility">The inherited method's accessibility.</param>
    [TestMethod]
    [DataRow("Final", "public")]
    [DataRow("Combine", "public")]
    [DataRow("Serialize", "public")]
    [DataRow("Deserialize", "public")]
    [DataRow("MovingTransition", "public")]
    [DataRow("MovingInverse", "public")]
    [DataRow("MovingFinal", "public")]
    [DataRow("Combine", "protected")]
    [DataRow("Combine", "internal")]
    [DataRow("Combine", "protected internal")]
    [DataRow("Combine", "private protected")]
    public void InheritedAggregateRoleRequiresCapability(string role, string accessibility)
    {
        string callback = role switch
        {
            "Serialize" => accessibility + " static byte[]? Serialize(Ankus.PgAggregateContext context, T state) => null;",
            "Deserialize" => accessibility + " static T Deserialize(Ankus.PgAggregateContext context, byte[] bytes) => default!;",
            _ => accessibility + " static T " + role + "(Ankus.PgAggregateContext context, T state, T other) => state;",
        };
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public abstract class Parent<T>
            {
                {{callback}}
            }
            public abstract class Intermediate : Parent<int> { }
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Typed : Intermediate, Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int input) => state + input;
            }
            """);

        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS111", error.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("The " + role + " method requires its aggregate capability interface", error.GetMessage(CultureInfo.InvariantCulture));
        Assert.AreEqual(role, error.Location.SourceTree!.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.DoesNotContain("CREATE AGGREGATE", InstallationBody(compilation));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Instance helpers cannot implement static aggregate roles and do not require optional capabilities.
    /// </summary>
    /// <param name="role">The helper's otherwise reserved callback name.</param>
    /// <param name="inherited">Whether the instance helper is inherited.</param>
    [TestMethod]
    [DataRow("Final", false)]
    [DataRow("Combine", false)]
    [DataRow("Serialize", false)]
    [DataRow("Deserialize", false)]
    [DataRow("MovingTransition", false)]
    [DataRow("MovingInverse", false)]
    [DataRow("MovingFinal", false)]
    [DataRow("Combine", true)]
    [DataRow("Serialize", true)]
    public void AggregateInstanceHelpersRemainValid(string role, bool inherited)
    {
        string helper = "public int " + role + "(int value) => value;";
        string source = "public abstract class Parent { " + (inherited ? helper : string.Empty) + " } " +
            "[Ankus.PgAggregate(InitialCondition = \"0\")] public sealed class Typed : Parent, Ankus.IPgAggregate<int, int> { " +
            "public static int Transition(Ankus.PgAggregateContext context, int state, int input) => state + input; " +
            (inherited ? string.Empty : helper) + " }";
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(source);
        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE", InstallationBody(compilation));
        Assert.DoesNotContain("COMBINEFUNC", InstallationBody(compilation));
        Assert.DoesNotContain("SERIALFUNC", InstallationBody(compilation));
        Assert.DoesNotContain("FINALFUNC =", InstallationBody(compilation));
    }

    /// <summary>
    /// A private base helper is not an inherited member of an unrelated derived aggregate.
    /// </summary>
    [TestMethod]
    public void InaccessibleBaseAggregateHelperRemainsValid()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public abstract class Parent
            {
                private static int Combine(int state, int other) => state + other;
            }
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Typed : Parent, Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int input) => state + input;
            }
            """);

        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE", InstallationBody(compilation));
        Assert.DoesNotContain("COMBINEFUNC", InstallationBody(compilation));
    }

    /// <summary>
    /// Static application helpers with a reserved role name are ignored unless their shape identifies an aggregate callback.
    /// </summary>
    /// <param name="callback">The unrelated static helper declaration.</param>
    [TestMethod]
    [DataRow("public static int Combine(int left, int right) => left + right;")]
    [DataRow("public static int Combine(Ankus.PgFunctionContext context, int left, int right) => left + right;")]
    [DataRow("public static void Combine(Ankus.PgAggregateContext context, int left, int right) { }")]
    [DataRow("public static string Combine(Ankus.PgAggregateContext context, string left, string right) => left + right;")]
    [DataRow("public static int Serialize(Ankus.PgAggregateContext context, int state) => state;")]
    [DataRow("public static int Deserialize(Ankus.PgAggregateContext context, int value) => value;")]
    [DataRow("public static int MovingTransition(Ankus.PgAggregateContext context, int state, string value) => state;")]
    public void StaticHelpersWithReservedNamesRemainValid(string callback)
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Typed : Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int input) => state + input;
                {{callback}}
            }
            """);

        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE", InstallationBody(compilation));
        Assert.DoesNotContain("COMBINEFUNC", InstallationBody(compilation));
    }

    /// <summary>
    /// One inherited callback declaration produces one actionable diagnostic when several aggregates omit its capability.
    /// </summary>
    [TestMethod]
    public void SharedInheritedRoleReportsOnce()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public abstract class Parent<T>
            {
                public static T Combine(Ankus.PgAggregateContext context, T state, T other) => state;
            }
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class First : Parent<int>, Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int input) => state + input;
            }
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Second : Parent<int>, Ankus.IPgAggregate<int, int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int input) => state + input;
            }
            """);

        Diagnostic error = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("ANKUS111", error.Id);
        Assert.AreEqual("Combine", error.Location.SourceTree!.GetText(context.CancellationToken).ToString(error.Location.SourceSpan));
        Assert.DoesNotContain("CREATE AGGREGATE", InstallationBody(compilation));
        Assert.IsEmpty(compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Inherited implementations of declared capabilities remain compiler checked and appear in the SQL contract.
    /// </summary>
    [TestMethod]
    public void InheritedDeclaredAggregateCapabilityRemainsValid()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            public abstract class Parent : Ankus.IPgAggregate<int, int>, Ankus.IPgCombinableAggregate<int>
            {
                public static int Transition(Ankus.PgAggregateContext context, int state, int input) => state + input;
                public static int Combine(Ankus.PgAggregateContext context, int state, int other) => state + other;
            }
            [Ankus.PgAggregate(InitialCondition = "0")]
            public sealed class Typed : Parent { }
            """);

        AssertAggregateCompilation(compilation, diagnostics);
        Assert.Contains("CREATE AGGREGATE", InstallationBody(compilation));
        Assert.Contains("COMBINEFUNC =", InstallationBody(compilation));
        Assert.Contains("\"typed_combine\"", InstallationBody(compilation));
    }
}

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
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate($$"""
            public abstract class Parent<T>
            {
                {{accessibility}} static T {{role}}(Ankus.PgAggregateContext context, T state, T other) => state;
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

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies generated code cannot be redirected by user types named like the namespaces and types it uses.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Every declaration kind compiles beside types named <c>System</c>, <c>Ankus</c> and common runtime types, as
    /// pgrx's <c>pgrx_module_qualification</c> test requires of its macros.
    /// </summary>
    [TestMethod]
    public void DeclarationsCompileBesideShadowingTypeNames()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            namespace Probe.Shadowed
            {
                public sealed class System { }
                public sealed class Ankus { }
                public sealed class Generated { }
                public sealed class CompilerServices { }
                public sealed class Runtime { }
                public sealed class Spi { }
                public sealed class SpiParameter { }
                public sealed class NativeValue { }
                public sealed class PgHeapTuple { }
                public sealed class PgArray<T> { }
                public sealed class Exception { }
                public sealed class InvalidOperationException { }
                public sealed class ArgumentNullException { }
                public sealed class String { }
                public sealed class Int32 { }
                public sealed class Int64 { }
                public sealed class Object { }
                public sealed class Type { }
                public sealed class Span<T> { }
                public sealed class ReadOnlySpan<T> { }
                public sealed class Unsafe { }
                public sealed class Marshal { }
                public sealed class GC { }
                public sealed class Math { }
                public sealed class IntPtr { }
                public sealed class Encoding { }
                public sealed class CultureInfo { }
                public sealed class Func<T> { }
                public sealed class Action { }
                public sealed class IEnumerable<T> { }
                public sealed class IEnumerator<T> { }
                public sealed class List<T> { }
                public sealed class Dictionary<TKey, TValue> { }

                [global::Ankus.PgEnum]
                public enum Mood
                {
                    Happy,
                    Sad,
                }

                [global::Ankus.PgType(TextCodec = typeof(ValueCodec))]
                public sealed record Value(int Number);

                public sealed class ValueCodec : global::Ankus.PgTypeTextCodec<Value>
                {
                    public override Value Parse(string text) => new(int.Parse(text, global::System.Globalization.CultureInfo.InvariantCulture));
                    public override string Format(Value value) => value.Number.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
                }

                [global::Ankus.PgAggregate(InitialCondition = "0")]
                public sealed class SumValues : global::Ankus.IPgAggregate<long, int>
                {
                    public static long Transition(global::Ankus.PgAggregateContext context, long state, int value) => state + value;
                }

                public static partial class Settings
                {
                    [global::Ankus.PgGucInt("probe.limit", 10, "Limit")]
                    public static partial int Limit { get; }

                    [global::Ankus.PgGucString("probe.label", null, "Label")]
                    public static partial string? Label { get; }

                    [global::Ankus.PgGucEnum("probe.mood", Mood.Happy, "Mood")]
                    public static partial Mood Mood { get; }
                }

                public static partial class Functions
                {
                    [global::Ankus.PgFunction]
                    public static int Echo(int value, long other = 2) => value;

                    [global::Ankus.PgFunction]
                    public static string? Text(string? value) => value;

                    [global::Ankus.PgFunction]
                    public static global::Ankus.PgArray<int?> Values(global::Ankus.PgArray<int?> values) => values;

                    [global::Ankus.PgFunction]
                    public static int[] Vector(params int[] values) => values;

                    [global::Ankus.PgFunction]
                    public static Mood Swap(Mood mood) => mood == Mood.Happy ? Mood.Sad : Mood.Happy;

                    [global::Ankus.PgFunction]
                    public static Value? Copy(Value? value) => value;

                    [global::Ankus.PgFunction]
                    public static global::System.Collections.Generic.IEnumerable<int> Series(int count)
                    {
                        for (int index = 0; index < count; index++)
                        {
                            yield return index;
                        }
                    }

                    [global::Ankus.PgFunction]
                    public static global::System.Collections.Generic.IEnumerable<(int Id, string Name)> Rows()
                    {
                        yield return (1, "one");
                    }

                    [global::Ankus.PgFunction]
                    [return: global::Ankus.PgCompositeType("dog")]
                    public static global::Ankus.PgHeapTuple? Dog([global::Ankus.PgCompositeType("dog")] global::Ankus.PgHeapTuple? value) => value;

                    [global::Ankus.PgOperator("@@@")]
                    public static int Add(int left, int right) => left + right;

                    [global::Ankus.PgFunction, global::Ankus.PgCast]
                    public static int ToNumber(Value value) => value.Number;

                    [global::Ankus.PgTrigger]
                    public static global::Ankus.PgHeapTuple? Audit(global::Ankus.PgTriggerContext context) => null;

                    [global::Ankus.PgBackgroundWorker]
                    public static void Run(nuint argument)
                    {
                    }

                    [global::Ankus.PgTest]
                    public static void Backend()
                    {
                    }
                }
            }
            """);
        Diagnostic[] generated = [.. diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Assert.IsEmpty(generated, string.Join(Environment.NewLine, generated.Select(static error => error.ToString())));
        Diagnostic[] errors = [.. compilation.GetDiagnostics(context.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
        using var assembly = new MemoryStream();
        Assert.IsTrue(compilation.Emit(assembly, cancellationToken: context.CancellationToken).Success);
    }
}

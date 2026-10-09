using System.Text;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies generation, shrinking and failure reports of the property runner without a backend; each case runs
/// directly instead of in a subtransaction.
/// </summary>
[TestClass]
public sealed class PgPropertyRunnerTests
{
    /// <summary>
    /// A failing integer property shrinks to the boundary on either side of zero, and the failure keeps its cause.
    /// </summary>
    [TestMethod]
    public void IntegersShrinkToTheBoundary()
    {
        PgPropertyException above = Fail(PgGenerators.Number<int>(), static value =>
        {
            if (value >= 1_000)
            {
                throw new InvalidOperationException("too large");
            }
        });
        Assert.AreEqual("1000", above.Input);
        Assert.IsInstanceOfType<InvalidOperationException>(above.InnerException);
        Assert.StartsWith("Test failed: too large; minimal failing input: 1000. ", above.Message);
        Assert.Contains("rerun with seed " + above.Seed, above.Message);
        Assert.AreEqual("-1000", Fail(PgGenerators.Number<long>(), static value => Check(value > -1_000)).Input);
        Assert.AreEqual("5", Fail(PgGenerators.Number<byte>(5, 200), static value => Check(value > 200)).Input);
        Assert.AreEqual("-5", Fail(PgGenerators.Number<short>(-200, -5), static value => Check(value < -200)).Input);
    }

    /// <summary>
    /// Text shrinks to the one character that fails, arrays to their fewest elements, and floating-point values to a
    /// special value when one fails.
    /// </summary>
    [TestMethod]
    public void TextArraysAndFloatingPointShrink()
    {
        Assert.AreEqual("\"z\"", Fail(PgGenerators.Text(), static value => Check(!value.Contains('z', StringComparison.Ordinal))).Input);
        PgPropertyException array = Fail(PgGenerators.Array(PgGenerators.Number<int>(0, 100)), static values => Check(values.Sum() < 150));
        Assert.HasCount(2, array.Input.Split(", "));
        Assert.AreEqual("NaN", Fail(PgGenerators.FloatingPoint<double>(), static value => Check(double.IsFinite(value))).Input);
        Assert.AreEqual("[]", Fail(PgGenerators.Array(PgGenerators.Boolean()), static values => Check(values.Length > 100)).Input);
    }

    /// <summary>
    /// The same seed draws the same inputs, and a passing run draws the configured number of cases.
    /// </summary>
    [TestMethod]
    public void SeedsReproduceInputs()
    {
        PgGenerator<(int, string)> generator =
            from number in PgGenerators.Number<int>()
            from text in PgGenerators.Text(8)
            select (number, text);
        List<(int, string)> first = Collect(generator, 42, 64);
        Assert.HasCount(64, first);
        Assert.AreSequenceEqual(first, Collect(generator, 42, 64));
        Assert.AreNotEqual(string.Join(',', first), string.Join(',', Collect(generator, 43, 64)));
        PgPropertyException failure = Fail(PgGenerators.Number<int>(), static value => Check(value % 7 != 3));
        PgPropertyException replayed = Assert.ThrowsExactly<PgPropertyException>(() =>
            new PgPropertyRunner(new PgPropertyOptions { Seed = failure.Seed }).Run(PgGenerators.Number<int>(),
                static value => Check(value % 7 != 3), static action => action()));
        Assert.AreEqual(failure.Input, replayed.Input);
        Assert.AreEqual(failure.PassedCases, replayed.PassedCases);
    }

    /// <summary>
    /// Integers are a sign and a magnitude, positive first; ranges that exclude zero are an offset from the limit
    /// nearest it; and random draws stay in range while sometimes taking each limit.
    /// </summary>
    [TestMethod]
    public void IntegersAreSignAndMagnitude()
    {
        Assert.AreEqual(0L, Generate(PgGenerators.Number<long>()));
        Assert.AreEqual(5L, Generate(PgGenerators.Number<long>(), 0, 5));
        Assert.AreEqual(-5L, Generate(PgGenerators.Number<long>(), 1, 5));
        Assert.AreEqual(long.MinValue, Generate(PgGenerators.Number<long>(), 1, ulong.MaxValue));
        Assert.AreEqual(long.MaxValue, Generate(PgGenerators.Number<long>(), 0, ulong.MaxValue));
        Assert.AreEqual(ulong.MaxValue, Generate(PgGenerators.Number<ulong>(), ulong.MaxValue));
        Assert.AreEqual(-2, Generate(PgGenerators.Number(-2, 4), 1, 9));
        Assert.AreEqual(4, Generate(PgGenerators.Number(-2, 4), 0, 9));
        Assert.AreEqual(13, Generate(PgGenerators.Number(10, 20), 3));
        Assert.AreEqual(-13, Generate(PgGenerators.Number(-20, -10), 3));
        Assert.AreEqual(10, Generate(PgGenerators.Number(10, 20)));
        List<int> values = Collect(PgGenerators.Number(-3, 7), 7, 256);
        foreach (int value in values)
        {
            Assert.IsInRange(-3, 7, value);
        }

        List<int> wide = Collect(PgGenerators.Number<int>(), 8, 512);
        Assert.Contains(int.MinValue, wide);
        Assert.Contains(int.MaxValue, wide);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgGenerators.Number(5, 4));
        Assert.ThrowsExactly<NotSupportedException>(() => PgGenerators.Number<Int128>());
        Assert.ThrowsExactly<NotSupportedException>(() => PgGenerators.Number<UInt128>());
    }

    /// <summary>
    /// Generated text never holds NUL or an unpaired surrogate, and nullable generators produce null.
    /// </summary>
    [TestMethod]
    public void GeneratedValuesStayValid()
    {
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        List<string> texts = Collect(PgGenerators.Text(64), 9, 512);
        foreach (string text in texts)
        {
            Assert.DoesNotContain("\0", text, StringComparison.Ordinal);
            Assert.AreEqual(text, strict.GetString(strict.GetBytes(text)));
        }

        Assert.Contains(static text => text.Any(char.IsSurrogate), texts);
        List<int?> nullable = Collect(PgGenerators.Nullable(PgGenerators.Number<int>()), 11, 256);
        Assert.Contains(static value => value is null, nullable);
        Assert.Contains(static value => value is not null, nullable);
        Assert.IsNull(Generate(PgGenerators.NullableReference(PgGenerators.Text()), 1));
        Assert.AreEqual("b", Generate(PgGenerators.Element("a", "b", "c"), 1));
        Assert.AreEqual("a", Generate(PgGenerators.OneOf(PgGenerators.Constant("a"), PgGenerators.Constant("b"))));
        Assert.ThrowsExactly<ArgumentException>(() => PgGenerators.Element<int>());
    }

    /// <summary>
    /// A filter that rejects every value fails the run after the configured number of rejected cases.
    /// </summary>
    [TestMethod]
    public void TooManyRejectionsFailTheRun()
    {
        PgPropertyException error = Assert.ThrowsExactly<PgPropertyException>(() =>
            new PgPropertyRunner(new PgPropertyOptions { Seed = 1, MaxRejects = 3 })
                .Run(PgGenerators.Number<int>().Where(static _ => false), static _ => { }, static action => action()));
        Assert.StartsWith("Too many rejected cases: 4 cases were rejected after 0 passed (seed 1).", error.Message);
        Assert.AreEqual(string.Empty, error.Input);
        int even = 0;
        new PgPropertyRunner(new PgPropertyOptions { Seed = 2, Cases = 32 })
            .Run(PgGenerators.Number<int>().Where(static value => value % 2 == 0), value =>
            {
                Assert.AreEqual(0, value % 2);
                even++;
            }, static action => action());
        Assert.AreEqual(32, even);
    }

    /// <summary>
    /// Without shrinking runs, the first failing input is reported unchanged; query cancellation ends the run instead
    /// of failing an input; and invalid options are rejected.
    /// </summary>
    [TestMethod]
    public void LimitsAndCancellationAreHonored()
    {
        PgPropertyException unshrunk = Assert.ThrowsExactly<PgPropertyException>(() =>
            new PgPropertyRunner(new PgPropertyOptions { Seed = 5, MaxShrinkRuns = 0 })
                .Run(PgGenerators.Number<int>(), static value => Check(value < 1_000), static action => action()));
        Assert.AreEqual(0, unshrunk.ShrinkRuns);
        Assert.IsGreaterThanOrEqualTo(1_000, int.Parse(unshrunk.Input, System.Globalization.CultureInfo.InvariantCulture));
        PgException canceled = Assert.ThrowsExactly<PgException>(() =>
            new PgPropertyRunner(new PgPropertyOptions { Seed = 5 }).Run(PgGenerators.Number<int>(),
                static _ => throw new PgException(PgSqlStates.QueryCanceled, "canceling statement due to user request"),
                static action => action()));
        Assert.AreEqual(PgSqlStates.QueryCanceled, canceled.SqlState);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PgPropertyRunner(new PgPropertyOptions { Cases = 0 }).Run(PgGenerators.Boolean(), static _ => { }, static action => action()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PgPropertyRunner(new PgPropertyOptions { MaxShrinkRuns = -1 }).Run(PgGenerators.Boolean(), static _ => { }, static action => action()));
    }

    /// <summary>
    /// Failure reports quote and escape text, bracket sequences, parenthesize tuples and format numbers invariantly.
    /// </summary>
    [TestMethod]
    public void InputsAreDescribedUnambiguously()
    {
        Assert.AreEqual("null", PgPropertyRunner.Describe(null));
        Assert.AreEqual("\"a\\\"b\\\\c\\n\\u0001\"", PgPropertyRunner.Describe("a\"b\\c\n\u0001"));
        Assert.AreEqual("'x'", PgPropertyRunner.Describe('x'));
        Assert.AreEqual("[1, null, 3]", PgPropertyRunner.Describe(new int?[] { 1, null, 3 }));
        Assert.AreEqual("(1.5, \"t\")", PgPropertyRunner.Describe((1.5, "t")));
        Assert.AreEqual("-0", PgPropertyRunner.Describe(-0.0));
    }

    private static PgPropertyException Fail<T>(PgGenerator<T> generator, Action<T> test)
        => Assert.ThrowsExactly<PgPropertyException>(() =>
            new PgPropertyRunner(new PgPropertyOptions { Seed = 20261009 }).Run(generator, test, static action => action()));

    private static T Generate<T>(PgGenerator<T> generator, params ulong[] choices) => generator.Generate(PgPropertySource.Replay(choices));

    private static List<T> Collect<T>(PgGenerator<T> generator, ulong seed, int cases)
    {
        var values = new List<T>();
        new PgPropertyRunner(new PgPropertyOptions { Seed = seed, Cases = cases }).Run(generator, values.Add, static action => action());
        return values;
    }

    private static void Check(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("The property does not hold.");
        }
    }
}

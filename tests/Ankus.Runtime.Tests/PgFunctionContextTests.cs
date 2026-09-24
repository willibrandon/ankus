namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies owned call metadata and immutable argument ordering independently of native execution.
/// </summary>
[TestClass]
public sealed class PgFunctionContextTests
{
    /// <summary>
    /// Metadata survives the backend binding; raw values still enforce their separate native lifetime.
    /// </summary>
    [TestMethod]
    public void MetadataRemainsOwnedAndArgumentsCannotBeReplaced()
    {
        using var fixture = new MemoryContextTestFixture();
        PgFunctionContext call;
        using (MemoryContextTestFixture.Enter())
        {
            PgDatum first = PgDatum.DangerousCreate(0, 23, PgMemoryContext.Current);
            PgDatum second = PgDatum.DangerousCreate(0, 25, PgMemoryContext.Current, isNull: true);
            call = new PgFunctionContext(12345, 25, 100, [first, second]);
            Assert.AreSame(first, call.Arguments[0]);
            Assert.AreSame(second, call.Arguments[1]);
            IList<PgDatum> mutable = Assert.IsInstanceOfType<IList<PgDatum>>(call.Arguments);
            Assert.ThrowsExactly<NotSupportedException>(() => mutable[0] = second);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => call.Arguments[-1]);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => call.Arguments[2]);
        }

        Assert.AreEqual(12345U, call.FunctionOid);
        Assert.AreEqual(25U, call.ResultTypeOid);
        Assert.AreEqual(100U, call.CollationOid);
        Assert.AreSequenceEqual([23U, 25U], call.Arguments.Select(static value => value.TypeOid));
        Assert.IsFalse(call.Arguments[0].IsNull);
        Assert.IsTrue(call.Arguments[1].IsNull);
        Assert.ThrowsExactly<InvalidOperationException>(() => call.Arguments[0].DangerousGetBits());
    }
}

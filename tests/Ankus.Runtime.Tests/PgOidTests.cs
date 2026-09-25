using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies complete versioned constant catalogs, unsigned conversion boundaries and tagged OID identity.
/// </summary>
[TestClass]
public sealed class PgOidTests
{
    /// <summary>
    /// Matches every selected native name and numeric value to independently captured reference manifests.
    /// </summary>
    /// <param name="major">The source PostgreSQL major.</param>
    /// <param name="count">The exact source enum size.</param>
    /// <param name="hash">SHA256 of sorted native-name=value lines from the pinned pgrx source, including final newlines.</param>
    [TestMethod]
    [DataRow(13, 263, "8FFE0D0326CDC3915617F774EF9250DECE3B082E1BF2A6D93617E89672A903D7")]
    [DataRow(14, 292, "2A31F4C8749C75CC6F2DFF4B072D48B7ECD6AC2A92600A0B4D83C594ACB33B99")]
    [DataRow(15, 292, "1898211C84F3F04CED37F80ED7DC6A49A17C1532FAA8BD21415E7E93DE24D156")]
    [DataRow(16, 292, "1898211C84F3F04CED37F80ED7DC6A49A17C1532FAA8BD21415E7E93DE24D156")]
    [DataRow(17, 292, "1898211C84F3F04CED37F80ED7DC6A49A17C1532FAA8BD21415E7E93DE24D156")]
    [DataRow(18, 291, "F93EC9CDE08558E0505BEF6EF7E251BC3E6874758637B22856A862D66A6DB3A6")]
    [DataRow(19, 296, "3C7FCF0265D299EC10DD85E60AA0242645C0A30830E87D390A1F6D471F40CB77")]
    public void ReferenceCatalogsMatch(int major, int count, string hash)
    {
        IReadOnlyList<PgBuiltInOid> values = PgBuiltInOids.GetValues(major);
        Assert.HasCount(count, values);
        Assert.AreSequenceEqual(values.OrderBy(static value => (uint)value), values);
        Assert.HasCount(count, values.Distinct());
        string manifest = string.Concat(values.Select(value => $"{PgBuiltInOids.GetNativeName(value, major)}={((uint)value).ToString(CultureInfo.InvariantCulture)}\n")
            .Order(StringComparer.Ordinal));
        Assert.AreEqual(hash, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))));
        foreach (PgBuiltInOid value in values)
        {
            Assert.IsTrue(PgBuiltInOids.TryFromValue((uint)value, major, out PgBuiltInOid converted, out PgOidLookupError error));
            Assert.AreEqual(value, converted);
            Assert.AreEqual(PgOidLookupError.None, error);
            PgOid tagged = PgOid.FromValue((uint)value, major);
            Assert.AreEqual(PgOidKind.BuiltIn, tagged.Kind);
            Assert.AreEqual((uint)value, tagged.Value);
            Assert.AreEqual(value, tagged.BuiltIn);
            Assert.AreEqual(tagged, PgOid.FromBuiltIn(value, major));
        }

        var members = new HashSet<uint>(values.Select(static value => (uint)value));
        for (uint value = 1; value <= 65536; value++)
        {
            if (members.Contains(value))
            {
                continue;
            }

            Assert.IsFalse(PgBuiltInOids.TryFromValue(value, major, out PgBuiltInOid converted, out PgOidLookupError error));
            Assert.AreEqual(0U, (uint)converted);
            Assert.AreEqual(PgOidLookupError.Ambiguous, error);
            Assert.AreEqual(PgOid.Custom(value), PgOid.FromValue(value, major));
        }
    }

    /// <summary>
    /// Renames preserve numeric identity while selected native spelling and membership change at version boundaries.
    /// </summary>
    [TestMethod]
    public void RenamesAndVersionBoundariesRemainDistinct()
    {
        Assert.AreEqual(790U, (uint)PgBuiltInOid.MoneyOid);
        Assert.AreEqual("CASHOID", PgBuiltInOids.GetNativeName(PgBuiltInOid.MoneyOid, 13));
        Assert.AreEqual("MONEYOID", PgBuiltInOids.GetNativeName(PgBuiltInOid.MoneyOid, 14));
        Assert.AreEqual("PGNODETREEOID", PgBuiltInOids.GetNativeName(PgBuiltInOid.PgNodeTreeOid, 13));
        Assert.AreEqual("PG_NODE_TREEOID", PgBuiltInOids.GetNativeName(PgBuiltInOid.PgNodeTreeOid, 19));
        Assert.AreEqual("F_I8TOOID", PgBuiltInOids.GetNativeName(PgBuiltInOid.FunctionOid, 13));
        Assert.AreEqual("F_OID", PgBuiltInOids.GetNativeName(PgBuiltInOid.FunctionOid, 14));
        Assert.AreEqual("TemplateDbOid", PgBuiltInOids.GetNativeName(PgBuiltInOid.TemplateDbOid, 14));
        Assert.IsNull(PgBuiltInOids.GetNativeName(PgBuiltInOid.TemplateDbOid, 15));
        Assert.IsNull(PgBuiltInOids.GetNativeName(PgBuiltInOid.Int4MultirangeOid, 13));
        Assert.AreEqual("INT4MULTIRANGEOID", PgBuiltInOids.GetNativeName(PgBuiltInOid.Int4MultirangeOid, 14));
        Assert.AreEqual("POSIX_COLLATION_OID", PgBuiltInOids.GetNativeName(PgBuiltInOid.PosixCollationOid, 17));
        Assert.IsNull(PgBuiltInOids.GetNativeName(PgBuiltInOid.PosixCollationOid, 18));
        Assert.IsNull(PgBuiltInOids.GetNativeName(PgBuiltInOid.Oid8Oid, 18));
        Assert.AreEqual("OID8OID", PgBuiltInOids.GetNativeName(PgBuiltInOid.Oid8Oid, 19));
        Assert.AreEqual(6437U, (uint)PgBuiltInOid.Oid8Oid);
        Assert.AreEqual(6490U, (uint)PgBuiltInOid.RegDatabaseOid);
        Assert.AreEqual(PgOidKind.Custom, PgOid.FromValue(4451, 13).Kind);
        Assert.AreEqual(PgOidKind.BuiltIn, PgOid.FromValue(4451, 14).Kind);
        Assert.AreEqual(PgOidKind.Custom, PgOid.FromValue(951, 18).Kind);
        Assert.AreEqual(PgOidKind.BuiltIn, PgOid.FromValue(951, 17).Kind);
        Assert.AreEqual("PROGRESS_CREATEIDX_INDEX_OID", PgBuiltInOids.GetNativeName(PgBuiltInOid.ProgressCreateidxIndexOid, 18));
        Assert.AreEqual(6U, (uint)PgBuiltInOid.ProgressCreateidxIndexOid);
        Assert.AreEqual(2278U, (uint)PgBuiltInOid.VoidOid);
    }

    /// <summary>
    /// Zero, unlisted unsigned values and out-of-range datum words fail for distinct reasons without truncation.
    /// </summary>
    /// <param name="value">The complete unsigned input.</param>
    /// <param name="expected">The expected error partition.</param>
    [TestMethod]
    [DataRow(0UL, PgOidLookupError.Invalid)]
    [DataRow(15UL, PgOidLookupError.Ambiguous)]
    [DataRow(65536UL, PgOidLookupError.Ambiguous)]
    [DataRow(4294967294UL, PgOidLookupError.Ambiguous)]
    [DataRow(4294967295UL, PgOidLookupError.Ambiguous)]
    [DataRow(4294967296UL, PgOidLookupError.TooBig)]
    [DataRow(4294967319UL, PgOidLookupError.TooBig)]
    [DataRow(ulong.MaxValue, PgOidLookupError.TooBig)]
    public void ConversionRejectsInvalidAmbiguousAndOversizedValues(ulong value, PgOidLookupError expected)
    {
        Assert.IsFalse(PgBuiltInOids.TryFromValue(value, 18, out PgBuiltInOid result, out PgOidLookupError error));
        Assert.AreEqual(expected, error);
        Assert.AreEqual(0U, (uint)result);
    }

    /// <summary>
    /// Explicit custom tags retain zero and known numeric values; equal variants work as collection keys.
    /// </summary>
    [TestMethod]
    public void TaggedIdentityPreservesCustomValues()
    {
        PgOid invalid = PgOid.FromValue(0, 18);
        Assert.AreEqual(default, invalid);
        Assert.AreEqual(PgOid.Invalid, invalid);
        Assert.AreEqual(0U, invalid.Value);
        Assert.AreEqual(PgOidKind.Invalid, invalid.Kind);
        Assert.IsNull(invalid.BuiltIn);
        Assert.AreEqual("0", invalid.ToString());
        PgOid known = PgOid.FromBuiltIn(PgBuiltInOid.Int4Oid, 18);
        PgOid custom = PgOid.Custom(23);
        Assert.AreEqual(PgOidKind.Custom, custom.Kind);
        Assert.AreEqual(23U, custom.Value);
        Assert.IsNull(custom.BuiltIn);
        Assert.AreNotEqual(known, custom);
        Assert.IsTrue(known != custom);
        Assert.IsFalse(known == custom);
        Assert.AreNotEqual(invalid, PgOid.Custom(0));
        var keys = new HashSet<PgOid> { invalid, PgOid.Custom(0), known, custom };
        Assert.HasCount(4, keys);
        Assert.Contains(PgOid.FromValue(23, 13), keys);
        Assert.AreEqual(known.GetHashCode(), PgOid.FromValue(23, 19).GetHashCode());
        PgOid maximum = PgOid.FromValue(uint.MaxValue, 19);
        Assert.AreEqual(PgOid.Custom(uint.MaxValue), maximum);
        Assert.AreEqual(uint.MaxValue, maximum.Value);
        Assert.AreEqual("4294967295", maximum.ToString());
    }

    /// <summary>
    /// Undefined enum values and unavailable members cannot create a built-in tag.
    /// </summary>
    [TestMethod]
    public void BuiltInConstructionValidatesMembership()
    {
        foreach (PgBuiltInOid value in new[] { (PgBuiltInOid)0, (PgBuiltInOid)uint.MaxValue, PgBuiltInOid.Int4MultirangeOid })
        {
            Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgOid.FromBuiltIn(value, 13)).ParamName);
            Assert.IsNull(PgBuiltInOids.GetNativeName(value, 13));
        }

        Assert.AreEqual(16U, PgOid.FromBuiltIn(PgBuiltInOid.BoolOid, 13).Value);
        Assert.AreEqual(6491U, PgOid.FromBuiltIn(PgBuiltInOid.RegDatabaseArrayOid, 19).Value);
    }

    /// <summary>
    /// Every explicit-version entry point rejects unsupported majors before classification.
    /// </summary>
    /// <param name="major">The unsupported version.</param>
    [TestMethod]
    [DataRow(int.MinValue)]
    [DataRow(0)]
    [DataRow(12)]
    [DataRow(20)]
    [DataRow(int.MaxValue)]
    public void UnsupportedVersionsAreRejected(int major)
    {
        Assert.AreEqual("postgresMajor", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgBuiltInOids.GetValues(major)).ParamName);
        Assert.AreEqual("postgresMajor", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgBuiltInOids.GetNativeName(PgBuiltInOid.BoolOid, major)).ParamName);
        Assert.AreEqual("postgresMajor", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgBuiltInOids.TryFromValue(0, major, out _, out _)).ParamName);
        Assert.AreEqual("postgresMajor", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgOid.FromValue(0, major)).ParamName);
        Assert.AreEqual("postgresMajor", Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PgOid.FromBuiltIn(PgBuiltInOid.BoolOid, major)).ParamName);
    }

    /// <summary>
    /// Catalog snapshots cannot be modified and active-version overloads require a backend binding.
    /// </summary>
    [TestMethod]
    public void SnapshotsAreImmutableAndActiveOverloadsRequireBackend()
    {
        IReadOnlyList<PgBuiltInOid> values = PgBuiltInOids.GetValues(18);
        var mutable = (IList<PgBuiltInOid>)values;
        Assert.ThrowsExactly<NotSupportedException>(() => mutable[0] = PgBuiltInOid.BoolOid);
        Assert.AreEqual(PgBuiltInOid.HeapTableAmOid, PgBuiltInOids.GetValues(18)[0]);
        Assert.ThrowsExactly<InvalidOperationException>(() => PgBuiltInOids.GetValues());
        Assert.ThrowsExactly<InvalidOperationException>(() => PgBuiltInOids.GetNativeName(PgBuiltInOid.BoolOid));
        Assert.ThrowsExactly<InvalidOperationException>(() => PgBuiltInOids.TryFromValue(16, out _, out _));
        Assert.ThrowsExactly<InvalidOperationException>(() => PgOid.FromValue(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => PgOid.FromBuiltIn(PgBuiltInOid.BoolOid));
    }

    /// <summary>
    /// Invalid maps to typed NULL while custom zero and maximum values retain their bits and explicit owner.
    /// </summary>
    [TestMethod]
    public void DatumConversionPreservesTagNullAndLifetime()
    {
        Assert.AreEqual("context", Assert.ThrowsExactly<ArgumentNullException>(() => PgOid.Invalid.ToDatum(null!)).ParamName);
        using var fixture = new MemoryContextTestFixture();
        using MemoryContextTestFixture.Scope scope = MemoryContextTestFixture.Enter();
        PgMemoryContext context = PgMemoryContext.Current;
        (PgOid Oid, uint Bits, bool Null)[] cases =
        [
            (PgOid.Invalid, 0, true), (PgOid.Custom(0), 0, false),
            (PgOid.Custom(uint.MaxValue), uint.MaxValue, false), (PgOid.FromBuiltIn(PgBuiltInOid.Int4Oid, 18), 23, false),
        ];
        foreach ((PgOid oid, uint bits, bool isNull) in cases)
        {
            PgDatum datum = oid.ToDatum(context);
            Assert.AreEqual(26U, datum.TypeOid);
            Assert.AreEqual<nuint>(bits, datum.DangerousGetBits());
            Assert.AreEqual(isNull, datum.IsNull);
            SpiParameter parameter = SpiParameter.Create(datum);
            Assert.AreEqual(26U, parameter.TypeOid);
            NativeValue transport = SpiType.ToNative(parameter.Value);
            try
            {
                Assert.AreEqual(isNull ? (byte)1 : (byte)0, transport.IsNull);
                NativeDatumReference reference = System.Runtime.InteropServices.MemoryMarshal.Read<NativeDatumReference>(transport.ReadBytes());
                Assert.AreEqual<nuint>(bits, reference._bits);
                Assert.AreEqual(101, reference._context);
                Assert.AreEqual((nuint)901, reference._generation);
            }
            finally
            {
                transport.Release();
            }
        }

        PgDatum stale = PgOid.Custom(0).ToDatum(context);
        fixture.Handler = request => request._operation == NativeMemoryOperation.CaptureGeneration
            ? new NativeMemoryResult { _value = 902 }
            : fixture.Respond(request);
        Assert.ThrowsExactly<ObjectDisposedException>(() => stale.DangerousGetBits());
    }
}

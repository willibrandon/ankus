using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

/// <summary>
/// Verifies finite closed constructions of a type-level datum mapping.
/// </summary>
public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Distinct requested constructions retain independent exact registrations and mapped callback conversions.
    /// </summary>
    [TestMethod]
    public void GenericDatumMappingsRegisterOnlySelectedClosedRoots()
    {
        Compilation compilation = GenerateSqlControl(GenericBoxSource + """
            public static class Functions
            {
                [Ankus.PgFunction] public static Box<int> EchoInt(Box<int> value) => value;
                [Ankus.PgFunction] public static Box<long> EchoLong(Box<long> value) => value;
                [Ankus.PgFunction] public static Box<int>? EchoOptional(Box<int>? value) => value;
                [Ankus.PgFunction] public static Box<int>[] EchoArray(Box<int>[] value) => value;
            }
            """);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("RegisterValue<global::Box<int>>(\"int4\", \"pg_catalog\"", managed);
        Assert.Contains("RegisterValue<global::Box<long>>(\"int4\", \"pg_catalog\"", managed);
        Assert.DoesNotContain("RegisterValue<global::Box<T>>", managed);
        Assert.AreEqual(2, managed.Split("global::Ankus.PgDatumRegistry.RegisterValue<global::Box<", StringSplitOptions.None).Length - 1);
        Assert.Contains("ReadMapped<global::Box<int>>()", managed);
        Assert.Contains("ReadMapped<global::Box<long>>()", managed);
        Assert.Contains("FromMapped<global::Box<int>>(", managed);
        Assert.Contains("FromMapped<global::Box<long>>(", managed);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("\"pg_catalog\".\"int4\"[]", sql);
        Assert.DoesNotContain("CREATE TYPE", sql);
    }

    /// <summary>
    /// A generic-containing nested root preserves every constructed containing argument.
    /// </summary>
    [TestMethod]
    public void GenericDatumMappingsPreserveConstructedContainingIdentities()
    {
        Compilation compilation = GenerateSqlControl("""
            public class Outer<T>
            {
                [Ankus.PgDatumType("int4", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
                public readonly record struct Value(int Word);
            }
            public sealed class Converter : Ankus.IPgDatumReader<Outer<int>.Value>, Ankus.IPgDatumReader<Outer<long>.Value>
            {
                Outer<int>.Value Ankus.IPgDatumReader<Outer<int>.Value>.Read(Ankus.PgDatum value) => new(17);
                Outer<long>.Value Ankus.IPgDatumReader<Outer<long>.Value>.Read(Ankus.PgDatum value) => new(19);
            }
            public static class Functions
            {
                [Ankus.PgFunction] public static int ReadInt(Outer<int>.Value value) => value.Word;
                [Ankus.PgFunction] public static int ReadLong(Outer<long>.Value value) => value.Word;
            }
            """);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("RegisterValue<global::Outer<int>.Value>", managed);
        Assert.Contains("RegisterValue<global::Outer<long>.Value>", managed);
        Assert.DoesNotContain("RegisterValue<global::Outer<T>.Value>", managed);
        Assert.AreEqual(2, managed.Split("global::Ankus.PgDatumRegistry.RegisterValue<global::Outer<", StringSplitOptions.None).Length - 1);
        Assert.Contains("ReadMapped<global::Outer<int>.Value>()", managed);
        Assert.Contains("ReadMapped<global::Outer<long>.Value>()", managed);
    }

    /// <summary>
    /// An unused generic definition is only a template and never creates an unbound registration.
    /// </summary>
    [TestMethod]
    public void GenericDatumMappingsDoNotRegisterUnusedDefinitions()
    {
        Compilation compilation = GenerateSqlControl("""
            [Ankus.PgDatumType("int4", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            [Ankus.PgEquality]
            public readonly record struct Box<T>(int Word);
            public sealed class Converter { }
            """);
        Assert.DoesNotContain("RegisterValue<global::Box<", DatumMappingManaged(compilation));
        Assert.DoesNotContain("CREATE TYPE", ManifestValue(compilation, "Ankus.Sql"));
        Assert.DoesNotContain("CREATE OPERATOR", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// Open declarations without a mapping retain the existing invalid derived-operator diagnostic.
    /// </summary>
    [TestMethod]
    public void GenericDatumMappingsPreserveUnmappedDeriveDiagnostics()
        => AssertDatumMappingError("[Ankus.PgEquality] public readonly record struct Value<T>(int Word);",
            "ANKUS018", "require a valid, accessible PgType, PgEnum or PgDatumType");

    /// <summary>
    /// A selected closure must find its exact converter interface instead of borrowing a sibling's implementation.
    /// </summary>
    [TestMethod]
    public void GenericDatumMappingsRejectUnimplementedClosedInterface()
        => AssertDatumMappingError("""
            [Ankus.PgDatumType("int4", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public readonly record struct Box<T>(int Word);
            public sealed class Converter : Ankus.IPgDatumReader<Box<int>>
            {
                public Box<int> Read(Ankus.PgDatum value) => new(17);
            }
            public static class Functions
            {
                [Ankus.PgFunction] public static int Read(Box<long> value) => value.Word;
            }
            """, "ANKUS019", "exact non-nullable managed type");

    /// <summary>
    /// Each selected closure needs the conversion direction used by its generated callback slot.
    /// </summary>
    /// <param name="read">Whether the selected function consumes the mapped SQL input.</param>
    /// <param name="reason">The expected direction diagnostic.</param>
    [TestMethod]
    [DataRow(true, "reading SQL arguments")]
    [DataRow(false, "writing SQL results")]
    public void GenericDatumMappingsRejectMissingSelectedDirection(bool read, string reason)
        => AssertDatumMappingError("""
            [Ankus.PgDatumType("int4", typeof(Converter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
            public readonly record struct Box<T>(int Word);
            public sealed class Converter :
            """ + (read ? "Ankus.IPgDatumWriter<Box<int>>" : "Ankus.IPgDatumReader<Box<int>>") + """
            {
            """ + (read ? """
                Ankus.PgDatum Ankus.IPgDatumWriter<Box<int>>.Write(Box<int> value, uint typeOid, Ankus.PgMemoryContext destination)
                    => throw new System.InvalidOperationException();
                """ : """
                Box<int> Ankus.IPgDatumReader<Box<int>>.Read(Ankus.PgDatum value) => new(17);
                """) + """
            }
            public static class Functions
            {
            """ + (read ? "[Ankus.PgFunction] public static int Read(Box<int> value) => value.Word;" :
                "[Ankus.PgFunction] public static Box<int> Write(int value) => new(value);") + "\n}", "ANKUS019", reason);

    /// <summary>
    /// Two exact owned closures can depend on one completed manual SQL type without duplicating it.
    /// </summary>
    [TestMethod]
    public void GenericDatumMappingsRequireExactCompletedProviders()
    {
        Compilation compilation = GenerateSqlControl("""
            using Ankus;
            [assembly: PgSql("generic-key", "CREATE TYPE generic_key; CREATE FUNCTION generic_key_in(cstring) RETURNS generic_key LANGUAGE internal IMMUTABLE STRICT AS 'int4in'; CREATE FUNCTION generic_key_out(generic_key) RETURNS cstring LANGUAGE internal IMMUTABLE STRICT AS 'int4out'; CREATE TYPE generic_key(INPUT=generic_key_in,OUTPUT=generic_key_out,LIKE=int4);")]
            [assembly: PgSqlTypeProvider("generic-key", typeof(Box<int>))]
            [assembly: PgSqlTypeProvider("generic-key", typeof(Box<long>))]
            [PgDatumType("generic_key", typeof(Converter))]
            public readonly record struct Box<T>(int Word);
            public sealed class Converter : IPgDatumReader<Box<int>>, IPgDatumReader<Box<long>>
            {
                Box<int> IPgDatumReader<Box<int>>.Read(PgDatum value) => new(17);
                Box<long> IPgDatumReader<Box<long>>.Read(PgDatum value) => new(19);
            }
            public static class Functions
            {
                [PgFunction] public static int ReadInt(Box<int> value) => value.Word;
                [PgFunction] public static int ReadLong(Box<long> value) => value.Word;
            }
            """);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.AreEqual(1, sql.Split("CREATE TYPE generic_key;", StringSplitOptions.None).Length - 1);
        int completed = sql.IndexOf("CREATE TYPE generic_key(INPUT=", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, completed);
        Assert.IsGreaterThan(completed, sql.IndexOf("CREATE FUNCTION \"read_int\"", StringComparison.Ordinal));
        Assert.IsGreaterThan(completed, sql.IndexOf("CREATE FUNCTION \"read_long\"", StringComparison.Ordinal));
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("RegisterValue<global::Box<int>>(\"generic_key\", null", managed);
        Assert.Contains("RegisterValue<global::Box<long>>(\"generic_key\", null", managed);
    }

    /// <summary>
    /// An exact managed provider selects a finite registration even when no generated callback uses it.
    /// </summary>
    [TestMethod]
    public void GenericDatumMappingsRegisterProviderOnlyRoots()
    {
        Compilation compilation = GenerateSqlControl("""
            using Ankus;
            [assembly: PgSql("generic-key", "CREATE DOMAIN generic_key AS integer;", Relocatable=true)]
            [assembly: PgSqlTypeProvider("generic-key", typeof(Box<int>))]
            [PgDatumType("generic_key", typeof(Converter))]
            public readonly record struct Box<T>(int Word);
            public sealed class Converter : IPgDatumReader<Box<int>>
            {
                public Box<int> Read(PgDatum value) => new(17);
            }
            """);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("RegisterValue<global::Box<int>>(\"generic_key\", null", managed);
        Assert.DoesNotContain("RegisterValue<global::Box<T>>", managed);
        Assert.DoesNotContain("CREATE FUNCTION", ManifestValue(compilation, "Ankus.Sql"));
        Assert.Contains("CREATE DOMAIN generic_key AS integer;", ManifestValue(compilation, "Ankus.Sql"));
    }

    /// <summary>
    /// A missing provider on one selected closure rejects the whole generated extension.
    /// </summary>
    [TestMethod]
    public void GenericDatumMappingsRejectMissingExactProvider()
        => AssertDatumMappingError("""
            using Ankus;
            [assembly: PgSql("generic-key", "CREATE TYPE generic_key;")]
            [assembly: PgSqlTypeProvider("generic-key", typeof(Box<int>))]
            [PgDatumType("generic_key", typeof(Converter))]
            public readonly record struct Box<T>(int Word);
            public sealed class Converter : IPgDatumReader<Box<int>>, IPgDatumReader<Box<long>>
            {
                Box<int> IPgDatumReader<Box<int>>.Read(PgDatum value) => new(17);
                Box<long> IPgDatumReader<Box<long>>.Read(PgDatum value) => new(19);
            }
            public static class Functions
            {
                [PgFunction] public static int ReadLong(Box<long> value) => value.Word;
            }
            """, "ANKUS005", "requires a PgSqlTypeProvider");

    /// <summary>
    /// A finite selected generic derive emits only closed read-only helpers and their native input route.
    /// </summary>
    [TestMethod]
    public void GenericDatumMappingsGenerateClosedDerivedFamilies()
    {
        Compilation compilation = GenerateSqlControl("""
            using Ankus;
            [PgDatumType("int4", typeof(Converter), Origin=PgTypeOrigin.External, Schema="pg_catalog")]
            [PgEquality][PgOrdering][PgHashing]
            public readonly record struct Box<T>(int Word) : System.IComparable<Box<T>>, IPgHashable
            {
                public bool Equals(Box<T> other) => Word/10 == other.Word/10;
                public int CompareTo(Box<T> other) => Word/10 > other.Word/10 ? -1 : Word/10 < other.Word/10 ? 1 : 0;
                public int GetPostgresHashCode() => 12345 + Word/10;
                public override int GetHashCode() => GetPostgresHashCode();
            }
            public sealed class Converter : IPgDatumReader<Box<int>>
            {
                public Box<int> Read(PgDatum value) => new(17);
            }
            public static class Functions
            {
                [PgFunction] public static int Read(Box<int> value) => value.Word;
            }
            """);
        string managed = DatumMappingManaged(compilation);
        Assert.Contains("ReadMapped<global::Box<int>>()", managed);
        Assert.DoesNotContain("ReadMapped<global::Box<T>>()", managed);
        Assert.DoesNotContain("FromMapped<global::Box<int>>(", managed);
        string sql = ManifestValue(compilation, "Ankus.Sql");
        Assert.Contains("CREATE OPERATOR CLASS", sql);
        Assert.Contains("USING btree", sql);
        Assert.Contains("USING hash", sql);
        Assert.AreEqual("false", ManifestValue(compilation, "Ankus.Relocatable"));
        Assert.Contains("ankus_read_polymorphic(fcinfo, 0", ManifestValue(compilation, "Ankus.NativeSource"));
    }

    /// <summary>
    /// Distinct closed derives over one fixed SQL identity cannot both own its generated functions and classes.
    /// </summary>
    [TestMethod]
    public void GenericDatumMappingsRejectDerivedSqlIdentityCollisions()
        => AssertDatumMappingError("""
            using Ankus;
            [PgDatumType("int4", typeof(Converter), Origin=PgTypeOrigin.External, Schema="pg_catalog")]
            [PgEquality]
            public readonly record struct Box<T>(int Word);
            public sealed class Converter : IPgDatumReader<Box<int>>, IPgDatumReader<Box<long>>
            {
                Box<int> IPgDatumReader<Box<int>>.Read(PgDatum value) => new(17);
                Box<long> IPgDatumReader<Box<long>>.Read(PgDatum value) => new(19);
            }
            public static class Functions
            {
                [PgFunction] public static int ReadInt(Box<int> value) => value.Word;
                [PgFunction] public static int ReadLong(Box<long> value) => value.Word;
            }
            """, "ANKUS005", "Duplicate PostgreSQL");

    /// <summary>
    /// Provides two independent closed managed identities over one external SQL type.
    /// </summary>
    private const string GenericBoxSource = """
        [Ankus.PgDatumType("int4", typeof(BoxConverter), Origin=Ankus.PgTypeOrigin.External, Schema="pg_catalog")]
        public readonly record struct Box<T>(int Word);
        public sealed class BoxConverter : Ankus.IPgDatumReader<Box<int>>, Ankus.IPgDatumWriter<Box<int>>,
            Ankus.IPgDatumReader<Box<long>>, Ankus.IPgDatumWriter<Box<long>>
        {
            Box<int> Ankus.IPgDatumReader<Box<int>>.Read(Ankus.PgDatum value) => new(17);
            Box<long> Ankus.IPgDatumReader<Box<long>>.Read(Ankus.PgDatum value) => new(19);
            Ankus.PgDatum Ankus.IPgDatumWriter<Box<int>>.Write(Box<int> value, uint typeOid, Ankus.PgMemoryContext destination)
                => throw new System.InvalidOperationException();
            Ankus.PgDatum Ankus.IPgDatumWriter<Box<long>>.Write(Box<long> value, uint typeOid, Ankus.PgMemoryContext destination)
                => throw new System.InvalidOperationException();
        }
        """;
}

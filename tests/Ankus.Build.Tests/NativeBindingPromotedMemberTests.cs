namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    private const string PromotedMemberHeaders = """
        #include <stdint.h>
        typedef struct Cell { int32_t value; } Cell;
        #pragma pack(push, 2)
        typedef struct Promoted {
            uint8_t marker;
            union {
                struct { int32_t value; const uint16_t fixed_value; volatile uint16_t watched; };
                uint64_t combined;
            };
            struct {
                Cell cells[2][3];
                unsigned int before : 3;
                signed int delta : 5;
                unsigned int after : 4;
            };
            struct { int32_t count; } left;
            struct { int32_t count; } right;
            struct { int32_t count; uint8_t tail[]; };
        } Promoted;
        #pragma pack(pop)
        extern Promoted observed;
        """;

    /// <summary>
    /// Nested promoted members retain independent native checks for qualifiers, arrays, packing, bitfields and trailing storage.
    /// </summary>
    [TestMethod]
    public async Task NativeRecordChecksPreservePromotedMembers()
        => await VerifyRecordChecksAsync(PromotedMemberHeaders, PromotedMemberHeaders,
            [new("observed", "observed", false)], compile: true, execute: true);

    /// <summary>
    /// Equal-size member substitutions, qualifier changes and array reshaping cannot bypass promoted native contracts.
    /// </summary>
    /// <param name="original">The original native declaration.</param>
    /// <param name="replacement">The incompatible declaration.</param>
    /// <param name="reason">The expected independent compiler failure.</param>
    [TestMethod]
    [DataRow("int32_t value; const", "float value; const", "member type Promoted.value")]
    [DataRow("const uint16_t fixed_value", "uint16_t fixed_value", "member type Promoted.fixed_value")]
    [DataRow("volatile uint16_t watched", "uint16_t watched", "member type Promoted.watched")]
    [DataRow("Cell cells[2][3]", "Cell cells[3][2]", "member type Promoted.cells")]
    [DataRow("int32_t value; const uint16_t fixed_value;", "const uint16_t fixed_value; int32_t value;", "offset Promoted.value")]
    [DataRow("#pragma pack(push, 2)", "#pragma pack(push, 1)", "alignment Promoted")]
    public async Task NativeRecordChecksRejectChangedPromotedMembers(string original, string replacement, string reason)
    {
        string changed = PromotedMemberHeaders.Replace(original, replacement, StringComparison.Ordinal);
        Assert.AreNotEqual(PromotedMemberHeaders, changed);
        string diagnostics = await VerifyRecordChecksAsync(PromotedMemberHeaders, changed,
            [new("observed", "observed", false)], compile: false, execute: false);
        Assert.Contains("Native record contract changed: " + reason, diagnostics);
        await VerifyRecordChecksAsync(PromotedMemberHeaders, PromotedMemberHeaders,
            [new("observed", "observed", false)], compile: true, execute: true);
    }

    /// <summary>
    /// Promoted bitfields are verified using complete native parent storage rather than a synthetic anonymous type.
    /// </summary>
    /// <param name="replacement">The incompatible physical bitfield definition.</param>
    [TestMethod]
    [DataRow("unsigned int before : 2; signed int delta : 5; unsigned int after : 5;")]
    [DataRow("unsigned int before : 3; signed int delta : 4; unsigned int after : 5;")]
    [DataRow("unsigned int before : 3; signed int delta : 6; unsigned int after : 3;")]
    [DataRow("unsigned int before : 3; unsigned int delta : 5; unsigned int after : 4;")]
    public async Task NativeRecordChecksRejectChangedPromotedBitfields(string replacement)
    {
        const string Original = "unsigned int before : 3; signed int delta : 5; unsigned int after : 4;";
        const string Headers = "struct Parent { uint32_t marker; union { struct { " + Original +
            " }; uint32_t combined; }; }; extern struct Parent observed;";
        string headers = "#include <stdint.h>\n" + Headers;
        string changed = headers.Replace(Original, replacement, StringComparison.Ordinal);
        string diagnostics = await VerifyRecordChecksAsync(headers, changed, [new("observed", "observed", false)], compile: true, execute: false);
        Assert.Contains("Native record bitfield changed: Parent.", diagnostics);
        await VerifyRecordChecksAsync(headers, headers, [new("observed", "observed", false)], compile: true, execute: true);
    }

    /// <summary>
    /// Managed promotion preserves actual C values and addressable identities without exposing an unverified anonymous allocation type.
    /// </summary>
    [TestMethod]
    public async Task ManagedPromotedMembersExposeOnlyAddressableNativeTypes()
    {
        const string Witness = """
            #include <stdio.h>
            #include <stddef.h>
            #include <stdlib.h>
            #include <string.h>
            int main(void)
            {
                Promoted *value = calloc(1, sizeof(Promoted) + 3);
                if (value == NULL)
                {
                    return 2;
                }

                value->marker = 91;
                value->value = -731;
                uint16_t initial = 65000;
                memcpy((char *)value + offsetof(Promoted, fixed_value), &initial, sizeof(initial));
                value->watched = 17;
                value->cells[0][0].value = -9;
                value->cells[1][2].value = 83;
                value->before = 5;
                value->delta = -13;
                value->after = 11;
                value->left.count = 23;
                value->right.count = 47;
                value->count = 3;
                value->tail[2] = 201;
                printf("%zu %zu %zu %zu %u %d %u %u %llu %d %d %u %d %u %d %d %d %u\n",
                    sizeof(Promoted), _Alignof(Promoted), offsetof(Promoted, value), offsetof(Promoted, tail),
                    (unsigned int)value->marker, value->value, (unsigned int)value->fixed_value,
                    (unsigned int)value->watched, (unsigned long long)value->combined,
                    value->cells[0][0].value, value->cells[1][2].value, value->before, value->delta,
                    value->after, value->left.count, value->right.count, value->count, (unsigned int)value->tail[2]);
                free(value);
                return 0;
            }
            """;
        const string Harness = """
            using System;
            using System.Linq;
            using Ankus;
            using Ankus.Postgres;
            public static class BindingAssertions
            {
                public static unsafe long[] Run()
                {
                    Type[] expectedTypes = [typeof(Promoted), typeof(Cell),
                        typeof(Promoted).GetField("left")!.FieldType, typeof(Promoted).GetField("right")!.FieldType];
                    Type[] actualTypes = typeof(Promoted).Assembly.GetExportedTypes()
                        .Where(type => typeof(IPgNativeType).IsAssignableFrom(type)).ToArray();
                    if (!expectedTypes.OrderBy(type => type.FullName).SequenceEqual(actualTypes.OrderBy(type => type.FullName)) ||
                        expectedTypes[2] == expectedTypes[3] || typeof(Promoted).GetFields().Any(field => field.Name.StartsWith("Anonymous")))
                    {
                        throw new InvalidOperationException("Promotion changed the exposed native type or allocation identities.");
                    }

                    byte* bytes = stackalloc byte[sizeof(Promoted) + 3];
                    new Span<byte>(bytes, sizeof(Promoted) + 3).Clear();
                    Promoted* value = (Promoted*)bytes;
                    value->marker = 91;
                    value->value = -731;
                    value->fixed_value = 65000;
                    value->watched = 17;
                    value->cells[0][0].value = -9;
                    value->cells[1][2].value = 83;
                    value->before = 5;
                    value->delta = -13;
                    value->after = 11;
                    value->left.count = 23;
                    value->right.count = 47;
                    value->count = 3;
                    Span<byte> tail = Promoted.Dangerous_tail((nint)value, 3);
                    tail[2] = 201;
                    fixed (byte* start = tail)
                    {
                        return [sizeof(Promoted), Alignment<Promoted>(), (byte*)&value->value - bytes, start - bytes,
                            value->marker, value->value, value->fixed_value, value->watched, (long)value->combined,
                            value->cells[0][0].value, value->cells[1][2].value, value->before, value->delta,
                            value->after, value->left.count, value->right.count, value->count, tail[2]];
                    }
                }
                private static int Alignment<T>() where T : unmanaged, IPgNativeType => T.NativeAlignment;
            }
            """;
        string directory = Directory.CreateTempSubdirectory("ankus-promoted-values-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(PromotedMemberHeaders, [new("observed", "observed", false)], directory);
            NativeBindingSource binding = NativeBindingRecordCSharp.Generate(records, []);
            long[] expected = await RunRecordWitnessAsync(PromotedMemberHeaders + "\n" + Witness, directory);
            Assert.AreEqual(-731, expected[5]);
            Assert.AreSequenceEqual<long>([-9, 83, 5, -13, 11, 23, 47, 3, 201], expected.Skip(9));
            Assert.AreSequenceEqual(expected, GeneratedBindingCompilation.Run(binding, Harness, context.CancellationToken));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }

    /// <summary>
    /// Promoted callbacks use the enclosing record's field name and retain their native address and guarded invocation.
    /// </summary>
    [TestMethod]
    public async Task ManagedPromotedCallbacksPreserveOwnerAndInvocation()
    {
        const string Headers = """
            typedef struct Methods {
                int bias;
                union {
                    struct { int (*apply)(int); };
                    void *address;
                };
            } Methods;
            int increment(int value) { return value + 17; }
            Methods get(void) { Methods value = {0}; value.bias = 3; value.apply = increment; return value; }
            """;
        const string Harness = """
            public static class BindingAssertions
            {
                public static long[] Run()
                {
                    using NativeCallTestBridge.Scope scope = new();
                    Methods value = NativeMethods.get();
                    Methods_applyCallback callback = value.apply;
                    value.apply = callback;
                    return [value.bias, callback.Invoke(25), value.apply.Invoke(-18),
                        callback.DangerousGetAddress() == value.address ? 1 : 0,
                        Unsafe.SizeOf<Methods_applyCallback>(), IntPtr.Size];
                }
            }
            """;
        Assert.AreSequenceEqual<long>([3, 42, -1, 1, IntPtr.Size, IntPtr.Size], await ExecuteManagedCallsAsync(Headers, ["get"], Harness));
    }
}

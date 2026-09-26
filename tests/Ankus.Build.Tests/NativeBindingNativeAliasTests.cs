namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    /// <summary>
    /// Integer typedefs retain their measured widths when the bindgen source and selected headers use different C primitives.
    /// </summary>
    [TestMethod]
    public async Task CompiledTargetSizedAliasesPreserveNativeWidths()
    {
        const string Declarations = """
            pub enum NodeTag { T_Invalid = 0, T_Leaf = 7, }
            pub type SmallSigned = ::core::ffi::c_long;
            pub type SmallUnsigned = ::core::ffi::c_ulong;
            pub type WideSigned = ::core::ffi::c_long;
            pub type WideUnsigned = ::core::ffi::c_ulong;
            pub struct Leaf {
                pub type_: NodeTag,
                pub small_signed: SmallSigned,
                pub small_unsigned: SmallUnsigned,
                pub wide_signed: WideSigned,
                pub wide_unsigned: WideUnsigned,
                pub small_array: [SmallUnsigned; 2usize],
                pub wide_array: [WideUnsigned; 2usize],
                pub nested: [[SmallUnsigned; 2usize]; 2usize],
            }
            """;
        const string Headers = """
            #include <stdint.h>
            #include <stdio.h>
            #define PG_VERSION_NUM 180006
            typedef enum NodeTag { T_Invalid = 0, T_Leaf = 7 } NodeTag;
            typedef int32_t SmallSigned;
            typedef uint32_t SmallUnsigned;
            typedef int64_t WideSigned;
            typedef uint64_t WideUnsigned;
            typedef struct Leaf {
                NodeTag type;
                SmallSigned small_signed;
                SmallUnsigned small_unsigned;
                WideSigned wide_signed;
                WideUnsigned wide_unsigned;
                SmallUnsigned small_array[2];
                WideUnsigned wide_array[2];
                SmallUnsigned nested[2][2];
            } Leaf;
            """;
        NativeBindingSource binding = await CompileCastBindingAsync(Declarations, Headers, 18);
        const string Harness = """
            using Ankus.Postgres;
            public static class BindingAssertions
            {
                public static unsafe long[] Run()
                {
                    Leaf leaf = new()
                    {
                        type = NodeTag.T_Leaf,
                        small_signed = int.MinValue,
                        small_unsigned = uint.MaxValue,
                        wide_signed = long.MinValue,
                        wide_unsigned = ulong.MaxValue,
                    };
                    leaf.small_array[0] = 17;
                    leaf.small_array[1] = uint.MaxValue;
                    leaf.wide_array[0] = 4294967296UL;
                    leaf.wide_array[1] = ulong.MaxValue;
                    leaf.nested[0][0] = 1;
                    leaf.nested[0][1] = 2;
                    leaf.nested[1][0] = 3;
                    leaf.nested[1][1] = uint.MaxValue;
                    return [sizeof(Leaf), (byte*)&leaf.small_unsigned - (byte*)&leaf.small_signed,
                        (byte*)&leaf.wide_unsigned - (byte*)&leaf.wide_signed,
                        leaf.small_signed, leaf.small_unsigned, leaf.wide_signed, unchecked((long)leaf.wide_unsigned),
                        leaf.small_array[0], leaf.small_array[1], (long)leaf.wide_array[0], unchecked((long)leaf.wide_array[1]),
                        leaf.nested[0][0], leaf.nested[0][1], leaf.nested[1][0], leaf.nested[1][1]];
                }
            }
            """;
        long[] observed = GeneratedBindingCompilation.Run(binding, Harness, context.CancellationToken);
        Assert.AreSequenceEqual<long>(
            [72, 4, 8, int.MinValue, uint.MaxValue, long.MinValue, -1, 17, uint.MaxValue, 4294967296, -1, 1, 2, 3, uint.MaxValue], observed);
    }
}

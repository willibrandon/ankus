using System.Globalization;
using System.Text.Json.Nodes;

namespace Ankus.Build.Tests;

/// <summary>
/// Supplies independently specified C numeric facts for synthetic target observations.
/// </summary>
internal static class NativeNumericModelFixture
{
    /// <summary>
    /// Gets signed char and wchar_t with IEEE binary32, binary64 and extended binary80 values.
    /// </summary>
    internal static NativeNumericModel Binary80 { get; } = new(true, 4, true, 2,
        new(24, -125, 128), new(53, -1021, 1024), new(64, -16381, 16384));

    /// <summary>
    /// Encodes the independent fixture's scalar facts in the storage protocol's documented order.
    /// </summary>
    internal const string EncodedBinary80 = "1,4,1,2,24,-125,128,53,-1021,1024,64,-16381,16384";

    /// <summary>
    /// Adds native numeric constants to the fixture's existing target enum.
    /// </summary>
    /// <param name="root">The synthetic translation unit.</param>
    /// <returns>The same translation unit with a complete numeric target.</returns>
    internal static JsonNode AddFacts(JsonNode root)
    {
        string[] names = ["char_signed", "wchar_size", "wchar_signed", "float_radix", "float_precision", "float_min_exp", "float_max_exp",
            "double_precision", "double_min_exp", "double_max_exp", "long_double_precision", "long_double_min_exp", "long_double_max_exp"];
        int[] values = [1, 4, 1, 2, 24, -125, 128, 53, -1021, 1024, 64, -16381, 16384];
        JsonArray members = root["inner"]![0]!["inner"]!.AsArray();
        for (int index = 0; index < names.Length; index++)
        {
            members.Add(new JsonObject
            {
                ["name"] = "ankus_header_" + names[index], ["kind"] = "EnumConstantDecl",
                ["inner"] = new JsonArray(new JsonObject { ["kind"] = "ConstantExpr", ["value"] = values[index].ToString(CultureInfo.InvariantCulture) }),
            });
        }

        return root;
    }
}

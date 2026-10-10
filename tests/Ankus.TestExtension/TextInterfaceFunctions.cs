using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises PostgreSQL value types through .NET's generic parsing contract.
/// </summary>
public static class TextInterfaceFunctions
{
    /// <summary>
    /// Parses values as generic .NET code does, through <see cref="IParsable{TSelf}"/>, which runs each type's
    /// PostgreSQL input function.
    /// </summary>
    /// <returns>The parsed numeric, network, geometry, interval and date values and whether invalid text was rejected.</returns>
    [PgFunction]
    public static string ParseThroughInterfaces()
        => string.Join('|',
            Parse<PgNumeric>("12.50").ToString(),
            Parse<PgInet>("192.0.2.1/24").ToString(),
            Parse<PgPoint>("(1.5, -2)").ToString(),
            Parse<PgInterval>("1 day 02:00").ToIsoString(),
            Parse<PgDate>("2026-10-10").DaysSinceEpoch.ToString(CultureInfo.InvariantCulture),
            TryParse<PgNumeric>("not a number") ? "accepted" : "rejected",
            TryParse<PgTimestamp>("2026-10-10 12:00") ? "accepted" : "rejected");

    private static T Parse<T>(string text) where T : IParsable<T> => T.Parse(text, CultureInfo.InvariantCulture);

    private static bool TryParse<T>(string text) where T : IParsable<T> => T.TryParse(text, CultureInfo.InvariantCulture, out _);
}

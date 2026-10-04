using System.Globalization;

namespace Ankus.TestExtension;

/// <summary>
/// Exercises interpolation token ownership and native quoting against PostgreSQL's parser.
/// </summary>
public static class SpiCommandBoundaryFunctions
{
    /// <summary>
    /// Reads a bound value alongside dollar text in strings, identifiers and nested comments.
    /// </summary>
    /// <param name="value">The hostile value that must remain data.</param>
    /// <returns>The four independently read cells.</returns>
    [PgFunction]
    public static string CommandDollarContexts(string value)
    {
        SpiResult result = Spi.Query(Spi.Sql($"SELECT {value}, '$1', \"$1\", $tag$$2$tag$ FROM (VALUES (42)) AS t(\"$1\") /* $3 /* $4 */ */ -- $5\n"));
        return $"{result[0].Get<string>(0)}|{result[0].Get<string>(1)}|{result[0].Get<int>(2)}|{result[0].Get<string>(3)}";
    }

    /// <summary>
    /// Rejects ambiguous parameter ownership before execution and recovers from malformed adjacent SQL tokens.
    /// </summary>
    /// <param name="mode">The token collision or hidden interpolation.</param>
    /// <returns>The exact error and a later successful query in the same callback.</returns>
    [PgFunction]
    public static string CommandTokenRecovery(int mode)
    {
        string error = "missing";
        try
        {
            SpiCommand command = mode switch
            {
                0 => Spi.Sql($"SELECT {42}0"),
                1 => Spi.Sql($"SELECT $1 + {40}"),
                2 => Spi.Sql($"SELECT {40} + $1"),
                3 => Spi.Sql($"SELECT '{42}'"),
                4 => Spi.Sql($"SELECT /* {42} */ 99"),
                5 => Spi.Sql($"SELECT $$ {42} $$"),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            _ = Spi.ExecuteScalar<int>(command);
        }
        catch (ArgumentException)
        {
            error = nameof(ArgumentException);
        }
        catch (PgException native)
        {
            error = native.SqlState;
        }

        int recovered = Spi.ExecuteScalar<int>(Spi.Sql($"SELECT {40}::int + {2}"));
        return error + "|" + recovered.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Uses unchanged quoted identifier fragments and a quoted literal in raw SPI text.
    /// </summary>
    /// <param name="identifier">A column name containing arbitrary identifier punctuation.</param>
    /// <param name="value">A text value containing arbitrary SQL punctuation.</param>
    /// <returns>The recovered value with its exact column name.</returns>
    [PgFunction]
    public static string CommandQuotedFragments(string identifier, string value)
    {
        string column = Spi.QuoteIdentifier(identifier);
        SpiResult result = Spi.Query($"SELECT {Spi.QuoteLiteral(value)} AS {column}");
        return result.Columns[0].Name + "|" + result[0].Get<string>(0);
    }

    /// <summary>
    /// Keeps two-digit ordinals distinct while casts and arithmetic retain their normal SQL meaning.
    /// </summary>
    /// <returns>The native result of all twelve independently bound integers.</returns>
    [PgFunction]
    public static int CommandTwoDigitBindings()
        => Spi.ExecuteScalar<int>(Spi.Sql($"SELECT {1}::int + {2} + {3} + {4} + {5} + {6} + {7} + {8} + {9} + {10} + {11} + {12}"));
}

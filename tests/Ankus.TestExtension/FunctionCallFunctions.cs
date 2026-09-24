namespace Ankus.TestExtension;

/// <summary>
/// Invokes catalog functions through the managed API for live PostgreSQL contract tests.
/// </summary>
public static class FunctionCallFunctions
{
    /// <summary>
    /// Calls a supplied native entry point with copied raw integer arguments.
    /// </summary>
    /// <param name="address">The fixture's version-1 entry point.</param>
    /// <param name="left">The nullable left operand.</param>
    /// <param name="right">The nullable right operand.</param>
    /// <param name="raw">Whether to copy through a raw result owner.</param>
    /// <param name="call">The live input datums.</param>
    /// <returns>The native result.</returns>
    [PgFunction]
    public static int? CallNative(long address, int? left, int? right, bool raw, PgFunctionContext call)
    {
        if (!raw)
        {
            return PgFunctions.DangerousCall<int?>((nint)address, 0, call.Arguments[1], call.Arguments[2]);
        }

        using PgMemoryContext owner = PgMemoryContext.Create("direct native result");
        PgDatum result = PgFunctions.DangerousCallRaw((nint)address, 23, owner, 0, call.Arguments[1], call.Arguments[2]);
        return result.Read<int?>();
    }

    /// <summary>
    /// Calls a native function with zero or more than PostgreSQL's catalog argument limit.
    /// </summary>
    /// <param name="address">The native fixture address.</param>
    /// <param name="count">The number of integer arguments.</param>
    /// <returns>The fixture's sum.</returns>
    [PgFunction]
    public static int CallNativeMany(long address, int count)
    {
        PgDatum one = PgDatum.DangerousCreate(1, 23, PgMemoryContext.Current);
        PgDatum[] arguments = [.. Enumerable.Repeat(one, count)];
        return PgFunctions.DangerousCall<int>((nint)address, 0, arguments);
    }

    /// <summary>
    /// Copies a native text result before its operation context ends.
    /// </summary>
    /// <param name="address">The native textcat entry point.</param>
    /// <param name="left">The left text.</param>
    /// <param name="right">The right text.</param>
    /// <param name="call">The raw arguments.</param>
    /// <returns>The copied concatenation.</returns>
    [PgFunction]
    public static string CallNativeText(long address, string left, string right, PgFunctionContext call)
        => PgFunctions.DangerousCall<string>((nint)address, 0, call.Arguments[1], call.Arguments[2]);

    /// <summary>
    /// Calls integer functions with explicit values, NULLs, defaults, or no arguments.
    /// </summary>
    /// <param name="name">The SQL identifier.</param>
    /// <param name="oid">The optional direct function OID.</param>
    /// <param name="value">The nullable integer argument.</param>
    /// <param name="mode">The positional/default argument arrangement.</param>
    /// <returns>The nullable result.</returns>
    [PgFunction]
    public static int? CallInteger(string name, uint oid, int? value, int mode)
    {
        PgFunctionArgument[] arguments = mode switch
        {
            0 => [PgFunctionArgument.Create(value)],
            1 => [],
            2 => [PgFunctionArgument.Create(value), PgFunctionArgument.Default<int>()],
            3 => [PgFunctionArgument.Default<int>(), PgFunctionArgument.Create(value)],
            4 => [PgFunctionArgument.Default<int>()],
            5 => [PgFunctionArgument.Default<long>()],
            6 => [.. Enumerable.Repeat(PgFunctionArgument.Create(1), 101)],
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        return oid == 0 ? PgFunctions.Call<int?>(name, arguments) : PgFunctions.Call<int?>(oid, arguments);
    }

    /// <summary>
    /// Calls text functions with an optional explicit collation.
    /// </summary>
    /// <param name="name">The function identifier.</param>
    /// <param name="oid">The optional function OID.</param>
    /// <param name="value">The nullable text argument.</param>
    /// <param name="collation">The optional collation OID.</param>
    /// <returns>The independent text result.</returns>
    [PgFunction]
    public static string? CallText(string name, uint oid, string? value, uint? collation)
    {
        var options = new PgFunctionCallOptions { CollationOid = collation };
        return oid == 0
            ? PgFunctions.Call<string?>(name, options, PgFunctionArgument.Create(value))
            : PgFunctions.Call<string?>(oid, options, PgFunctionArgument.Create(value));
    }

    /// <summary>
    /// Supplies either an explicit variadic array or individual scalar variadic arguments.
    /// </summary>
    /// <param name="name">The variadic function name.</param>
    /// <param name="oid">The optional function OID.</param>
    /// <param name="packed">Whether to supply the declared array.</param>
    /// <returns>The computed result.</returns>
    [PgFunction]
    public static int CallVariadic(string name, uint oid, bool packed)
    {
        PgFunctionArgument[] arguments = packed
            ? [PgFunctionArgument.Create<int[]>([3, 5, 7])]
            : [PgFunctionArgument.Create(3), PgFunctionArgument.Create(5), PgFunctionArgument.Create(7)];
        var options = new PgFunctionCallOptions { Variadic = packed };
        return oid == 0 ? PgFunctions.Call<int>(name, options, arguments) : PgFunctions.Call<int>(oid, options, arguments);
    }

    /// <summary>
    /// Resolves a polymorphic array result using actual argument types.
    /// </summary>
    /// <param name="oid">The optional array_append function OID.</param>
    /// <returns>The appended array.</returns>
    [PgFunction]
    public static int[] CallPolymorphic(uint oid)
    {
        PgFunctionArgument[] arguments = [PgFunctionArgument.Create<int[]>([3, 5]), PgFunctionArgument.Create(7)];
        return oid == 0 ? PgFunctions.Call<int[]>("pg_catalog.array_append", arguments) : PgFunctions.Call<int[]>(oid, arguments);
    }

    /// <summary>
    /// Executes a void-returning function through name or OID lookup.
    /// </summary>
    /// <param name="name">The SQL function identifier.</param>
    /// <param name="oid">The optional function identity.</param>
    [PgFunction]
    public static void CallVoid(string name, uint oid)
    {
        if (oid == 0)
        {
            PgFunctions.Call(name, PgFunctionArgument.Create(42));
        }
        else
        {
            PgFunctions.Call(oid, PgFunctionArgument.Create(42));
        }
    }

    /// <summary>
    /// Copies exact native return types to an independent owner and verifies reset invalidation.
    /// </summary>
    /// <param name="name">The no-argument target.</param>
    /// <param name="oid">The optional function OID.</param>
    /// <returns>The type OID, NULL state, output value, and invalidation result.</returns>
    [PgFunction]
    public static string CallRaw(string name, uint oid)
    {
        using PgMemoryContext owner = PgMemoryContext.Create("function result owner");
        PgDatum result = oid == 0 ? PgFunctions.CallRaw(name, owner) : PgFunctions.CallRaw(oid, owner);
        Spi.Execute("SELECT repeat('overwrite', 10000)");
        string observed = $"{result.TypeOid}|{result.IsNull}|{result.ToPostgresString() ?? "NULL"}";
        owner.Reset();
        try
        {
            result.DangerousGetBits();
            return observed + "|live";
        }
        catch (ObjectDisposedException)
        {
            return observed + "|expired";
        }
    }

    /// <summary>
    /// Requests an incorrect result type before a mutating function can execute.
    /// </summary>
    /// <param name="name">The integer-returning target.</param>
    /// <returns>The incorrectly requested string, if validation fails.</returns>
    [PgFunction]
    public static string CallWrongResult(string name) => PgFunctions.Call<string>(name);

    /// <summary>
    /// Catches a function error, checks its diagnostics, and immediately calls another function.
    /// </summary>
    /// <param name="name">The failing function.</param>
    /// <param name="oid">The optional function OID.</param>
    /// <returns>Owned diagnostics and the recovered result.</returns>
    [PgFunction]
    public static string CallRecover(string name, uint oid)
    {
        string failure;
        int cleanup = 0;
        try
        {
            try
            {
                _ = oid == 0 ? PgFunctions.Call<int>(name) : PgFunctions.Call<int>(oid);
                return "no error";
            }
            finally
            {
                cleanup++;
            }
        }
        catch (PgException error)
        {
            failure = $"{error.SqlState}|{error.Message}|{error.Detail}|{error.Hint}";
        }

        return $"{failure}|{cleanup}|{PgFunctions.Call<int>("pg_catalog.abs", PgFunctionArgument.Create(-42))}";
    }

    /// <summary>
    /// Returns a call's resolved collation through nested generated dispatch.
    /// </summary>
    /// <param name="call">The injected native context.</param>
    /// <param name="value">The SQL input.</param>
    /// <returns>The collation OID as text.</returns>
    [PgFunction]
    public static string CallCollation(PgFunctionContext call, string value) => call.CollationOid.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Verifies direct invocation supplies real function metadata and releases its cached state.
    /// </summary>
    /// <returns>The nested value and cleanup counters.</returns>
    [PgFunction]
    public static string CallManagedState()
    {
        string result = PgFunctions.Call<string>("datatype.state_tick", PgFunctionArgument.Create("nested"), PgFunctionArgument.Create(false));
        return result + "/" + PgFunctions.Call<string>("datatype.state_counts");
    }

    /// <summary>
    /// Round-trips a raw datum through a polymorphic function while preserving NULL and declared domain identity.
    /// </summary>
    /// <param name="value">The nullable text value.</param>
    /// <param name="call">The actual typed input datum.</param>
    /// <returns>The unchanged typed PostgreSQL result.</returns>
    [PgFunction]
    public static string CallRawArgument(string? value, PgFunctionContext call)
    {
        PgDatum argument = call.Arguments[0];
        PgDatum result = PgFunctions.CallRaw("pg_temp.raw_identity", PgMemoryContext.Current, PgFunctionArgument.Create(argument));
        return $"{argument.TypeOid == result.TypeOid}|{result.IsNull}|{result.ToPostgresString() ?? "NULL"}";
    }

    /// <summary>
    /// Checks exact boolean and nullable result conversion through built-in functions.
    /// </summary>
    /// <returns>The independent typed observations.</returns>
    [PgFunction]
    public static string CallBooleanValues()
    {
        bool equal = PgFunctions.Call<bool>("pg_catalog.int4eq", PgFunctionArgument.Create(42), PgFunctionArgument.Create(42));
        bool unequal = PgFunctions.Call<bool>("pg_catalog.int4eq", PgFunctionArgument.Create(41), PgFunctionArgument.Create(42));
        bool? missing = PgFunctions.Call<bool?>("pg_catalog.int4eq", PgFunctionArgument.Create<int?>(null), PgFunctionArgument.Create(42));
        return $"{equal}|{unequal}|{missing is null}";
    }
}

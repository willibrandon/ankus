namespace Ankus;

/// <summary>Selects a PostgreSQL numeric routine within the native guard.</summary>
internal enum NumericOperation
{
    Parse, Add, Subtract, Multiply, Divide, Remainder, Negate, Abs, Round, Truncate,
    Ceiling, Floor, Sqrt, Exp, Log, LogBase, Power, Gcd, Lcm, FromDouble, ToDouble, Rescale,
    FromSingle, ToSingle, ToInt16, ToInt32, ToInt64,
}

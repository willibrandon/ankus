namespace Ankus.Generators;

/// <summary>
/// Shares exact managed datum conversion expressions between scalar and set dispatchers.
/// </summary>
internal static class ManagedConversion
{
    /// <summary>
    /// Reads a nullable-aware managed value from the shared native transport.
    /// </summary>
    internal static string Read(FunctionType type, string slot, string numericSuffix)
    {
        string numeric = slot + ".ReadNumeric()" + numericSuffix;
        string value = type.Managed switch
        {
            _ when type.Element is not null => slot + ".ReadArray<" + type.ElementManaged + ">()" +
                (type.IsVector ? ".ToVector()" : string.Empty),
            _ when type.GeometryName.Length != 0 => slot + ".Read" + type.GeometryName + "()",
            _ when type.RangeSubtype is not null => slot + ".ReadRange<" + type.RangeSubtype.Managed + ">()",
            _ when type.Enumeration is not null => "global::Ankus.PgEnums.Parse<" + type.Managed + ">(" + slot + ".ReadString())",
            _ when type.IsComposite => slot + ".ReadTuple()",
            "string" => slot + ".ReadString()",
            "byte[]" => slot + ".ReadBytes()",
            "global::System.Guid" => slot + ".ReadGuid()",
            "global::Ankus.PgInet" => slot + ".ReadInet()",
            "global::Ankus.PgCidr" => slot + ".ReadCidr()",
            "global::System.Net.IPAddress" => slot + ".ReadInet().ToIPAddress()",
            "global::System.Net.IPNetwork" => slot + ".ReadCidr().ToIPNetwork()",
            "global::Ankus.PgJson" => slot + ".ReadJson()",
            "global::Ankus.PgJsonb" => slot + ".ReadJsonb()",
            "global::Ankus.PgNumeric" => numeric,
            "decimal" => numeric + ".ToDecimal()",
            "bool" => slot + ".Integral != 0",
            "float" => "global::System.BitConverter.Int32BitsToSingle((int)" + slot + ".Integral)",
            "double" => "global::System.BitConverter.Int64BitsToDouble(" + slot + ".Integral)",
            _ when type.IsTemporal => slot + ".Read" + type.TemporalName + "()" +
                (type.ClrTemporalName.Length == 0 ? string.Empty : ".To" + type.ClrTemporalName + "()"),
            _ => "(" + type.Managed + ")" + slot + "." + type.Field,
        };
        return type.Nullable ? $"({slot}.IsNull != 0 ? ({type.Managed}?)null : {value})" : value;
    }

    /// <summary>
    /// Writes a present managed value through a pointer to the shared transport.
    /// </summary>
    internal static string Write(FunctionType result, string value, string target, string numericSuffix)
    {
        string temporalValue = result.IsTemporal && result.ClrTemporalName.Length != 0
            ? $"global::Ankus.Pg{result.TemporalName}.From{result.ClrTemporalName}({value})" : value;
        string numericValue = (result.Managed == "decimal" ? $"global::Ankus.PgNumeric.FromDecimal({value})" : value)
            + numericSuffix;
        return result.Managed switch
        {
            _ when result.Element is not null => "*" + target + " = global::Ankus.NativeValue.FromArray(" +
                (result.IsVector ? "new global::Ankus.PgArray<" + result.ElementManaged + ">(" + value + ")" : value) + ");",
            _ when result.GeometryName.Length != 0 => $"*{target} = global::Ankus.NativeValue.From{result.GeometryName}({value});",
            _ when result.RangeSubtype is not null => $"*{target} = global::Ankus.NativeValue.FromRange({value});",
            _ when result.Enumeration is not null => $"*{target} = global::Ankus.NativeValue.FromString(global::Ankus.PgEnums.GetLabel({value}));",
            _ when result.IsComposite => $"*{target} = global::Ankus.NativeValue.FromTuple({value});",
            "string" => $"*{target} = global::Ankus.NativeValue.FromString({value});",
            "byte[]" => $"*{target} = global::Ankus.NativeValue.FromBytes({value});",
            "global::System.Guid" => $"*{target} = global::Ankus.NativeValue.FromGuid({value});",
            "global::Ankus.PgInet" => $"*{target} = global::Ankus.NativeValue.FromInet({value});",
            "global::Ankus.PgCidr" => $"*{target} = global::Ankus.NativeValue.FromCidr({value});",
            "global::System.Net.IPAddress" => $"*{target} = global::Ankus.NativeValue.FromInet(new global::Ankus.PgInet({value}));",
            "global::System.Net.IPNetwork" => $"*{target} = global::Ankus.NativeValue.FromCidr(new global::Ankus.PgCidr({value}));",
            "global::Ankus.PgJson" or "global::Ankus.PgJsonb" =>
                $"*{target} = global::Ankus.NativeValue.FromString({value}.Text);",
            "global::Ankus.PgNumeric" or "decimal" => $"*{target} = global::Ankus.NativeValue.FromString({numericValue}.Text);",
            "bool" => $"{target}->Integral = {value} ? 1 : 0;",
            "float" => $"{target}->Integral = global::System.BitConverter.SingleToInt32Bits({value});",
            "double" => $"{target}->Integral = global::System.BitConverter.DoubleToInt64Bits({value});",
            _ when result.IsTemporal => $"*{target} = global::Ankus.NativeValue.From{result.TemporalName}({temporalValue});",
            _ => $"{target}->{result.Field} = {value};",
        };
    }
}

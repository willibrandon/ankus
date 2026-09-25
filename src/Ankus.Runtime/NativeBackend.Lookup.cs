using System.Text;

namespace Ankus;

public static partial class NativeBackend
{
    private static readonly UTF8Encoding s_lookupEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Rejects string content that would lose identity when transported as a native C string.
    /// </summary>
    internal static void ValidateLookupName(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("PostgreSQL names cannot contain a zero character.", parameterName);
        }

        _ = s_lookupEncoding.GetByteCount(value);
    }

    /// <summary>
    /// Calls native regtypein with exact type syntax under the existing error and scratch-context guard.
    /// </summary>
    internal static uint ResolveTypeName(string typeName)
    {
        ValidateLookupName(typeName, nameof(typeName));
        return Scalar<uint>(SpiOperation.Lookup, 0, [SpiParameter.Create(typeName)]);
    }

    /// <summary>
    /// Passes exact OIDs followed by independent name components to native OpernameGetOprid.
    /// </summary>
    internal static uint ResolveOperator(IReadOnlyList<string> components, uint leftTypeOid, uint rightTypeOid)
    {
        SpiParameter[] parameters = new SpiParameter[checked(components.Count + 2)];
        parameters[0] = SpiParameter.Create(leftTypeOid);
        parameters[1] = SpiParameter.Create(rightTypeOid);
        for (int index = 0; index < components.Count; index++)
        {
            parameters[index + 2] = SpiParameter.Create(components[index]);
        }

        return Scalar<uint>(SpiOperation.Lookup, 1, parameters);
    }
}

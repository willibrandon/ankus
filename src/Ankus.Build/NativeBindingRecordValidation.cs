using System.Globalization;
using System.Numerics;

namespace Ankus.Build;

/// <summary>
/// Rejects partial, contradictory or out-of-bounds native record graphs before publication.
/// </summary>
internal static class NativeBindingRecordValidation
{
    /// <summary>
    /// Checks target identity, every graph edge and physical record storage without following recursive pointers.
    /// </summary>
    internal static void Validate(NativeRecordGraph graph, NativeHeaderTarget expected, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(graph);
        NativeBindingNumericModel.Validate(expected.Numeric);
        string[] selected = [.. names.Order(StringComparer.Ordinal)];
        if (graph.Target != expected || graph.Roots is null || graph.Types is null || graph.Declarations is null ||
            string.IsNullOrEmpty(expected.RuntimeIdentifier) || !NativeBindingTarget.IsValid(expected.RuntimeIdentifier, expected.PointerSize, expected.IsLittleEndian) ||
            graph.Types.Count > 100_000 || graph.Declarations.Count > 100_000 ||
            !graph.Roots.Keys.Order(StringComparer.Ordinal).SequenceEqual(selected, StringComparer.Ordinal))
        {
            throw new FormatException("Native record graph has the wrong target or selection.");
        }

        foreach ((string name, int id) in graph.Roots)
        {
            NativeBindingCDeclaration.ValidateName(name);
            _ = Type(id);
        }

        for (int id = 0; id < graph.Types.Count; id++)
        {
            NativeRecordType type = Type(id);
            NativeRecordType canonical = Type(type.Canonical);
            if (canonical.Canonical != type.Canonical || string.IsNullOrEmpty(type.Spelling) || type.Spelling.Length > 1_048_576 ||
                type.Name is null || (type.Qualifiers & ~(NativeHeaderQualifiers.Const | NativeHeaderQualifiers.Volatile | NativeHeaderQualifiers.Restrict)) != 0 ||
                type.SourceDeclaration?.Length > 1_048_576)
            {
                throw new FormatException("Invalid canonical native type or spelling.");
            }

            Storage(type.Size, type.Alignment);
            if (type.Element is int element) { _ = Type(element); }

            if (type.Declaration is int declaration) { _ = Declaration(declaration); }

            bool indirect = type.Kind is "alias" or "pointer" or "array" or "vector" or "elaborated" or "attributed" or "atomic" or "complex";
            bool tagged = type.Kind is "record" or "enum";
            if (indirect != type.Element.HasValue || tagged != type.Declaration.HasValue ||
                (type.Kind == "function") != (type.Function is not null) ||
                (type.Kind != "array" && type.Kind != "vector" && type.Count is not null) ||
                (type.Kind != "alias" && type.SourceDeclaration is not null))
            {
                throw new FormatException("Native type edges do not match its shape.");
            }

            if (type.Kind is not ("scalar" or "alias" or "pointer" or "array" or "vector" or "elaborated" or "attributed" or "atomic" or "complex" or "record" or "enum" or "function"))
            {
                throw new FormatException("Unknown native record type shape.");
            }

            if ((type.Kind is "scalar" or "alias") != (type.Name.Length != 0) || (type.Kind == "alias" && string.IsNullOrEmpty(type.SourceDeclaration)))
            {
                throw new FormatException("Missing or extraneous native type name/declaration.");
            }

            if (canonical.Kind == "pointer" && (type.Size != expected.PointerSize || type.Alignment is null))
            {
                throw new FormatException("Native pointer storage disagrees with the selected target.");
            }

            if (canonical.Kind == "function" || (canonical.Kind == "scalar" && canonical.Name == "void"))
            {
                if (type.Size is not null || type.Alignment is not null) { throw new FormatException("A non-object native type has object storage."); }
            }

            if (type.Kind is "array" or "vector")
            {
                if (type.Count < 0 || (type.Kind == "vector" && type.Count is null or 0)) { throw new FormatException("Invalid native array/vector extent."); }

                if (canonical.Kind == "array" && type.Count is null && type.Size is not null) { throw new FormatException("An incomplete native array cannot have a total size."); }

                NativeRecordType item = Type(type.Element!.Value);
                if (canonical.Kind == type.Kind && type.Count is long count && item.Size is long stride && type.Size is long size &&
                    (type.Kind == "array" ? (BigInteger)count * stride != size : (BigInteger)count * stride > size))
                {
                    throw new FormatException("Native array/vector storage disagrees with its extent.");
                }
            }

            if (type.Function is NativeRecordFunction function)
            {
                _ = Type(function.Result);
                if (function.Parameters is null || function.Parameters.Count > 100_000 || function.CallingConvention is < 0 or >= 100 ||
                    (!function.HasPrototype && function.Parameters.Count != 0))
                {
                    throw new FormatException("Invalid native function shape.");
                }

                foreach (int parameter in function.Parameters) { _ = Type(parameter); }
            }

            if (tagged && Declaration(type.Declaration!.Value).Kind == "enum" != (type.Kind == "enum"))
            {
                throw new FormatException("Native type references the wrong declaration kind.");
            }

            if (tagged && (type.Size != Declaration(type.Declaration!.Value).Size || type.Alignment != Declaration(type.Declaration.Value).Alignment))
            {
                throw new FormatException("Native tagged storage disagrees with its declaration.");
            }
        }

        long totalFields = 0;
        long totalConstants = 0;
        foreach (NativeRecordDeclaration declaration in graph.Declarations)
        {
            if (declaration is null || declaration.Kind is not ("struct" or "union" or "enum") || declaration.Name is null ||
                declaration.Fields is null || declaration.EnumValues is null || declaration.IsComplete != declaration.Size.HasValue ||
                declaration.IsComplete != declaration.Alignment.HasValue)
            {
                throw new FormatException("Invalid native declaration completeness.");
            }

            Storage(declaration.Size, declaration.Alignment);
            if (declaration.Name.Length != 0) { NativeBindingCDeclaration.ValidateName(declaration.Name); }

            totalFields += declaration.Fields.Count;
            totalConstants += declaration.EnumValues.Count;
            if (totalFields > 1_000_000 || (!declaration.IsComplete && declaration.Fields.Count != 0) ||
                totalConstants > 1_000_000 || (!declaration.IsComplete && declaration.EnumValues.Count != 0) ||
                (declaration.Kind == "enum" && declaration.IsComplete && declaration.EnumUnderlying is null) ||
                (declaration.Kind == "enum" && declaration.Fields.Count != 0) ||
                (declaration.Kind != "enum" && (declaration.EnumUnderlying is not null || declaration.EnumValues.Count != 0)))
            {
                throw new FormatException("Invalid fields or enum values for the native declaration.");
            }

            if (declaration.EnumUnderlying is int underlying) { _ = Type(underlying); }

            var constantNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (NativeRecordConstant constant in declaration.EnumValues)
            {
                if (constant is null || constant.Value is null || constant.Value.Length > 20 || !constantNames.Add(constant.Name) ||
                    !BigInteger.TryParse(constant.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger value) ||
                    value < long.MinValue || value > ulong.MaxValue || value.ToString(CultureInfo.InvariantCulture) != constant.Value)
                {
                    throw new FormatException("Invalid or duplicate native enum constant.");
                }

                NativeBindingCDeclaration.ValidateName(constant.Name);
            }

            foreach (NativeRecordField field in declaration.Fields)
            {
                if (field is null || field.Name is null || string.IsNullOrEmpty(field.SourceDeclaration) || field.SourceDeclaration.Length > 1_048_576 ||
                    field.OffsetBits < 0 || field.BitWidth < 0 || (field.IsAnonymous && (field.Name.Length != 0 || field.BitWidth is not null)))
                {
                    throw new FormatException("Invalid native field identity or bit layout.");
                }

                NativeRecordType fieldType = Type(field.Type);
                NativeRecordType canonicalField = Type(fieldType.Canonical);
                if (field.Name.Length != 0) { NativeBindingCDeclaration.ValidateName(field.Name); }

                if ((!field.IsAnonymous && field.Name.Length == 0 && field.BitWidth is null) ||
                    (field.IsAnonymous && canonicalField.Kind != "record") ||
                    (field.BitWidth == 0 && field.Name.Length != 0) ||
                    (field.BitWidth is int bitWidth && (fieldType.Size is null || bitWidth > (BigInteger)fieldType.Size.Value * 8)) ||
                    (fieldType.Size is null && (canonicalField.Kind != "array" || canonicalField.Count is not null)) ||
                    (field.BitWidth is null && field.OffsetBits % 8 != 0) || (declaration.Kind == "union" && field.OffsetBits != 0))
                {
                    throw new FormatException("Native field shape contradicts its physical layout.");
                }

                BigInteger length = field.BitWidth is int width ? width : (BigInteger)(fieldType.Size ?? 0) * 8;
                if ((BigInteger)field.OffsetBits + length > (BigInteger)declaration.Size!.Value * 8)
                {
                    throw new FormatException("Native field exceeds its containing record.");
                }
            }
        }

        var visitedTypes = new HashSet<int>();
        var visitedDeclarations = new HashSet<int>();
        var pending = new Queue<int>(graph.Roots.Values);
        while (pending.TryDequeue(out int typeIndex))
        {
            if (!visitedTypes.Add(typeIndex)) { continue; }

            NativeRecordType type = Type(typeIndex);
            pending.Enqueue(type.Canonical);
            if (type.Element is int element) { pending.Enqueue(element); }

            if (type.Function is NativeRecordFunction function)
            {
                pending.Enqueue(function.Result);
                foreach (int parameter in function.Parameters) { pending.Enqueue(parameter); }
            }

            if (type.Declaration is int declarationIndex && visitedDeclarations.Add(declarationIndex))
            {
                NativeRecordDeclaration declaration = Declaration(declarationIndex);
                foreach (NativeRecordField field in declaration.Fields) { pending.Enqueue(field.Type); }

                if (declaration.EnumUnderlying is int underlying) { pending.Enqueue(underlying); }
            }
        }

        if (visitedTypes.Count != graph.Types.Count || visitedDeclarations.Count != graph.Declarations.Count)
        {
            throw new FormatException("Native record graph contains observations outside the selected closure.");
        }

        NativeRecordType Type(int id) => id >= 0 && id < graph.Types.Count && graph.Types[id] is NativeRecordType value
            ? value : throw new FormatException("Invalid native type index.");

        NativeRecordDeclaration Declaration(int id) => id >= 0 && id < graph.Declarations.Count && graph.Declarations[id] is NativeRecordDeclaration value
            ? value : throw new FormatException("Invalid native declaration index.");
    }

    private static void Storage(long? size, long? alignment)
    {
        if (size < 0 || (alignment is long value && (value <= 0 || !BitOperations.IsPow2((ulong)value))) || (size is not null && alignment is null))
        {
            throw new FormatException("Invalid native object size or alignment.");
        }
    }
}

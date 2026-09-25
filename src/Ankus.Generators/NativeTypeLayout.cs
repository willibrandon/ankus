using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Proves a source-defined unmanaged layout contains only dense, fixed-width native values.
/// </summary>
internal static class NativeTypeLayout
{
    /// <summary>
    /// Computes the packed payload size without assuming the layout of framework or metadata-only structs.
    /// </summary>
    internal static int Validate(INamedTypeSymbol type, out string? error)
    {
        try
        {
            if (type.TypeKind != TypeKind.Struct)
            {
                throw new InvalidOperationException("NativeLayout requires an unmanaged struct.");
            }

            int size = Size(type, type.ContainingAssembly, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default),
                new Dictionary<ITypeSymbol, int>(SymbolEqualityComparer.Default));
            error = null;
            return size;
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            error = exception is OverflowException ? "The packed native layout is too large." : exception.Message;
            return 0;
        }
    }

    /// <summary>
    /// Rejects process-specific representations and padding before adding field sizes.
    /// </summary>
    private static int Size(ITypeSymbol type, IAssemblySymbol assembly, HashSet<ITypeSymbol> visiting, Dictionary<ITypeSymbol, int> sizes)
    {
        int primitive = PrimitiveSize(type);
        if (primitive != 0)
        {
            return primitive;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying })
        {
            return Size(underlying, assembly, visiting, sizes);
        }

        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct, IsUnmanagedType: true, IsGenericType: false, IsRefLikeType: false } named ||
            !SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, assembly) || named.DeclaringSyntaxReferences.Length == 0)
        {
            throw new InvalidOperationException("NativeLayout fields require fixed-width numeric values, enums, or packed unmanaged structs declared in this assembly; references, booleans, characters, pointers, native integers and opaque framework or external structs are unsupported.");
        }

        if (sizes.TryGetValue(named, out int knownSize))
        {
            return knownSize;
        }

        AttributeData? layout = named.GetAttributes().FirstOrDefault(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
        if (layout?.ConstructorArguments.FirstOrDefault().Value is not 0 ||
            AttributeValues.Get(layout, "Pack", 0) != 1 || AttributeValues.Get(layout, "Size", 0) != 0 ||
            AttributeValues.Get(layout, "CharSet", 1) != 1 ||
            named.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.InlineArrayAttribute"))
        {
            throw new InvalidOperationException("NativeLayout requires explicit StructLayout(LayoutKind.Sequential, Pack = 1) on every struct, without Size, CharSet or inline-array overrides.");
        }

        if (!visiting.Add(named))
        {
            throw new InvalidOperationException("NativeLayout cannot contain a recursive value layout.");
        }

        int size = 0;
        foreach (IFieldSymbol field in named.GetMembers().OfType<IFieldSymbol>().Where(static field => !field.IsStatic))
        {
            if (field.IsFixedSizeBuffer && field.Type is IPointerTypeSymbol pointer)
            {
                int elementSize = PrimitiveSize(pointer.PointedAtType);
                if (elementSize == 0 || field.FixedSize <= 0)
                {
                    throw new InvalidOperationException("NativeLayout fixed buffers require fixed-width numeric elements.");
                }

                size = checked(size + checked(elementSize * field.FixedSize));
            }
            else
            {
                size = checked(size + Size(field.Type, assembly, visiting, sizes));
            }
        }

        visiting.Remove(named);
        if (size == 0)
        {
            throw new InvalidOperationException("NativeLayout does not support empty structs.");
        }

        sizes.Add(named, size);

        return size;
    }

    /// <summary>
    /// Returns the CLR storage width only for representations with valid arbitrary bit patterns.
    /// </summary>
    private static int PrimitiveSize(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Byte or SpecialType.System_SByte => 1,
        SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 4,
        SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => 8,
        _ => 0,
    };
}

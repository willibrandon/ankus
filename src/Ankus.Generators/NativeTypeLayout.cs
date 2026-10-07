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
    internal static int Validate(INamedTypeSymbol type, out CustomTypeValidationFailure? failure,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (type.TypeKind != TypeKind.Struct)
            {
                throw Invalid(CustomTypeDiagnosticKind.NativeRoot, type.Locations.FirstOrDefault(), type.ToDisplayString());
            }

            int size = Size(type, type.ContainingAssembly, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default),
                new Dictionary<ITypeSymbol, int>(SymbolEqualityComparer.Default), type.Locations.FirstOrDefault(), cancellationToken);
            failure = null;
            return size;
        }
        catch (ValidationException exception)
        {
            failure = exception.Failure;
            return 0;
        }
        catch (OverflowException)
        {
            failure = new(CustomTypeDiagnosticKind.NativeTooLarge, type.Locations.FirstOrDefault(), new([type.ToDisplayString()]));
            return 0;
        }
    }

    /// <summary>
    /// Rejects process-specific representations and padding before adding field sizes.
    /// </summary>
    private static int Size(ITypeSymbol type, IAssemblySymbol assembly, HashSet<ITypeSymbol> visiting,
        Dictionary<ITypeSymbol, int> sizes, Location? location, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int primitive = PrimitiveSize(type);
        if (primitive != 0)
        {
            return primitive;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying })
        {
            return Size(underlying, assembly, visiting, sizes, location, cancellationToken);
        }

        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct, IsUnmanagedType: true, IsGenericType: false, IsRefLikeType: false } named ||
            !SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, assembly) || named.DeclaringSyntaxReferences.Length == 0)
        {
            throw Invalid(CustomTypeDiagnosticKind.NativeField, location, type.ToDisplayString());
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
            throw Invalid(CustomTypeDiagnosticKind.NativeStructLayout,
                layout?.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ?? location, named.ToDisplayString());
        }

        if (!visiting.Add(named))
        {
            throw Invalid(CustomTypeDiagnosticKind.NativeRecursive, location, named.ToDisplayString());
        }

        int size = 0;
        foreach (IFieldSymbol field in named.GetMembers().OfType<IFieldSymbol>().Where(static field => !field.IsStatic))
        {
            if (field.IsFixedSizeBuffer && field.Type is IPointerTypeSymbol pointer)
            {
                int elementSize = PrimitiveSize(pointer.PointedAtType);
                if (elementSize == 0 || field.FixedSize <= 0)
                {
                    throw Invalid(CustomTypeDiagnosticKind.NativeFixedBuffer, field.Locations.FirstOrDefault(), field.Name);
                }

                size = checked(size + checked(elementSize * field.FixedSize));
            }
            else
            {
                size = checked(size + Size(field.Type, assembly, visiting, sizes, field.Locations.FirstOrDefault(), cancellationToken));
            }
        }

        visiting.Remove(named);
        if (size == 0)
        {
            throw Invalid(CustomTypeDiagnosticKind.NativeEmpty, location, named.ToDisplayString());
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

    /// <summary>
    /// Creates a local unwind carrying a closed diagnostic instead of formatted free text.
    /// </summary>
    private static ValidationException Invalid(CustomTypeDiagnosticKind kind, Location? location, params string[] arguments)
        => new(new(kind, location, new(arguments)));

    /// <summary>
    /// Unwinds recursive layout validation while preserving the exact failed contract.
    /// </summary>
    private sealed class ValidationException(CustomTypeValidationFailure failure) : Exception
    {
        /// <summary>
        /// Gets the diagnostic to report at the custom-type boundary.
        /// </summary>
        internal CustomTypeValidationFailure Failure { get; } = failure;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators;

/// <summary>
/// Resolves a partial native callback property to one exact static managed handler.
/// </summary>
internal sealed class NativeCallbackDeclaration(IPropertySymbol property, IMethodSymbol handler, IMethodSymbol signature, int nativeSignature)
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS021", "Invalid PostgreSQL native callback", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: "https://willibrandon.github.io/ankus/raw-values/#managed-native-callbacks-and-hooks");

    /// <summary>
    /// Rejects handlers whose invocation can disappear during ordinary C# compilation.
    /// </summary>
    private static readonly DiagnosticDescriptor s_conditional = new(
        "ANKUS276", "Native callback handler cannot be conditional",
        "Native callback '{0}' cannot use conditional handler '{1}'; remove Conditional so every native invocation runs the handler",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: "https://willibrandon.github.io/ankus/raw-values/#managed-native-callbacks-and-hooks");

    /// <summary>
    /// Gets the defining partial property.
    /// </summary>
    internal IPropertySymbol Property { get; } = property;

    /// <summary>
    /// Gets the ordinary static method called within the generated exception boundary.
    /// </summary>
    internal IMethodSymbol Handler { get; } = handler;

    /// <summary>
    /// Gets the generated pointer's exact managed invocation signature.
    /// </summary>
    internal IMethodSymbol Signature { get; } = signature;

    /// <summary>
    /// Gets the canonical prototype index verified against the selected native graph when linking.
    /// </summary>
    internal int NativeSignature { get; } = nativeSignature;

    /// <summary>
    /// Gets the assembly-qualified identity which distinguishes independent callback properties.
    /// </summary>
    internal string Identity
    {
        get
        {
            using SHA256 hash = SHA256.Create();
            string identity = Property.ContainingAssembly.Identity + ":" + Property.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(identity)).Take(16)
                .Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>
    /// Identifies a native callback property attribute.
    /// </summary>
    /// <param name="attribute">The semantic attribute.</param>
    /// <returns>Whether it requests a static native callback.</returns>
    internal static bool IsAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgNativeCallbackAttribute";

    /// <summary>
    /// Validates declaration and handler contracts before creating a native import or managed dispatcher.
    /// </summary>
    /// <param name="property">The candidate property.</param>
    /// <param name="context">The diagnostic destination.</param>
    /// <returns>A complete callback declaration, or null after an error diagnostic.</returns>
    internal static NativeCallbackDeclaration? Create(IPropertySymbol property, GeneratorDiagnostics context)
    {
        AttributeData[] attributes = [.. property.GetAttributes().Where(IsAttribute)];
        if (attributes.Length != 1 || property.GetAttributes().Any(GucDeclaration.IsGucAttribute))
        {
            return Invalid("A native callback requires exactly one callback attribute and cannot also declare a GUC.");
        }

        if (!property.IsStatic || property.IsIndexer || property.RefKind != RefKind.None || property.GetMethod is null ||
            property.SetMethod is not null || !property.IsPartialDefinition || property.PartialImplementationPart is not null)
        {
            return Invalid("A native callback requires a static partial getter-only property without an existing implementation.");
        }

        for (INamedTypeSymbol? type = property.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsGenericType || type.IsFileLocal ||
                type.DeclaringSyntaxReferences.Any(static reference => reference.GetSyntax() is not TypeDeclarationSyntax declaration ||
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                return Invalid("Native callback properties require non-generic, non-file-local partial classes or structs, including every containing type.");
            }
        }

        if (property.Type is not INamedTypeSymbol { IsUnmanagedType: true, IsGenericType: false, IsRefLikeType: false } value ||
            !IsNativeValue(value))
        {
            return Invalid("The property type must be a generated native function pointer with a complete fixed Invoke signature.");
        }

        AttributeData[] metadata = [.. value.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "Ankus.CompilerServices.NativeFunctionPointerAttribute")];
        IMethodSymbol[] signatures = [.. value.GetMembers("Invoke").OfType<IMethodSymbol>().Where(static method =>
            !method.IsStatic && method.DeclaredAccessibility == Accessibility.Public && IsSignature(method))];
        if (metadata.Length != 1 || metadata[0].ConstructorArguments.Length != 1 ||
            metadata[0].ConstructorArguments[0].Value is not int index || index < 0 || signatures.Length != 1 ||
            !value.InstanceConstructors.Any(static method => method.DeclaredAccessibility == Accessibility.Public &&
                method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.None &&
                method.Parameters[0].Type is IPointerTypeSymbol { PointedAtType.SpecialType: SpecialType.System_Void }))
        {
            return Invalid("The property type must be a generated native function pointer with a complete fixed Invoke signature.");
        }

        if (attributes[0].ConstructorArguments.Length != 1 || attributes[0].ConstructorArguments[0].Value is not string name ||
            string.IsNullOrWhiteSpace(name))
        {
            return Invalid("A native callback requires the name of a static handler in its containing type.");
        }

        IMethodSymbol signature = signatures[0];
        IMethodSymbol[] handlers = [.. property.ContainingType.GetMembers(name).OfType<IMethodSymbol>().Where(method =>
            method.MethodKind == MethodKind.Ordinary && method.IsStatic && !method.IsAbstract && !method.IsExtern &&
            !method.IsAsync && method.PartialImplementationPart?.IsAsync != true &&
            (!method.IsPartialDefinition || method.PartialImplementationPart is not null) && IsSignature(method) &&
            !method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() ==
                "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute") &&
            SymbolEqualityComparer.Default.Equals(method.ReturnType, signature.ReturnType) &&
            method.Parameters.Length == signature.Parameters.Length &&
            method.Parameters.Zip(signature.Parameters, static (actual, expected) =>
                SymbolEqualityComparer.Default.Equals(actual.Type, expected.Type)).All(static same => same))];
        if (handlers.Length != 1)
        {
            return Invalid("The handler must resolve to one synchronous, non-generic static method whose by-value parameters and return type exactly match Invoke.");
        }

        AttributeData? conditional = handlers[0].GetAttributes()
            .Concat(handlers[0].PartialImplementationPart?.GetAttributes() ?? [])
            .FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute");
        if (conditional is not null)
        {
            context.Report(s_conditional,
                conditional.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? handlers[0].Locations.FirstOrDefault(),
                property.Name, handlers[0].Name);
            return null;
        }

        return new(property, handlers[0], signature, index);

        NativeCallbackDeclaration? Invalid(string message)
        {
            context.Report(s_invalid, property.Locations.FirstOrDefault(), property.Name, message);
            return null;
        }
    }

    /// <summary>
    /// Distinguishes generated native values from primitives and enums for exact storage transport.
    /// </summary>
    /// <param name="type">The semantic value type.</param>
    /// <returns>Whether the value declares the native storage contract.</returns>
    internal static bool IsNativeValue(ITypeSymbol type)
        => type.AllInterfaces.Any(static contract => contract.ToDisplayString() == "Ankus.IPgNativeType");

    private static bool IsSignature(IMethodSymbol method)
        => !method.IsGenericMethod && !method.IsVararg && method.RefKind == RefKind.None &&
            (method.ReturnsVoid || IsValue(method.ReturnType)) &&
            method.Parameters.All(static parameter => parameter.RefKind == RefKind.None && IsValue(parameter.Type));

    private static bool IsValue(ITypeSymbol type)
        => type.IsUnmanagedType && !type.IsRefLikeType &&
            (type.SpecialType is SpecialType.System_Boolean or SpecialType.System_SByte or SpecialType.System_Byte or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr or SpecialType.System_UIntPtr or
                SpecialType.System_Single or SpecialType.System_Double || type.TypeKind is TypeKind.Enum or TypeKind.Pointer || IsNativeValue(type));
}

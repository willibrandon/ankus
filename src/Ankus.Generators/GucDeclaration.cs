using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators;

/// <summary>
/// Validates native-backed configuration properties and statically resolves their managed hooks.
/// </summary>
internal sealed class GucDeclaration
{
    private static readonly DiagnosticDescriptor s_invalid = new(
        "ANKUS014", "Invalid PostgreSQL configuration declaration", "'{0}': {1}", "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Gets the defining partial property.
    /// </summary>
    internal IPropertySymbol Property { get; private set; } = null!;

    /// <summary>
    /// Gets the exact PostgreSQL setting name.
    /// </summary>
    internal string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the stable transport kind: Boolean, integer, real, string, or enum.
    /// </summary>
    internal int Kind { get; private set; }

    /// <summary>
    /// Gets the validated boot value, using a dense ordinal for an enum.
    /// </summary>
    internal object? Default { get; private set; }

    /// <summary>
    /// Gets the retained short description.
    /// </summary>
    internal string ShortDescription { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional retained long description.
    /// </summary>
    internal string? LongDescription { get; private set; }

    /// <summary>
    /// Gets the stable PostgreSQL setting context.
    /// </summary>
    internal int Context { get; private set; }

    /// <summary>
    /// Gets the stable flag bits mapped through target PostgreSQL headers.
    /// </summary>
    internal int Flags { get; private set; }

    /// <summary>
    /// Gets the unit choice mapped through target PostgreSQL headers.
    /// </summary>
    internal int Unit { get; private set; }

    /// <summary>
    /// Gets the integer or real lower bound.
    /// </summary>
    internal object? Minimum { get; private set; }

    /// <summary>
    /// Gets the integer or real upper bound.
    /// </summary>
    internal object? Maximum { get; private set; }

    /// <summary>
    /// Gets declaration-ordered enum labels and their dense values, retaining aliases and hidden entries.
    /// </summary>
    internal List<(IFieldSymbol Field, string Name, int Ordinal, bool Hidden)> Labels { get; } = [];

    /// <summary>
    /// Gets the optional normalization and validation callback.
    /// </summary>
    internal IMethodSymbol? Check { get; private set; }

    /// <summary>
    /// Gets the optional accepted-value notification callback.
    /// </summary>
    internal IMethodSymbol? Assign { get; private set; }

    /// <summary>
    /// Gets the optional display callback.
    /// </summary>
    internal IMethodSymbol? Show { get; private set; }

    /// <summary>
    /// Gets whether registration or access can call managed hook code.
    /// </summary>
    internal bool HasHooks => Check is not null || Assign is not null || Show is not null;

    /// <summary>
    /// Gets the exact managed property type including reference nullability.
    /// </summary>
    internal string ManagedType => Property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

    /// <summary>
    /// Identifies one of the five typed GUC attributes.
    /// </summary>
    /// <param name="attribute">The semantic attribute.</param>
    /// <returns>Whether the attribute declares a configuration property.</returns>
    internal static bool IsGucAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() is "Ankus.PgGucBoolAttribute" or "Ankus.PgGucIntAttribute" or
            "Ankus.PgGucRealAttribute" or "Ankus.PgGucStringAttribute" or "Ankus.PgGucEnumAttribute";

    /// <summary>
    /// Reads a property contract and reports a precise diagnostic before generating code or native registration.
    /// </summary>
    /// <param name="property">The candidate property.</param>
    /// <param name="context">The generator diagnostic context.</param>
    /// <returns>A validated native declaration, or null on failure.</returns>
    internal static GucDeclaration? Create(IPropertySymbol property, SourceProductionContext context)
    {
        AttributeData[] attributes = [.. property.GetAttributes().Where(IsGucAttribute)];
        if (attributes.Length != 1)
        {
            return Invalid("A property must declare exactly one typed GUC attribute.");
        }

        if (!property.IsStatic || property.IsIndexer || property.RefKind != RefKind.None || property.GetMethod is null || property.SetMethod is not null ||
            !property.IsPartialDefinition || property.PartialImplementationPart is not null ||
            property.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
        {
            return Invalid("A GUC requires an accessible static partial getter-only property without an existing implementation.");
        }

        for (INamedTypeSymbol? type = property.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.TypeKind != TypeKind.Class || type.IsGenericType || type.IsFileLocal ||
                type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
                type.DeclaringSyntaxReferences.Any(static reference => reference.GetSyntax() is not TypeDeclarationSyntax declaration ||
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                return Invalid("GUC properties require accessible, non-generic, non-file-local partial classes, including every containing class.");
            }
        }

        AttributeData attribute = attributes[0];
        if (attribute.ConstructorArguments.Length != 3 || attribute.ConstructorArguments[0].Value is not string name ||
            !IsName(name) || attribute.ConstructorArguments[2].Value is not string description || !SqlText.IsText(description))
        {
            return Invalid("The name must be a valid dotted custom setting name, and its description must be nonnull valid text without zero characters.");
        }

        int kind = attribute.AttributeClass!.Name switch
        {
            "PgGucBoolAttribute" => 0,
            "PgGucIntAttribute" => 1,
            "PgGucRealAttribute" => 2,
            "PgGucStringAttribute" => 3,
            _ => 4,
        };
        SpecialType expected = kind switch
        {
            0 => SpecialType.System_Boolean,
            1 => SpecialType.System_Int32,
            2 => SpecialType.System_Double,
            3 => SpecialType.System_String,
            _ => SpecialType.None,
        };
        if (kind == 4 ? property.Type.TypeKind != TypeKind.Enum : property.Type.SpecialType != expected)
        {
            return Invalid("The property type must match its Boolean, Int32, Double, String, or enum GUC attribute exactly.");
        }

        var result = new GucDeclaration
        {
            Property = property, Name = name, Kind = kind,
            Default = attribute.ConstructorArguments[1].Value, ShortDescription = description,
            LongDescription = AttributeValues.Get<string?>(attribute, "LongDescription", null),
            Context = AttributeValues.Get(attribute, "Context", 6), Flags = AttributeValues.Get(attribute, "Flags", 0),
            Unit = AttributeValues.Get(attribute, "Unit", 0),
        };
        if (result.LongDescription is not null && !SqlText.IsText(result.LongDescription) || result.Context is < 0 or > 6 ||
            result.Flags < 0 || (result.Flags & ~1023) != 0 || result.Unit is < 0 or > 8 ||
            (result.Flags & 32) != 0 && kind != 3 || result.Unit != 0 && kind is not (1 or 2))
        {
            return Invalid("GUC context, flags, units, and description must be valid; IsName requires a string and units require an integer or real.");
        }

        switch (kind)
        {
            case 1:
                int minimum = AttributeValues.Get(attribute, "Minimum", int.MinValue);
                int maximum = AttributeValues.Get(attribute, "Maximum", int.MaxValue);
                if (result.Default is not int integer || integer < minimum || integer > maximum || minimum > maximum)
                {
                    return Invalid("Integer bounds must satisfy Minimum <= Default <= Maximum.");
                }

                result.Minimum = minimum;
                result.Maximum = maximum;
                break;
            case 2:
                double lower = AttributeValues.Get(attribute, "Minimum", double.MinValue);
                double upper = AttributeValues.Get(attribute, "Maximum", double.MaxValue);
                if (result.Default is not double real || double.IsNaN(real) || double.IsNaN(lower) || double.IsNaN(upper) ||
                    real < lower || real > upper || lower > upper)
                {
                    return Invalid("Real bounds must satisfy Minimum <= Default <= Maximum and cannot contain NaN.");
                }

                result.Minimum = lower;
                result.Maximum = upper;
                break;
            case 3:
                if (result.Default is string text && !SqlText.IsText(text) ||
                    result.Default is null && property.NullableAnnotation != NullableAnnotation.Annotated)
                {
                    return Invalid("String defaults must be valid text without zero characters; a null default requires a nullable string property.");
                }

                break;
            case 4:
                if (!SymbolEqualityComparer.Default.Equals(attribute.ConstructorArguments[1].Type, property.Type) || !result.ReadEnum())
                {
                    return Invalid("An enum default must be a declared value of the property's enum; labels must be valid and distinct under ASCII case folding.");
                }

                break;
        }

        foreach (string role in new[] { "Check", "Assign", "Show" })
        {
            string? hookName = AttributeValues.Get<string?>(attribute, role, null);
            if (hookName is null)
            {
                continue;
            }

            IMethodSymbol[] candidates = [.. property.ContainingType.GetMembers(hookName).OfType<IMethodSymbol>()];
            if (candidates.Length != 1 || !result.ValidateHook(candidates[0], role))
            {
                return Invalid(role + " must name one accessible synchronous non-generic static method with the exact typed hook signature.");
            }

            switch (role)
            {
                case "Check": result.Check = candidates[0]; break;
                case "Assign": result.Assign = candidates[0]; break;
                case "Show": result.Show = candidates[0]; break;
            }
        }

        return result;

        GucDeclaration? Invalid(string reason)
        {
            Error(property, context, reason);
            return null;
        }
    }

    /// <summary>
    /// Reports an invalid setting name or cross-declaration constraint.
    /// </summary>
    /// <param name="property">The offending declaration.</param>
    /// <param name="context">The generator diagnostic context.</param>
    /// <param name="reason">The actionable contract failure.</param>
    internal static void Error(IPropertySymbol property, SourceProductionContext context, string reason)
        => context.ReportDiagnostic(Diagnostic.Create(s_invalid, property.Locations.FirstOrDefault(), property.Name, reason));

    /// <summary>
    /// Maps PostgreSQL's ASCII-only case folding without changing high-bit Unicode characters.
    /// </summary>
    /// <param name="value">The setting or label name.</param>
    /// <returns>A deterministic comparison key.</returns>
    internal static string Fold(string value) => new([.. value.Select(static character => character is >= 'A' and <= 'Z' ? (char)(character + 32) : character)]);

    /// <summary>
    /// Validates the dotted custom identifier grammar without imposing SQL identifier truncation limits.
    /// </summary>
    private static bool IsName(string value)
    {
        if (!SqlText.IsText(value) || !value.Contains('.'))
        {
            return false;
        }

        foreach (string component in value.Split('.'))
        {
            if (component.Length == 0 || !IsStart(component[0]) ||
                component.Skip(1).Any(static character => !IsStart(character) && character is not (>= '0' and <= '9') && character != '$'))
            {
                return false;
            }
        }

        return true;

        static bool IsStart(char character) => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_' or >= '\u0080';
    }

    /// <summary>
    /// Builds a dense native enum table without narrowing the underlying managed values.
    /// </summary>
    private bool ReadEnum()
    {
        var values = new Dictionary<object, int>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (IFieldSymbol field in Property.Type.GetMembers().OfType<IFieldSymbol>().Where(static field => field.HasConstantValue))
        {
            AttributeData? label = field.GetAttributes().FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgGucLabelAttribute");
            string? name = label is null ? field.Name : label.ConstructorArguments.FirstOrDefault().Value as string;
            if (name is null || !SqlText.IsText(name) || !names.Add(Fold(name)))
            {
                return false;
            }

            if (!values.TryGetValue(field.ConstantValue!, out int ordinal))
            {
                ordinal = values.Count;
                values.Add(field.ConstantValue!, ordinal);
            }

            Labels.Add((field, name, ordinal, label is not null && AttributeValues.Get(label, "Hidden", false)));
        }

        if (Default is null || !values.TryGetValue(Default, out int boot))
        {
            return false;
        }

        Default = boot;
        return true;
    }

    /// <summary>
    /// Verifies a hook's invocation shape, nullable contract, and typed result.
    /// </summary>
    private bool ValidateHook(IMethodSymbol method, string role)
    {
        if (!method.IsStatic || method.MethodKind != MethodKind.Ordinary || method.IsGenericMethod || method.IsAsync ||
            method.PartialImplementationPart?.IsAsync == true || method.IsAbstract || method.IsExtern || method.IsVirtual ||
            method.ReturnsByRef || method.ReturnsByRefReadonly ||
            method.IsPartialDefinition && method.PartialImplementationPart is null ||
            method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) || method.Parameters.Length != 2 ||
            method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None || parameter.IsOptional || parameter.IsParams) ||
            method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() is
                "System.Diagnostics.ConditionalAttribute" or "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute") ||
            !SymbolEqualityComparer.IncludeNullability.Equals(method.Parameters[0].Type, Property.Type))
        {
            return false;
        }

        IParameterSymbol second = method.Parameters[1];
        if (role == "Check")
        {
            return second.Type.ToDisplayString() == "Ankus.PgGucSource" && method.ReturnNullableAnnotation != NullableAnnotation.Annotated &&
                method.ReturnType is INamedTypeSymbol result && result.OriginalDefinition.ToDisplayString() == "Ankus.PgGucCheckResult<T>" &&
                SymbolEqualityComparer.IncludeNullability.Equals(result.TypeArguments[0], Property.Type);
        }

        return second.Type is INamedTypeSymbol extra && extra.Name == "PgGucExtra" && extra.ContainingNamespace.ToDisplayString() == "Ankus" &&
            second.NullableAnnotation == NullableAnnotation.Annotated && (role == "Assign" ? method.ReturnsVoid :
                method.ReturnType.SpecialType == SpecialType.System_String && method.ReturnNullableAnnotation != NullableAnnotation.Annotated);
    }
}

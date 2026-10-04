using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Preserves imported GUC label strings before Roslyn trims trailing zero characters.
/// </summary>
internal static class GucEnumMetadata
{
    /// <summary>
    /// Reads exact field labels from the compiler-owned reference without loading its assembly.
    /// </summary>
    /// <param name="type">The referenced enum's exact semantic identity.</param>
    /// <param name="compilation">The current compilation owning reference images.</param>
    /// <param name="cancellationToken">Cancels metadata traversal.</param>
    /// <returns>The exact serialized labels, or null when no portable image supplies the enum.</returns>
    internal static Dictionary<string, string?>? ReadLabels(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken)
    {
        MetadataReference? reference = compilation.GetMetadataReference(type.ContainingAssembly) ??
            compilation.References.FirstOrDefault(item => SymbolEqualityComparer.Default.Equals(
                compilation.GetAssemblyOrModuleSymbol(item), type.ContainingModule));
        if (reference is not PortableExecutableReference image)
        {
            return null;
        }

        try
        {
            ImmutableArray<ModuleMetadata> modules = image.GetMetadata() switch
            {
                AssemblyMetadata assembly => assembly.GetModules(),
                ModuleMetadata module => [module],
                _ => [],
            };
            string identity = Identity(type);
            foreach (ModuleMetadata module in modules)
            {
                MetadataReader reader = module.GetMetadataReader();
                foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Identity(reader, handle) != identity)
                    {
                        continue;
                    }

                    var labels = new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (FieldDefinitionHandle fieldHandle in reader.GetTypeDefinition(handle).GetFields())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                        foreach (CustomAttributeHandle attributeHandle in field.GetCustomAttributes())
                        {
                            CustomAttribute attribute = reader.GetCustomAttribute(attributeHandle);
                            if (!IsLabelConstructor(reader, attribute.Constructor))
                            {
                                continue;
                            }

                            BlobReader value = reader.GetBlobReader(attribute.Value);
                            string fieldName = reader.GetString(field.Name);
                            string? label = value.ReadUInt16() == 1 ? ReadLabel(ref value) : null;
                            labels[fieldName] = labels.ContainsKey(fieldName) ? null : label;
                        }
                    }

                    return labels;
                }
            }
        }
        catch (BadImageFormatException)
        {
            // An unreadable label cannot satisfy a valid imported setting declaration.
            return null;
        }
        catch (IOException)
        {
            // The compiler owns reference I/O diagnostics; label validation still fails closed.
            return null;
        }
        catch (DecoderFallbackException)
        {
            // Malformed UTF-8 must not become replacement characters in a native label.
            return null;
        }

        return null;
    }

    /// <summary>
    /// Decodes one ECMA-335 serialized string with exact bytes and strict UTF-8 validation.
    /// </summary>
    private static string? ReadLabel(ref BlobReader value)
    {
        if (value.TryReadCompressedInteger(out int length))
        {
            return new UTF8Encoding(false, true).GetString(value.ReadBytes(length));
        }

        if (value.ReadByte() == byte.MaxValue)
        {
            return null;
        }

        throw new BadImageFormatException("The GUC label does not contain a valid serialized string.");
    }

    /// <summary>
    /// Forms the exact definition identity, including nested and generic container metadata names.
    /// </summary>
    private static string Identity(INamedTypeSymbol type)
        => type.ContainingType is { } container ? Identity(container) + "+" + type.MetadataName :
            (type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString() + ".") + type.MetadataName;

    /// <summary>
    /// Forms the equivalent identity directly from a reference definition.
    /// </summary>
    private static string Identity(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        TypeDefinitionHandle parent = type.GetDeclaringType();
        if (!parent.IsNil)
        {
            return Identity(reader, parent) + "+" + reader.GetString(type.Name);
        }

        string space = reader.GetString(type.Namespace);
        return (space.Length == 0 ? string.Empty : space + ".") + reader.GetString(type.Name);
    }

    /// <summary>
    /// Requires the exact label attribute and its one-string constructor before decoding metadata.
    /// </summary>
    private static bool IsLabelConstructor(MetadataReader reader, EntityHandle constructor)
    {
        EntityHandle owner;
        BlobHandle signature;
        StringHandle methodName;
        if (constructor.Kind == HandleKind.MemberReference)
        {
            MemberReference member = reader.GetMemberReference((MemberReferenceHandle)constructor);
            owner = member.Parent;
            signature = member.Signature;
            methodName = member.Name;
        }
        else if (constructor.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)constructor);
            owner = method.GetDeclaringType();
            signature = method.Signature;
            methodName = method.Name;
        }
        else
        {
            return false;
        }

        StringHandle name;
        StringHandle space;
        if (owner.Kind == HandleKind.TypeReference)
        {
            TypeReference type = reader.GetTypeReference((TypeReferenceHandle)owner);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return false;
            }

            name = type.Name;
            space = type.Namespace;
        }
        else if (owner.Kind == HandleKind.TypeDefinition)
        {
            TypeDefinition type = reader.GetTypeDefinition((TypeDefinitionHandle)owner);
            if (!type.GetDeclaringType().IsNil)
            {
                return false;
            }

            name = type.Name;
            space = type.Namespace;
        }
        else
        {
            return false;
        }

        if (!reader.StringComparer.Equals(methodName, ".ctor") || !reader.StringComparer.Equals(name, "PgGucLabelAttribute") ||
            !reader.StringComparer.Equals(space, "Ankus"))
        {
            return false;
        }

        BlobReader parameters = reader.GetBlobReader(signature);
        SignatureHeader header = parameters.ReadSignatureHeader();
        return header.Kind == SignatureKind.Method && header.IsInstance && !header.IsGeneric &&
            parameters.ReadCompressedInteger() == 1 && parameters.ReadSignatureTypeCode() == SignatureTypeCode.Void &&
            parameters.ReadSignatureTypeCode() == SignatureTypeCode.String && parameters.RemainingBytes == 0;
    }
}

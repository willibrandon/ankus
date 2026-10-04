using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators;

/// <summary>
/// Reads mapped SQL identities without Roslyn's imported-string normalization.
/// </summary>
internal static class MappedIdentityMetadata
{
    /// <summary>
    /// Preserves serialized strings and rejects malformed UTF-8 instead of replacing bytes.
    /// </summary>
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    /// <summary>
    /// Reads a datum or range attribute's exact name and optional schema from compiler-owned inputs.
    /// </summary>
    /// <param name="type">The selected closed managed carrier.</param>
    /// <param name="attribute">The selected default or exact mapping declaration.</param>
    /// <param name="compilation">The current compilation owning its reference images.</param>
    /// <param name="cancellationToken">Cancels metadata traversal.</param>
    /// <param name="name">The exact constructor name.</param>
    /// <param name="schema">The exact schema, or null when omitted or explicitly null.</param>
    /// <returns>Whether the complete attribute can be read without losing identity bytes.</returns>
    internal static bool TryRead(INamedTypeSymbol type, AttributeData attribute, Compilation compilation,
        CancellationToken cancellationToken, out string? name, out string? schema)
    {
        name = null;
        schema = null;
        if (attribute.AttributeClass?.ToDisplayString() is not ("Ankus.PgDatumTypeAttribute" or "Ankus.PgRangeTypeAttribute") ||
            (attribute.AttributeClass.Name == "PgDatumTypeAttribute" ? attribute.ConstructorArguments.Length is not (2 or 3) :
                attribute.ConstructorArguments.Length is not (1 or 2)))
        {
            return false;
        }

        int nameIndex = attribute.ConstructorArguments.Length - (attribute.AttributeClass?.Name == "PgDatumTypeAttribute" ? 2 : 1);
        name = attribute.ConstructorArguments[nameIndex].Value as string;
        schema = AttributeValues.Get<string?>(attribute, "Schema", null);
        if (type.DeclaringSyntaxReferences.Length != 0)
        {
            return true;
        }

        MetadataReference? reference = compilation.GetMetadataReference(type.ContainingAssembly) ??
            compilation.References.FirstOrDefault(item => SymbolEqualityComparer.Default.Equals(
                compilation.GetAssemblyOrModuleSymbol(item), type.ContainingModule));
        if (reference is CompilationReference)
        {
            // Source constants remain exact even when retargeted attribute data has no syntax reference.
            return true;
        }

        if (reference is not PortableExecutableReference image)
        {
            return false;
        }

        int ordinal = 0;
        bool found = false;
        foreach (AttributeData candidate in type.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute.AttributeClass))
            {
                continue;
            }

            if (ReferenceEquals(candidate, attribute))
            {
                found = true;
                break;
            }

            ordinal++;
        }

        if (!found || attribute.AttributeConstructor is null || attribute.AttributeClass is null)
        {
            return false;
        }

        try
        {
            ImmutableArray<ModuleMetadata> modules = image.GetMetadata() switch
            {
                AssemblyMetadata assembly => assembly.GetModules(),
                ModuleMetadata module => [module],
                _ => [],
            };
            string identity = MetadataTypeName.Create(type);
            string attributeIdentity = MetadataTypeName.Create(attribute.AttributeClass);
            foreach (ModuleMetadata module in modules)
            {
                MetadataReader reader = module.GetMetadataReader();
                if (!reader.StringComparer.Equals(reader.GetModuleDefinition().Name, type.ContainingModule.MetadataName))
                {
                    continue;
                }

                foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Identity(reader, handle) != identity)
                    {
                        continue;
                    }

                    int current = 0;
                    foreach (CustomAttributeHandle attributeHandle in reader.GetTypeDefinition(handle).GetCustomAttributes())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        CustomAttribute encoded = reader.GetCustomAttribute(attributeHandle);
                        if (!IsConstructor(reader, encoded.Constructor, attributeIdentity))
                        {
                            continue;
                        }

                        if (current++ != ordinal)
                        {
                            continue;
                        }

                        BlobReader value = reader.GetBlobReader(encoded.Value);
                        if (value.ReadUInt16() != 1)
                        {
                            return false;
                        }

                        foreach (IParameterSymbol parameter in attribute.AttributeConstructor.Parameters)
                        {
                            if (parameter.Type.SpecialType == SpecialType.System_String)
                            {
                                name = ReadString(ref value);
                            }
                            else if (parameter.Type.ToDisplayString() == "System.Type")
                            {
                                if (ReadString(ref value)?.Contains('\0') == true)
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                return false;
                            }
                        }

                        schema = null;
                        var named = new HashSet<string>(StringComparer.Ordinal);
                        int count = value.ReadUInt16();
                        for (int index = 0; index < count; index++)
                        {
                            if (value.ReadByte() != 0x54)
                            {
                                return false;
                            }

                            byte kind = value.ReadByte();
                            string? enumType = kind == 0x55 ? ReadString(ref value) : null;
                            string? property = ReadString(ref value);
                            if (property is null || !named.Add(property))
                            {
                                return false;
                            }

                            if (property == "Schema" && kind == 0x0e)
                            {
                                schema = ReadString(ref value);
                            }
                            else if (property == "Origin" && kind == 0x55 &&
                                enumType?.Split(',')[0] == "Ankus.PgTypeOrigin")
                            {
                                if (value.ReadInt32() != AttributeValues.Get(attribute, "Origin", 0))
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                return false;
                            }
                        }

                        return value.RemainingBytes == 0;
                    }

                    return false;
                }
            }
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Decodes an ECMA-335 SerString with exact contents and strict UTF-8.
    /// </summary>
    private static string? ReadString(ref BlobReader value)
    {
        if (value.TryReadCompressedInteger(out int length))
        {
            return s_utf8.GetString(value.ReadBytes(length));
        }

        if (value.ReadByte() == byte.MaxValue)
        {
            return null;
        }

        throw new BadImageFormatException("The mapping contains an invalid serialized string.");
    }

    /// <summary>
    /// Forms the same definition identity from a compiler-owned metadata table.
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
    /// Selects constructors of the exact mapping attribute before matching its semantic ordinal.
    /// </summary>
    private static bool IsConstructor(MetadataReader reader, EntityHandle handle, string identity)
    {
        EntityHandle owner;
        StringHandle name;
        if (handle.Kind == HandleKind.MemberReference)
        {
            MemberReference member = reader.GetMemberReference((MemberReferenceHandle)handle);
            owner = member.Parent;
            name = member.Name;
        }
        else if (handle.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            owner = method.GetDeclaringType();
            name = method.Name;
        }
        else
        {
            return false;
        }

        if (!reader.StringComparer.Equals(name, ".ctor"))
        {
            return false;
        }

        if (owner.Kind == HandleKind.TypeDefinition)
        {
            return Identity(reader, (TypeDefinitionHandle)owner) == identity;
        }

        if (owner.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        TypeReference type = reader.GetTypeReference((TypeReferenceHandle)owner);
        return type.ResolutionScope.Kind != HandleKind.TypeReference &&
            reader.GetString(type.Namespace) + "." + reader.GetString(type.Name) == identity;
    }
}

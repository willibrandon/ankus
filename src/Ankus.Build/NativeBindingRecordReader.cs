using System.Globalization;
using System.Runtime.InteropServices;

namespace Ankus.Build;

/// <summary>
/// Builds a finite selected-header graph using compiler type and canonical declaration identities.
/// </summary>
/// <param name="library">The worker's selected compiler library.</param>
/// <param name="unit">The live parsed translation unit.</param>
internal sealed unsafe class NativeBindingRecordReader(NativeClang library, NativeClangUnit unit)
{
    private readonly List<NativeClangType> _nativeTypes = [];
    private readonly List<NativeRecordType?> _types = [];
    private readonly List<(NativeClangCursor Cursor, NativeClangType Type)> _nativeDeclarations = [];
    private readonly List<NativeRecordDeclaration?> _declarations = [];
    private readonly Dictionary<uint, List<int>> _declarationHashes = [];
    private int _fieldCount;
    private int _constantCount;

    /// <summary>
    /// Collects all requested roots and transitive declarations before validating the finished graph.
    /// </summary>
    internal NativeRecordGraph Read(NativeHeaderTarget expectedTarget, IReadOnlyDictionary<string, NativeHeaderRequest> requests)
    {
        NativeClangCursor root = ((delegate* unmanaged[Cdecl]<nint, NativeClangCursor>)library.Export("clang_getTranslationUnitCursor"))(unit.DangerousGetHandle());
        List<NativeClangCursor> children = library.Children(root);
        NativeHeaderTarget target = ReadTarget(children, expectedTarget);
        var roots = new SortedDictionary<string, int>(StringComparer.Ordinal);
        NativeClangCursor[] aliases = [.. children.Where(cursor => cursor.Kind == 20 &&
            library.Name(cursor).StartsWith(NativeBindingHeaderParser.AliasPrefix, StringComparison.Ordinal)).OrderBy(library.Name, StringComparer.Ordinal)];
        foreach (NativeClangCursor alias in aliases)
        {
            string name = library.Name(alias)[NativeBindingHeaderParser.AliasPrefix.Length..];
            if (!requests.TryGetValue(name, out NativeHeaderRequest? expected) || roots.ContainsKey(name))
            {
                throw new FormatException("Unexpected or duplicate native record root.");
            }

            NativeClangCursor[] references = [.. library.Children(alias, recursive: true).Where(static cursor => cursor.Kind == 101)];
            if (references.Length != 1) { throw new FormatException("A native record root must reference exactly one symbol."); }

            NativeClangCursor declaration = CursorTransform(references[0], "clang_getCursorReferenced");
            if (declaration.Kind != (expected.IsFunction ? 8 : 9) || library.Name(declaration) != expected.NativeName)
            {
                throw new FormatException("Native record root disagrees with its header signature.");
            }

            roots.Add(name, TypeId(library.Type(declaration)));
        }

        if (roots.Count != requests.Count) { throw new FormatException("Missing requested native record roots."); }

        int typeIndex = 0;
        int declarationIndex = 0;
        while (typeIndex < _types.Count || declarationIndex < _declarations.Count)
        {
            while (typeIndex < _types.Count)
            {
                _types[typeIndex] = ReadType(_nativeTypes[typeIndex], root);
                typeIndex++;
            }

            if (declarationIndex < _declarations.Count)
            {
                (NativeClangCursor cursor, NativeClangType type) = _nativeDeclarations[declarationIndex];
                _declarations[declarationIndex] = ReadDeclaration(cursor, type);
                declarationIndex++;
            }
        }

        return new(target, roots, [.. _types.Select(static value => value ?? throw new FormatException("Missing native type observation."))],
            [.. _declarations.Select(static value => value ?? throw new FormatException("Missing native declaration observation."))]);
    }

    private NativeRecordType ReadType(NativeClangType type, NativeClangCursor root)
    {
        int canonical = TypeId(library.Transform(type, "clang_getCanonicalType"));
        NativeHeaderQualifiers qualifiers = NativeHeaderQualifiers.None;
        if (TypeFlag(type, "clang_isConstQualifiedType")) { qualifiers |= NativeHeaderQualifiers.Const; }

        if (TypeFlag(type, "clang_isVolatileQualifiedType")) { qualifiers |= NativeHeaderQualifiers.Volatile; }

        if (TypeFlag(type, "clang_isRestrictQualifiedType")) { qualifiers |= NativeHeaderQualifiers.Restrict; }

        string spelling = library.PrintType(type, root);
        if (spelling.Length == 0 || spelling.Length > 1_048_576) { throw new FormatException("Invalid native type spelling."); }

        string kind;
        string name = "";
        int? element = null;
        int? declaration = null;
        long? count = null;
        NativeRecordFunction? function = null;
        string? source = null;
        switch (type.Kind)
        {
            case 1 when IsTypeOf(spelling):
                if (canonical == TypeId(type)) { throw new FormatException("A native typeof expression has no resolved canonical type."); }

                kind = "typeof";
                element = canonical;
                break;
            case >= 2 and <= 23:
            case 30:
            case 31:
            case 32:
            case 39:
                kind = "scalar";
                name = library.Spelling(library.Transform(type, "clang_getUnqualifiedType"));
                break;
            case 100:
                kind = "complex";
                element = TypeId(library.Transform(type, "clang_getElementType"));
                break;
            case 101:
                kind = "pointer";
                element = TypeId(library.Transform(type, "clang_getPointeeType"));
                break;
            case 105:
            case 106:
                kind = type.Kind == 105 ? "record" : "enum";
                declaration = DeclarationId(type);
                break;
            case 107:
                kind = "alias";
                NativeClangCursor alias = library.Declaration(type);
                name = library.Name(alias);
                NativeBindingCDeclaration.ValidateName(name);
                element = TypeId(((delegate* unmanaged[Cdecl]<NativeClangCursor, NativeClangType>)library.Export("clang_getTypedefDeclUnderlyingType"))(alias));
                source = library.Print(alias);
                break;
            case 110:
            case 111:
                kind = "function";
                bool prototype = type.Kind == 111;
                int parameterCount = ((delegate* unmanaged[Cdecl]<NativeClangType, int>)library.Export("clang_getNumArgTypes"))(type);
                bool variadic = TypeFlag(type, "clang_isFunctionTypeVariadic");
                if (parameterCount < 0 || (!prototype && parameterCount != 0) || parameterCount > 100_000)
                {
                    throw new FormatException("Invalid native function parameter count.");
                }

                var parameters = new List<int>();
                for (uint i = 0; i < parameterCount; i++)
                {
                    parameters.Add(TypeId(((delegate* unmanaged[Cdecl]<NativeClangType, uint, NativeClangType>)library.Export("clang_getArgType"))(type, i)));
                }

                int convention = ((delegate* unmanaged[Cdecl]<NativeClangType, int>)library.Export("clang_getFunctionTypeCallingConv"))(type);
                if (convention is < 0 or >= 100) { throw new FormatException("Unavailable native calling convention."); }

                function = new(TypeId(library.Transform(type, "clang_getResultType")), parameters, variadic, prototype, convention);
                break;
            case 112:
            case 114:
                kind = "array";
                element = TypeId(library.Transform(type, "clang_getArrayElementType"));
                if (type.Kind == 112)
                {
                    long extent = library.Measure(type, "clang_getArraySize");
                    if (extent < 0) { throw new FormatException("Native array extent is unavailable or exceeds Int64."); }

                    count = extent;
                }

                break;
            case 113:
            case 176:
                kind = "vector";
                element = TypeId(library.Transform(type, "clang_getElementType"));
                count = library.Measure(type, "clang_getNumElements");
                if (count <= 0) { throw new FormatException("Invalid native vector extent."); }

                break;
            case 119:
                kind = "elaborated";
                element = TypeId(library.Transform(type, "clang_Type_getNamedType"));
                break;
            case 163:
                kind = "attributed";
                element = TypeId(library.Transform(type, "clang_Type_getModifiedType"));
                break;
            case 177:
                kind = "atomic";
                element = TypeId(library.Transform(type, "clang_Type_getValueType"));
                break;
            default:
                throw new FormatException($"Unsupported native record type kind {type.Kind}: {spelling}");
        }

        NativeClangType canonicalType = _nativeTypes[canonical];
        bool nonObject = canonicalType.Kind is 2 or 110 or 111;
        long? size = nonObject ? null : Layout(type, "clang_Type_getSizeOf");
        long? alignment = nonObject ? null : Layout(type, "clang_Type_getAlignOf");
        return new(kind, canonical, spelling, qualifiers, size, alignment, name, element, declaration, count, function, source);
    }

    /// <summary>
    /// Recognizes an unexposed typeof wrapper without stripping qualifiers through its resolved type.
    /// </summary>
    private static bool IsTypeOf(string spelling)
    {
        // clang_getUnqualifiedType can desugar typeof when its operand carries a qualifier.
        // Inspect the original spelling instead; canonical type data still supplies its representation.
        ReadOnlySpan<char> remaining = spelling.AsSpan();
        while (true)
        {
            int end = remaining.IndexOf(' ');
            if (end < 0 || remaining[..end] is not ("const" or "volatile" or "restrict")) { break; }

            remaining = remaining[(end + 1)..].TrimStart();
        }

        int opening = remaining.IndexOf('(');
        return opening >= 0 && remaining[..opening].TrimEnd() is "typeof" or "__typeof__" or "typeof_unqual" or "__typeof_unqual__";
    }

    private NativeRecordDeclaration ReadDeclaration(NativeClangCursor cursor, NativeClangType type)
    {
        string kind = cursor.Kind switch { 2 => "struct", 3 => "union", 5 => "enum", _ => throw new FormatException("Unsupported native tag declaration.") };
        bool anonymous = CursorFlag(cursor, "clang_Cursor_isAnonymous");
        string name = anonymous ? "" : library.Name(cursor);
        if (name.Length != 0)
        {
            // libclang gives an unnamed tag its typedef's name, but that name cannot be
            // used after 'struct', 'union' or 'enum'. Preserve only actual C tag names.
            string spelling = library.PrintType(type, cursor);
            if (spelling == name) { name = ""; }
            else if (spelling != kind + " " + name) { throw new FormatException("Unsupported native tag naming form: " + spelling); }
        }

        if (name.Length != 0) { NativeBindingCDeclaration.ValidateName(name); }

        long? size = Layout(type, "clang_Type_getSizeOf");
        long? alignment = Layout(type, "clang_Type_getAlignOf");
        bool complete = size.HasValue;
        if (complete != alignment.HasValue) { throw new FormatException("Inconsistent native tag completeness."); }

        var fields = new List<NativeRecordField>();
        var constants = new List<NativeRecordConstant>();
        int? underlying = null;
        if (kind == "enum")
        {
            NativeClangCursor definition = CursorTransform(cursor, "clang_getCursorDefinition");
            if (definition.Kind == 5) { cursor = definition; }

            NativeClangType representation = ((delegate* unmanaged[Cdecl]<NativeClangCursor, NativeClangType>)library.Export("clang_getEnumDeclIntegerType"))(cursor);
            if (representation.Kind != 0)
            {
                if (Layout(representation, "clang_Type_getSizeOf") is not (>= 1 and <= 8))
                {
                    throw new FormatException("Native enum constants exceed the supported 64-bit representation.");
                }

                underlying = TypeId(representation);
                int canonicalKind = library.Transform(representation, "clang_getCanonicalType").Kind;
                bool unsigned = canonicalKind is >= 4 and <= 12;
                foreach (NativeClangCursor member in library.Children(cursor).Where(static child => child.Kind == 7))
                {
                    if (++_constantCount > 1_000_000) { throw new InvalidDataException("Native record graph exceeds the enum constant limit."); }

                    string value = unsigned
                        ? ((delegate* unmanaged[Cdecl]<NativeClangCursor, ulong>)library.Export("clang_getEnumConstantDeclUnsignedValue"))(member).ToString(CultureInfo.InvariantCulture)
                        : ((delegate* unmanaged[Cdecl]<NativeClangCursor, long>)library.Export("clang_getEnumConstantDeclValue"))(member).ToString(CultureInfo.InvariantCulture);
                    constants.Add(new(library.Name(member), value));
                }
            }
        }
        else if (complete)
        {
            foreach (NativeClangCursor field in library.Fields(type))
            {
                if (++_fieldCount > 1_000_000) { throw new InvalidDataException("Native record graph exceeds the field limit."); }

                NativeClangType fieldType = library.Type(field);
                NativeClangCursor fieldDeclaration = library.Declaration(library.Transform(fieldType, "clang_getCanonicalType"));
                bool anonymousField = CursorFlag(fieldDeclaration, "clang_Cursor_isAnonymousRecordDecl");
                string fieldName = anonymousField ? "" : library.Name(field);
                if (fieldName.Length != 0) { NativeBindingCDeclaration.ValidateName(fieldName); }

                long offset = ((delegate* unmanaged[Cdecl]<NativeClangCursor, long>)library.Export("clang_Cursor_getOffsetOfField"))(field);
                int width = ((delegate* unmanaged[Cdecl]<NativeClangCursor, int>)library.Export("clang_getFieldDeclBitWidth"))(field);
                if (offset < 0 || width < -1) { throw new FormatException("Unavailable native field layout."); }

                fields.Add(new(fieldName, TypeId(fieldType), offset, width == -1 ? null : width, anonymousField, library.Print(field)));
            }
        }

        return new(kind, name, complete, size, alignment, fields, underlying, constants);
    }

    private int TypeId(NativeClangType type)
    {
        if (type.Kind == 0) { throw new FormatException("Invalid native record type."); }

        for (int i = 0; i < _nativeTypes.Count; i++)
        {
            if (((delegate* unmanaged[Cdecl]<NativeClangType, NativeClangType, uint>)library.Export("clang_equalTypes"))(_nativeTypes[i], type) != 0) { return i; }
        }

        if (_nativeTypes.Count == 100_000) { throw new InvalidDataException("Native record graph exceeds the type limit."); }

        _nativeTypes.Add(type);
        _types.Add(null);
        return _nativeTypes.Count - 1;
    }

    private int DeclarationId(NativeClangType type)
    {
        NativeClangCursor cursor = CursorTransform(library.Declaration(type), "clang_getCanonicalCursor");
        uint hash = ((delegate* unmanaged[Cdecl]<NativeClangCursor, uint>)library.Export("clang_hashCursor"))(cursor);
        if (!_declarationHashes.TryGetValue(hash, out List<int>? indices))
        {
            indices = [];
            _declarationHashes.Add(hash, indices);
        }

        foreach (int index in indices)
        {
            if (((delegate* unmanaged[Cdecl]<NativeClangCursor, NativeClangCursor, uint>)library.Export("clang_equalCursors"))(_nativeDeclarations[index].Cursor, cursor) != 0) { return index; }
        }

        if (_declarations.Count == 100_000) { throw new InvalidDataException("Native record graph exceeds the declaration limit."); }

        int next = _declarations.Count;
        indices.Add(next);
        _nativeDeclarations.Add((cursor, library.Transform(type, "clang_getUnqualifiedType")));
        _declarations.Add(null);
        return next;
    }

    private long? Layout(NativeClangType type, string function)
    {
        long value = library.Measure(type, function);
        return value >= 0 ? value : value == -2 ? null : throw new FormatException($"Unsupported native layout observation {value} for {library.Spelling(type)}.");
    }

    private bool TypeFlag(NativeClangType type, string function) => ((delegate* unmanaged[Cdecl]<NativeClangType, uint>)library.Export(function))(type) != 0;

    private bool CursorFlag(NativeClangCursor cursor, string function) => ((delegate* unmanaged[Cdecl]<NativeClangCursor, uint>)library.Export(function))(cursor) != 0;

    private NativeClangCursor CursorTransform(NativeClangCursor cursor, string function) => ((delegate* unmanaged[Cdecl]<NativeClangCursor, NativeClangCursor>)library.Export(function))(cursor);

    private NativeHeaderTarget ReadTarget(List<NativeClangCursor> children, NativeHeaderTarget expected)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        string? runtime = null;
        foreach (NativeClangCursor cursor in children)
        {
            if (cursor.Kind == 5)
            {
                foreach (NativeClangCursor member in library.Children(cursor).Where(static child => child.Kind == 7))
                {
                    string name = library.Name(member);
                    if (name is "ankus_header_pg_version" or "ankus_header_pointer_size" or "ankus_header_little_endian" or "ankus_header_clang_major" ||
                        NativeBindingNumericModel.IsFact(name))
                    {
                        long number = ((delegate* unmanaged[Cdecl]<NativeClangCursor, long>)library.Export("clang_getEnumConstantDeclValue"))(member);
                        if (number is < int.MinValue or > int.MaxValue || !values.TryAdd(name, (int)number))
                        {
                            throw new FormatException("Duplicate native record target constant.");
                        }
                    }
                }
            }
            else if (cursor.Kind == 9 && library.Name(cursor) == "ankus_header_runtime_identifier")
            {
                if (runtime is not null) { throw new FormatException("Duplicate native record runtime identifier."); }

                _ = library.Export("clang_EvalResult_dispose");
                nint evaluation = ((delegate* unmanaged[Cdecl]<NativeClangCursor, nint>)library.Export("clang_Cursor_Evaluate"))(cursor);
                if (evaluation == 0) { throw new FormatException("Unavailable native record runtime identifier."); }

                try
                {
                    int kind = ((delegate* unmanaged[Cdecl]<nint, int>)library.Export("clang_EvalResult_getKind"))(evaluation);
                    if (kind != 4) { throw new FormatException("Native record runtime identifier is not a string literal."); }

                    runtime = Marshal.PtrToStringUTF8(((delegate* unmanaged[Cdecl]<nint, nint>)library.Export("clang_EvalResult_getAsStr"))(evaluation));
                }
                finally { ((delegate* unmanaged[Cdecl]<nint, void>)library.Export("clang_EvalResult_dispose"))(evaluation); }
            }
        }

        if (values.GetValueOrDefault("ankus_header_pg_version") != expected.PostgresVersion ||
            NativeBindingHeaderTarget.Create(values, runtime, expected.PostgresVersion / 10000) != expected)
        {
            throw new FormatException("Native record library target does not match the selected header contract.");
        }

        return expected;
    }
}

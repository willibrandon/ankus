using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ankus.Generators;

/// <summary>
/// Generates managed dispatchers, native PostgreSQL error boundaries, and SQL declarations for attributed functions.
/// </summary>
[Generator]
public sealed class PgFunctionGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor s_invalidFunction = new(
        "ANKUS001", "Unsupported PostgreSQL function",
        "'{0}' must be an accessible, synchronous, non-generic static method using supported SQL types and at most 100 by-value parameters",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_invalidName = new(
        "ANKUS002", "Invalid PostgreSQL function name",
        "SQL name '{0}' must contain 1-63 lowercase ASCII letters, digits, or underscores, and its SQL signature must be unique",
        "Ankus", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// Registers semantic attribute discovery and deterministic extension source generation.
    /// </summary>
    /// <param name="context">The incremental generation context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<IMethodSymbol> functions = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgFunctionAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (attributeContext, _) => (IMethodSymbol)attributeContext.TargetSymbol);
        IncrementalValuesProvider<IMethodSymbol> operators = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgOperatorAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (attributeContext, _) => (IMethodSymbol)attributeContext.TargetSymbol);
        IncrementalValuesProvider<IMethodSymbol> casts = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgCastAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (attributeContext, _) => (IMethodSymbol)attributeContext.TargetSymbol);
        IncrementalValueProvider<ImmutableArray<IMethodSymbol>> methods = functions.Collect().Combine(operators.Collect()).Combine(casts.Collect())
            .Select(static (input, _) => input.Left.Left.AddRange(input.Left.Right).AddRange(input.Right)
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default).ToImmutableArray());
        IncrementalValuesProvider<INamedTypeSymbol> enums = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgEnumAttribute",
            static (node, _) => node is EnumDeclarationSyntax,
            static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        IncrementalValuesProvider<INamedTypeSymbol> schemas = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgSchemaAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        IncrementalValueProvider<ImmutableArray<AttributeData>> customSql = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.Assembly.GetAttributes().Where(static attribute => attribute.AttributeClass?.ToDisplayString() is
                "Ankus.PgSqlAttribute" or "Ankus.PgSqlFileAttribute").ToImmutableArray());
        IncrementalValueProvider<ImmutableArray<(string Path, string? Text)>> files = context.AdditionalTextsProvider
            .Select(static (file, token) => (file.Path, file.GetText(token)?.ToString())).Collect();
        IncrementalValueProvider<string> projectDirectory = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out string? path) ? path : string.Empty);
        context.RegisterSourceOutput(methods.Combine(schemas.Collect()).Combine(customSql).Combine(files).Combine(projectDirectory).Combine(enums.Collect()),
            static (output, input) => Generate(output, input.Left.Left.Left.Left.Left, input.Left.Left.Left.Left.Right,
                input.Left.Left.Left.Right, input.Left.Left.Right, input.Left.Right, input.Right));
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<IMethodSymbol> methods, ImmutableArray<INamedTypeSymbol> schemaTypes,
        ImmutableArray<AttributeData> customSql, ImmutableArray<(string Path, string? Text)> files, string projectDirectory, ImmutableArray<INamedTypeSymbol> enumTypes)
    {
        if (methods.IsEmpty && schemaTypes.IsEmpty && customSql.IsEmpty && enumTypes.IsEmpty)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var relatedNames = new HashSet<string>(StringComparer.Ordinal);
        var managed = new StringBuilder();
        var native = new StringBuilder(NativeBridge.Source);
        if (!methods.IsEmpty)
        {
            native.AppendLine(NativeBridge.ReadBuffers);
            native.AppendLine(NativeBridge.WriteBuffer);
            native.AppendLine(NativeGeometryTypes.Source);
            native.AppendLine(NativeExtendedTypes.Source);
            native.AppendLine(NativeTemporalTypes.Source);
            native.AppendLine(NativeEnumBridge.Source);
            native.AppendLine(NativeSpiBridge.Source);
            native.AppendLine(NativeEnumBridge.Operations);
            native.AppendLine(NativeRangeBridge.Source);
            native.AppendLine(NativeArrayBridge.Source);
            native.AppendLine(NativeTupleBridge.Source);
            native.AppendLine(NativeTupleBridge.Operations);
            native.AppendLine(NativeScalarFunctions.Source);
            native.AppendLine(NativeTemporalOperations.Source);
            native.AppendLine(NativeNumericOperations.Source);
            native.AppendLine(NativeNetworkOperations.Source);
            native.AppendLine(NativeGeometryOperations.Source);
            native.AppendLine(NativeRangeOperations.Source);
            native.AppendLine(NativeCursorBridge.Source);
            native.AppendLine(NativeSessionBridge.Source);
            native.AppendLine(NativeSqlHelpers.Source);
            native.AppendLine(NativeErrorBridge.Source);
            native.AppendLine(GuardedBackend.Source);
            if (methods.Any(static method => SetResult.IsSequence(method.ReturnType)))
            {
                native.AppendLine(NativeSetBridge.Source);
            }
        }

        var graph = new SqlGraph(context);
        var schemas = new Dictionary<string, SqlEntity>(StringComparer.Ordinal);
        bool fixedSchema = !schemaTypes.IsEmpty;
        foreach (INamedTypeSymbol type in schemaTypes.OrderBy(static type => type.ToDisplayString(), StringComparer.Ordinal))
        {
            (string Name, bool Create)? schema = FunctionDeclaration.ReadSchema(type, context);
            if (schema is { } declared)
            {
                if (!schemas.TryGetValue(declared.Name, out SqlEntity? entity))
                {
                    entity = new SqlEntity("0:schema:" + declared.Name, string.Empty, type.Locations.FirstOrDefault());
                    schemas.Add(declared.Name, entity);
                    graph.Add(entity);
                }

                if (declared.Create)
                {
                    entity.Sql = "CREATE SCHEMA IF NOT EXISTS " + SqlText.Identifier(declared.Name) + ";\n";
                }

                graph.Configure(entity, type.GetAttributes().First(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgSchemaAttribute"));
            }
        }

        fixedSchema |= !CustomSql.Add(customSql, files, projectDirectory, graph);
        var exports = new StringBuilder("Pg_magic_func\n");
        managed.AppendLine("// <auto-generated />");
        managed.AppendLine("#nullable enable");
        managed.AppendLine("namespace Ankus.Generated;");
        managed.AppendLine("internal static unsafe class ExtensionDispatchers");
        managed.AppendLine("{");

        var enumEntities = new Dictionary<string, SqlEntity>(StringComparer.Ordinal);
        var enumNames = new HashSet<string>(StringComparer.Ordinal);
        if (!enumTypes.IsEmpty)
        {
            managed.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            managed.AppendLine("    internal static void RegisterEnums()");
            managed.AppendLine("    {");
        }

        if (!methods.IsEmpty)
        {
            native.AppendLine("static bool ankus_enum_supported(Oid type)");
            native.AppendLine("{");
            native.AppendLine("    (void) type;");
        }

        foreach (INamedTypeSymbol type in enumTypes.OrderBy(static type => type.ToDisplayString(), StringComparer.Ordinal))
        {
            EnumDeclaration? enumeration = EnumDeclaration.Create(type, context);
            if (enumeration is null)
            {
                continue;
            }

            var entity = new SqlEntity("1:type:" + enumeration.Managed, enumeration.CreateSql(), type.Locations.FirstOrDefault());
            graph.Configure(entity, enumeration.Attribute);
            graph.Add(entity);
            if (!enumNames.Add(enumeration.Sql))
            {
                graph.Error(entity.Location, "Duplicate PostgreSQL enum type name " + enumeration.Sql + ".");
            }

            enumEntities.Add(enumeration.Managed, entity);
            if (enumeration.Schema is not null)
            {
                fixedSchema = true;
                if (schemas.TryGetValue(enumeration.Schema, out SqlEntity? schema))
                {
                    entity.Dependencies.Add(schema);
                }
            }

            enumeration.EmitRegistration(managed);
            if (!methods.IsEmpty)
            {
                enumeration.EmitNativeTypeCheck(native);
            }
        }

        if (!methods.IsEmpty)
        {
            native.AppendLine("    return false;");
            native.AppendLine("}");
            native.AppendLine();
        }

        if (!enumTypes.IsEmpty)
        {
            managed.AppendLine("    }");
            managed.AppendLine();
        }

        foreach (IMethodSymbol method in methods.OrderBy(static method => method.ToDisplayString(), StringComparer.Ordinal))
        {
            SetResult? set = SetResult.Create(method, context, out bool validSet);
            if (!validSet)
            {
                continue;
            }

            if (!CompositeReference.Validate(method, set, context))
            {
                continue;
            }

            if (!IsSupported(method, set))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_invalidFunction, method.Locations.FirstOrDefault(), method.Name));
                continue;
            }

            string name = GetSqlName(method);
            if (!NumericConstraint.Validate(method, context, set))
            {
                continue;
            }

            FunctionDeclaration? declaration = FunctionDeclaration.Create(method, name, context, set);
            if (declaration is null)
            {
                continue;
            }

            string signature = declaration.QualifiedName + "(" + string.Join(",", method.Parameters.Select(
                static parameter => FunctionType.Create(parameter)!.Sql)) + ")";
            if (!IsValidName(name) || !names.Add(signature))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_invalidName, method.Locations.FirstOrDefault(), name));
                continue;
            }

            string callback = GetCallbackName(method, name);
            var sql = new StringBuilder();
            if (set is null)
            {
                PgFunctionEmitter.Emit(method, declaration, callback, managed, native, sql, exports);
            }
            else
            {
                PgSetEmitter.Emit(method, declaration, set, callback, managed, native, sql, exports);
            }

            var entity = new SqlEntity("1:function:" + method.ToDisplayString(), sql.ToString(), method.Locations.FirstOrDefault());
            AttributeData? functionAttribute = method.GetAttributes().FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
            if (functionAttribute is not null)
            {
                graph.Configure(entity, functionAttribute);
            }

            graph.Add(entity);
            OperatorCastDeclaration.Add(method, declaration, entity, graph, relatedNames, context);
            foreach (FunctionType contract in method.Parameters.Select(static parameter => FunctionType.Create(parameter)!)
                .Concat(set?.Columns ?? [FunctionType.CreateResult(method)!]))
            {
                EnumDeclaration? enumeration = (contract.Element ?? contract).Enumeration;
                if (enumeration is not null && enumEntities.TryGetValue(enumeration.Managed, out SqlEntity? enumEntity))
                {
                    entity.Dependencies.Add(enumEntity);
                }

                if ((contract.Element ?? contract).Composite?.Schema is { } compositeSchema)
                {
                    fixedSchema = true;
                    if (schemas.TryGetValue(compositeSchema, out SqlEntity? schema))
                    {
                        entity.Dependencies.Add(schema);
                    }
                }
            }

            if (declaration.Schema is not null)
            {
                fixedSchema = true;
                if (schemas.TryGetValue(declaration.Schema, out SqlEntity? schema))
                {
                    entity.Dependencies.Add(schema);
                }
            }
        }

        managed.AppendLine("}");
        string? installation = graph.Emit();
        if (installation is null)
        {
            return;
        }

        context.AddSource("ExtensionDispatchers.g.cs", managed.ToString());
        string metadata = "// <auto-generated />\n" +
            Metadata("Ankus.NativeSource", native.ToString()) +
            Metadata("Ankus.Sql", installation.Length == 0 ? "-- No installable objects declared.\n" : installation) +
            Metadata("Ankus.Relocatable", fixedSchema ? "false" : "true") +
            Metadata("Ankus.Exports", exports.ToString());
        context.AddSource("ExtensionManifest.g.cs", metadata);
    }

    private static string Metadata(string key, string value)
        => $"[assembly: global::System.Reflection.AssemblyMetadata(\"{key}\", {SymbolDisplay.FormatLiteral(value, quote: true)})]\n";

    private static bool IsSupported(IMethodSymbol method, SetResult? set)
    {
        if (!method.IsStatic || method.IsAsync || method.IsGenericMethod || method.IsAbstract ||
            method.ReturnsByRef || method.ReturnsByRefReadonly ||
            (set is null && FunctionType.CreateResult(method) is null) || method.Parameters.Length > 100 ||
            method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
            method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None ||
                FunctionType.Create(parameter) is null ||
                (parameter.IsParams && FunctionType.Create(parameter)?.IsVector != true)))
        {
            return false;
        }

        for (INamedTypeSymbol? type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsGenericType || type.IsFileLocal ||
                type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetSqlName(IMethodSymbol method)
    {
        AttributeData? attribute = method.GetAttributes().FirstOrDefault(
            static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
        foreach (KeyValuePair<string, TypedConstant> argument in attribute?.NamedArguments ?? [])
        {
            if (argument.Key == "Name" && argument.Value.Value is string value)
            {
                return value;
            }
        }

        return SqlText.SnakeCase(method.Name);
    }

    private static bool IsValidName(string name)
        => name.Length is > 0 and <= 63 && name[0] is >= 'a' and <= 'z' or '_' &&
            name.All(static character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private static string GetCallbackName(IMethodSymbol method, string sqlName)
    {
        string identity = method.ContainingAssembly.Identity + ":" + method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        using SHA256 hash = SHA256.Create();
        byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(identity));
        string suffix = string.Concat(bytes.Take(16).Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
        return "ankus_managed_" + suffix + "_" + sqlName;
    }
}

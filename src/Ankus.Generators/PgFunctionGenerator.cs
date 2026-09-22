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
        context.RegisterSourceOutput(functions.Collect().Combine(schemas.Collect()).Combine(customSql).Combine(files).Combine(projectDirectory),
            static (output, input) => Generate(output, input.Left.Left.Left.Left, input.Left.Left.Left.Right,
                input.Left.Left.Right, input.Left.Right, input.Right));
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<IMethodSymbol> methods, ImmutableArray<INamedTypeSymbol> schemaTypes,
        ImmutableArray<AttributeData> customSql, ImmutableArray<(string Path, string? Text)> files, string projectDirectory)
    {
        if (methods.IsEmpty && schemaTypes.IsEmpty && customSql.IsEmpty)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var managed = new StringBuilder();
        var native = new StringBuilder(NativeBridge.Source);
        if (!methods.IsEmpty)
        {
            native.AppendLine(NativeBridge.ReadBuffers);
            native.AppendLine(NativeBridge.WriteBuffer);
            native.AppendLine(NativeGeometryTypes.Source);
            native.AppendLine(NativeExtendedTypes.Source);
            native.AppendLine(NativeTemporalTypes.Source);
            native.AppendLine(NativeSpiBridge.Source);
            native.AppendLine(NativeArrayBridge.Source);
            native.AppendLine(NativeScalarFunctions.Source);
            native.AppendLine(NativeTemporalOperations.Source);
            native.AppendLine(NativeNumericOperations.Source);
            native.AppendLine(NativeNetworkOperations.Source);
            native.AppendLine(NativeGeometryOperations.Source);
            native.AppendLine(NativeCursorBridge.Source);
            native.AppendLine(NativeSessionBridge.Source);
            native.AppendLine(NativeSqlHelpers.Source);
            native.AppendLine(NativeErrorBridge.Source);
            native.AppendLine(GuardedBackend.Source);
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

        foreach (IMethodSymbol method in methods.OrderBy(static method => method.ToDisplayString(), StringComparer.Ordinal))
        {
            if (!IsSupported(method))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_invalidFunction, method.Locations.FirstOrDefault(), method.Name));
                continue;
            }

            string name = GetSqlName(method);
            if (!NumericConstraint.Validate(method, context))
            {
                continue;
            }

            FunctionDeclaration? declaration = FunctionDeclaration.Create(method, name, context);
            if (declaration is null)
            {
                continue;
            }

            string signature = declaration.QualifiedName + "(" + string.Join(",", method.Parameters.Select(
                static parameter => FunctionType.Create(parameter.Type)!.Sql)) + ")";
            if (!IsValidName(name) || !names.Add(signature))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_invalidName, method.Locations.FirstOrDefault(), name));
                continue;
            }

            string callback = GetCallbackName(method, name);
            var sql = new StringBuilder();
            PgFunctionEmitter.Emit(method, declaration, callback, managed, native, sql, exports);
            var entity = new SqlEntity("1:function:" + method.ToDisplayString(), sql.ToString(), method.Locations.FirstOrDefault());
            graph.Configure(entity, method.GetAttributes().First(static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute"));
            graph.Add(entity);
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

    private static bool IsSupported(IMethodSymbol method)
    {
        if (!method.IsStatic || method.IsAsync || method.IsGenericMethod || method.IsAbstract ||
            method.ReturnsByRef || method.ReturnsByRefReadonly ||
            FunctionType.Create(method.ReturnType) is null || method.Parameters.Length > 100 ||
            method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
            method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None ||
                FunctionType.Create(parameter.Type) is null ||
                (parameter.IsParams && FunctionType.Create(parameter.Type)?.IsVector != true)))
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
        AttributeData attribute = method.GetAttributes().First(
            static attribute => attribute.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
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

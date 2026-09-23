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
        IncrementalValuesProvider<IMethodSymbol> triggers = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgTriggerAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (attributeContext, _) => (IMethodSymbol)attributeContext.TargetSymbol);
        IncrementalValuesProvider<IMethodSymbol> eventTriggers = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgEventTriggerAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (attributeContext, _) => (IMethodSymbol)attributeContext.TargetSymbol);
        IncrementalValuesProvider<IMethodSymbol> initializers = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgInitializeAttribute",
            static (_, _) => true,
            static (attributeContext, _) => attributeContext.TargetSymbol)
            .Where(static symbol => symbol is IMethodSymbol)
            .Select(static (symbol, _) => (IMethodSymbol)symbol);
        IncrementalValueProvider<ImmutableArray<IMethodSymbol>> methods = functions.Collect().Combine(operators.Collect()).Combine(casts.Collect())
            .Combine(triggers.Collect()).Combine(eventTriggers.Collect()).Combine(initializers.Collect())
            .Select(static (input, _) => input.Left.Left.Left.Left.Left.AddRange(input.Left.Left.Left.Left.Right).AddRange(input.Left.Left.Left.Right)
                .AddRange(input.Left.Left.Right).AddRange(input.Left.Right).AddRange(input.Right)
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default).ToImmutableArray());
        IncrementalValuesProvider<INamedTypeSymbol> enums = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgEnumAttribute",
            static (node, _) => node is EnumDeclarationSyntax,
            static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        IncrementalValuesProvider<INamedTypeSymbol> schemas = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgSchemaAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        IncrementalValuesProvider<INamedTypeSymbol> aggregates = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Ankus.PgAggregateAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        IncrementalValuesProvider<IPropertySymbol> gucs = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is PropertyDeclarationSyntax { AttributeLists.Count: > 0 } or IndexerDeclarationSyntax { AttributeLists.Count: > 0 },
            static (syntaxContext, token) => syntaxContext.SemanticModel.GetDeclaredSymbol((BasePropertyDeclarationSyntax)syntaxContext.Node, token) as IPropertySymbol)
            .Where(static property => property is not null && property.GetAttributes().Any(GucDeclaration.IsGucAttribute))
            .Select(static (property, _) => property!);
        IncrementalValueProvider<ImmutableArray<AttributeData>> customSql = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.Assembly.GetAttributes().Where(static attribute => attribute.AttributeClass?.ToDisplayString() is
                "Ankus.PgSqlAttribute" or "Ankus.PgSqlFileAttribute").ToImmutableArray());
        IncrementalValueProvider<ImmutableArray<AttributeData>> prefixes = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.Assembly.GetAttributes().Where(static attribute => attribute.AttributeClass?.ToDisplayString() ==
                "Ankus.PgGucPrefixAttribute").ToImmutableArray());
        IncrementalValueProvider<ImmutableArray<(string Path, string? Text)>> files = context.AdditionalTextsProvider
            .Select(static (file, token) => (file.Path, file.GetText(token)?.ToString())).Collect();
        IncrementalValueProvider<string> projectDirectory = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out string? path) ? path : string.Empty);
        context.RegisterSourceOutput(methods.Combine(schemas.Collect()).Combine(customSql).Combine(files).Combine(projectDirectory).Combine(enums.Collect()).Combine(aggregates.Collect()).Combine(gucs.Collect()).Combine(prefixes),
            static (output, input) => Generate(output, input.Left.Left.Left.Left.Left.Left.Left.Left, input.Left.Left.Left.Left.Left.Left.Left.Right,
                input.Left.Left.Left.Left.Left.Left.Right, input.Left.Left.Left.Left.Left.Right, input.Left.Left.Left.Left.Right,
                input.Left.Left.Left.Right, input.Left.Left.Right, input.Left.Right, input.Right));
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<IMethodSymbol> methods, ImmutableArray<INamedTypeSymbol> schemaTypes,
        ImmutableArray<AttributeData> customSql, ImmutableArray<(string Path, string? Text)> files, string projectDirectory,
        ImmutableArray<INamedTypeSymbol> enumTypes, ImmutableArray<INamedTypeSymbol> aggregateTypes, ImmutableArray<IPropertySymbol> gucProperties,
        ImmutableArray<AttributeData> prefixAttributes)
    {
        if (methods.IsEmpty && schemaTypes.IsEmpty && customSql.IsEmpty && enumTypes.IsEmpty && aggregateTypes.IsEmpty && gucProperties.IsEmpty && prefixAttributes.IsEmpty)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var relatedNames = new HashSet<string>(StringComparer.Ordinal);
        var managed = new StringBuilder();
        var native = new StringBuilder(NativeBridge.Source);
        ImmutableArray<string> prefixes = GucPrefixDeclaration.Read(prefixAttributes, context);
        var gucs = new List<GucDeclaration>();
        var gucNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (IPropertySymbol property in gucProperties.Distinct<IPropertySymbol>(SymbolEqualityComparer.Default))
        {
            GucDeclaration? guc = GucDeclaration.Create(property, context);
            if (guc is null)
            {
                continue;
            }

            if (!gucNames.Add(GucDeclaration.Fold(guc.Name)))
            {
                GucDeclaration.Error(property, context, "GUC names must be unique under PostgreSQL's ASCII case-insensitive comparison.");
                continue;
            }

            gucs.Add(guc);
        }

        gucs.Sort(static (left, right) => string.CompareOrdinal(GucDeclaration.Fold(left.Name), GucDeclaration.Fold(right.Name)));
        bool hasGucHooks = gucs.Any(static guc => guc.HasHooks);
        bool hasGucCheck = gucs.Any(static guc => guc.Check is not null);
        bool hasGucShow = gucs.Any(static guc => guc.Show is not null);
        bool hasFunctionCallbacks = !methods.IsEmpty || !aggregateTypes.IsEmpty;
        bool hasBackend = hasFunctionCallbacks || hasGucCheck;
        bool hasDispatchers = hasFunctionCallbacks || hasGucHooks;
        var aggregateMethods = new HashSet<IMethodSymbol>(aggregateTypes.SelectMany(AggregateDeclaration.SelectedMethods), SymbolEqualityComparer.Default);
        if (hasBackend)
        {
            native.AppendLine(NativeBridge.ReadBuffers);
            native.AppendLine(NativeBridge.WriteBuffer);
            native.AppendLine(NativeGeometryTypes.Source);
            native.AppendLine(NativeExtendedTypes.Source);
            native.AppendLine(NativeTemporalTypes.Source);
            native.AppendLine(NativeEnumBridge.Source);
            native.AppendLine(NativeSpiBridge.Source);
            native.AppendLine(NativeTriggerBridge.Declarations);
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
            if (hasFunctionCallbacks)
            {
                native.AppendLine(NativeErrorBridge.RaiseError);
            }

            native.AppendLine(NativeGucBridge.ReadBinding);
            native.AppendLine(GuardedBackend.Source);
            if (!aggregateTypes.IsEmpty)
            {
                native.AppendLine(NativeAggregateBridge.Source);
            }

            if (methods.Any(TriggerDeclaration.IsTrigger))
            {
                native.AppendLine(NativeTriggerBridge.Source);
            }

            if (methods.Any(EventTriggerDeclaration.IsEventTrigger))
            {
                native.AppendLine(NativeEventTriggerBridge.Source);
            }

            if (methods.Any(static method => SetResult.IsSequence(method.ReturnType)))
            {
                native.AppendLine(NativeSetBridge.Source);
            }
        }

        if (gucs.Count != 0)
        {
            native.AppendLine("#include <math.h>");
            native.AppendLine(NativeGucBridge.Declarations);
            native.AppendLine(NativeGucBridge.Registration);
            if (hasGucHooks && !hasBackend)
            {
                native.AppendLine(NativeErrorBridge.Declarations);
                native.AppendLine(NativeErrorBridge.Logging);
                native.AppendLine("typedef int (*AnkusExecute)(struct AnkusRequest *, struct AnkusResult *, AnkusError *);");
            }

            if (hasDispatchers)
            {
                native.AppendLine(NativeGucBridge.GetManagedDeclarations(hasGucCheck, hasGucHooks, hasGucShow));
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

        if (hasBackend)
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
            if (hasBackend)
            {
                enumeration.EmitNativeTypeCheck(native);
            }
        }

        if (hasBackend)
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

        IMethodSymbol? initializer = InitializeDeclaration.Select(methods, context);
        var registration = new StringBuilder();
        if (gucs.Count != 0)
        {
            var definitions = new List<string>();
            foreach (GucDeclaration guc in gucs)
            {
                string symbol = PgGucEmitter.Emit(guc, GetCallbackName(guc.Property.GetMethod!, "guc"), managed, native);
                definitions.Add(symbol);
                registration.AppendLine($"        ankus_guc_register(&{symbol});");
            }

            if (hasDispatchers)
            {
                native.AppendLine("static AnkusGuc *ankus_guc_definitions[] = { " + string.Join(", ", definitions.Select(static symbol => "&" + symbol)) + " };");
                native.AppendLine($"static const int ankus_guc_count = {definitions.Count};");
                native.AppendLine(NativeGucBridge.GetManagedSource(hasGucCheck, hasGucHooks, hasGucShow));
                registration.Insert(0, (hasBackend ? "        ankus_read_guc = ankus_guc_read;\n" : string.Empty) +
                    "        ankus_guc_prepare_encoding();\n");
            }

            context.AddSource("GucProperties.g.cs", PgGucEmitter.EmitProperties(gucs));
        }

        GucPrefixDeclaration.Emit(prefixes, native, registration);
        if (initializer is not null || gucs.Count != 0 || !prefixes.IsEmpty)
        {
            PgInitializeEmitter.Emit(initializer, initializer is null ? null : GetCallbackName(initializer, "initialize"),
                hasGucHooks, registration.ToString(), managed, native, exports);
        }

        foreach (IMethodSymbol method in methods.OrderBy(static method => method.ToDisplayString(), StringComparer.Ordinal))
        {
            if (aggregateMethods.Contains(method) || InitializeDeclaration.IsInitializer(method))
            {
                continue;
            }

            bool trigger = TriggerDeclaration.IsTrigger(method);
            bool eventTrigger = EventTriggerDeclaration.IsEventTrigger(method);
            bool contextParameter = trigger || eventTrigger;
            SetResult? set = null;
            if (eventTrigger)
            {
                if (!EventTriggerDeclaration.Validate(method, context))
                {
                    continue;
                }
            }
            else if (trigger)
            {
                if (!TriggerDeclaration.Validate(method, context))
                {
                    continue;
                }
            }
            else
            {
                set = SetResult.Create(method, context, out bool validSet);
                if (!validSet || !CompositeReference.Validate(method, set, context))
                {
                    continue;
                }

                if (!IsSupported(method, set))
                {
                    context.ReportDiagnostic(Diagnostic.Create(s_invalidFunction, method.Locations.FirstOrDefault(), method.Name));
                    continue;
                }
            }

            string name = GetSqlName(method);
            if (!contextParameter && !NumericConstraint.Validate(method, context, set))
            {
                continue;
            }

            FunctionDeclaration? declaration = FunctionDeclaration.Create(method, name, context, set, contextParameter);
            if (declaration is null)
            {
                continue;
            }

            string signature = declaration.QualifiedName + "(" + (contextParameter ? string.Empty : string.Join(",", method.Parameters.Select(
                static parameter => FunctionType.Create(parameter)!.Sql))) + ")";
            if (!IsValidName(name) || !names.Add(signature))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_invalidName, method.Locations.FirstOrDefault(), name));
                continue;
            }

            string callback = GetCallbackName(method, name);
            var sql = new StringBuilder();
            if (eventTrigger)
            {
                PgEventTriggerEmitter.Emit(method, declaration, callback, managed, native, sql, exports);
            }
            else if (trigger)
            {
                PgTriggerEmitter.Emit(method, declaration, callback, managed, native, sql, exports);
            }
            else if (set is null)
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
            if (!contextParameter)
            {
                OperatorCastDeclaration.Add(method, declaration, entity, graph, relatedNames, context);
            }

            IEnumerable<FunctionType> contracts = contextParameter ? [] : method.Parameters.Select(static parameter => FunctionType.Create(parameter)!)
                .Concat(set?.Columns ?? [FunctionType.CreateResult(method)!]);
            foreach (FunctionType contract in contracts)
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

        var supportFunctions = new Dictionary<string, (IMethodSymbol Method, SqlEntity Entity)>(StringComparer.Ordinal);
        foreach (INamedTypeSymbol type in aggregateTypes.OrderBy(static value => value.ToDisplayString(), StringComparer.Ordinal))
        {
            AggregateDeclaration? aggregate = AggregateDeclaration.Create(type, context);
            if (aggregate is null)
            {
                continue;
            }

            if (!names.Add(aggregate.Signature))
            {
                context.ReportDiagnostic(Diagnostic.Create(s_invalidName, type.Locations.FirstOrDefault(), aggregate.Name));
                continue;
            }

            var entity = new SqlEntity("2:aggregate:" + type.ToDisplayString(), PgAggregateEmitter.EmitAggregate(aggregate), type.Locations.FirstOrDefault());
            graph.Configure(entity, aggregate.Attribute);
            graph.Add(entity);
            AddSchemaDependency(entity, aggregate.Schema);
            foreach (AggregateHelper helper in aggregate.Helpers.Values)
            {
                if (supportFunctions.TryGetValue(helper.Signature, out (IMethodSymbol Method, SqlEntity Entity) existing) &&
                    SymbolEqualityComparer.Default.Equals(existing.Method, helper.Method))
                {
                    entity.Dependencies.Add(existing.Entity);
                    continue;
                }

                if (!names.Add(helper.Signature))
                {
                    context.ReportDiagnostic(Diagnostic.Create(s_invalidName, helper.Method.Locations.FirstOrDefault(), helper.Declaration.QualifiedName));
                    continue;
                }

                string callback = GetCallbackName(helper.Method, "aggregate_" + SqlText.SnakeCase(helper.Role));
                string helperSql = PgAggregateEmitter.EmitHelper(helper, callback, managed, native, exports);
                var support = new SqlEntity("1:aggregate-helper:" + type.ToDisplayString() + ":" + helper.Role, helperSql, helper.Method.Locations.FirstOrDefault());
                AttributeData? function = helper.Method.GetAttributes().FirstOrDefault(static item => item.AttributeClass?.ToDisplayString() == "Ankus.PgFunctionAttribute");
                if (function is not null)
                {
                    graph.Configure(support, function);
                }

                support.Requires.UnionWith(entity.Requires.Where(required => !support.Names.Contains(required)));
                graph.Add(support);
                supportFunctions.Add(helper.Signature, (helper.Method, support));
                entity.Dependencies.Add(support);
                AddSchemaDependency(support, helper.Declaration.Schema);
                foreach (AggregateType contract in helper.Types.Concat([helper.Result]))
                {
                    FunctionType? datum = contract.Datum;
                    EnumDeclaration? enumeration = (datum?.Element ?? datum)?.Enumeration;
                    if (enumeration is not null && enumEntities.TryGetValue(enumeration.Managed, out SqlEntity? enumEntity))
                    {
                        support.Dependencies.Add(enumEntity);
                    }

                    AddSchemaDependency(support, (datum?.Element ?? datum)?.Composite?.Schema);
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

        void AddSchemaDependency(SqlEntity entity, string? schema)
        {
            if (schema is not null)
            {
                fixedSchema = true;
                if (schemas.TryGetValue(schema, out SqlEntity? dependency))
                {
                    entity.Dependencies.Add(dependency);
                }
            }
        }
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

using System.Collections.Immutable;
using System.Globalization;
using Ankus.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Ankus.Generators;

public sealed partial class SpiInterpolationAnalyzer
{
    /// <summary>
    /// Follows the actual reaching command values and builder objects rather than every historical write.
    /// </summary>
    private sealed class SqlFlowAnalysis(INamedTypeSymbol spi, INamedTypeSymbol session, IAssemblySymbol core, CancellationToken cancellationToken)
    {
        private readonly Dictionary<(SyntaxTree Tree, TextSpan Span), bool> _results = [];
        private readonly Dictionary<ControlFlowGraph, FlowState?[]> _observed = [];
        private readonly Dictionary<int, ControlFlowGraph> _functions = [];
        private readonly Dictionary<int, IMethodSymbol> _functionSymbols = [];
        private readonly Dictionary<IMethodSymbol, ControlFlowGraph> _localFunctions = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ControlFlowGraph> _active = [];
        private ControlFlowGraph _graph = null!;
        private bool _report;

        /// <summary>
        /// Resolves reaching command values to diagnostic decisions after the control-flow entries converge.
        /// </summary>
        /// <param name="graph">The compiler-owned graph for the current operation block.</param>
        /// <returns>Exact source identities and whether their raw command construction is unsafe.</returns>
        internal Dictionary<(SyntaxTree Tree, TextSpan Span), bool> Analyze(ControlFlowGraph graph)
        {
            _ = Solve(graph, 0, graph.Blocks.Length - 1, new());
            _report = true;
            foreach (KeyValuePair<ControlFlowGraph, FlowState?[]> observed in _observed.ToArray())
            {
                _graph = observed.Key;
                foreach (BasicBlock block in observed.Key.Blocks)
                {
                    if (observed.Value[block.Ordinal] is { } state)
                    {
                        EvaluateBlock(block, state.Clone());
                    }
                }
            }

            return _results;
        }

        private FlowState Solve(ControlFlowGraph graph, int first, int last, FlowState initial)
        {
            ControlFlowGraph previousGraph = _graph;
            _graph = graph;
            foreach (IMethodSymbol function in graph.LocalFunctions)
            {
                _localFunctions[function] = graph.GetLocalFunctionControlFlowGraph(function, cancellationToken);
            }

            FlowState?[] incoming = new FlowState?[graph.Blocks.Length];
            Queue<int> work = new();
            incoming[first] = initial.Clone();
            work.Enqueue(first);
            FlowState? output = null;
            while (work.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int ordinal = work.Dequeue();
                BasicBlock block = graph.Blocks[ordinal];
                FlowState state = incoming[ordinal]!.Clone();
                foreach (IOperation operation in block.Operations)
                {
                    if (CanThrow(operation))
                    {
                        SeedHandlers(block.EnclosingRegion, state);
                    }

                    _ = Evaluate(operation, state);
                    if (CanThrow(operation))
                    {
                        SeedHandlers(block.EnclosingRegion, state);
                    }
                }

                if (block.BranchValue is { } branchValue)
                {
                    if (CanThrow(branchValue) || block.FallThroughSuccessor?.Semantics is ControlFlowBranchSemantics.Throw or ControlFlowBranchSemantics.Rethrow)
                    {
                        SeedHandlers(block.EnclosingRegion, state);
                    }

                    Value branchResult = Evaluate(branchValue, state);
                    if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return)
                    {
                        state.Returned = branchResult;
                    }
                }

                if (block.Kind == BasicBlockKind.Exit)
                {
                    output = output is null ? state : FlowState.Merge(output, state);
                }

                foreach (ControlFlowBranch branch in Successors(block))
                {
                    FlowState propagated = state.Clone();
                    foreach (ControlFlowRegion finalizer in branch.FinallyRegions)
                    {
                        propagated = Solve(graph, finalizer.FirstBlockOrdinal, finalizer.LastBlockOrdinal, propagated);
                    }

                    if (branch.Destination is { IsReachable: true } destination && destination.Ordinal >= first && destination.Ordinal <= last)
                    {
                        Seed(destination.Ordinal, propagated, destination.Ordinal <= ordinal);
                    }
                    else if (branch.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling)
                    {
                        output = output is null ? propagated : FlowState.Merge(output, propagated);
                    }
                }
            }

            if (!_observed.TryGetValue(graph, out FlowState?[]? seen))
            {
                seen = new FlowState?[graph.Blocks.Length];
                _observed.Add(graph, seen);
            }

            for (int index = first; index <= last; index++)
            {
                if (incoming[index] is { } current)
                {
                    seen[index] = seen[index] is { } old ? FlowState.Merge(old, current) : current;
                }
            }

            _graph = previousGraph;
            return output ?? initial.Clone();

            void Seed(int ordinal, FlowState state, bool backEdge = false)
            {
                FlowState propagated = state.Clone();
                if (backEdge && incoming[ordinal] is { } prior)
                {
                    propagated.Widen(prior);
                }

                FlowState merged = incoming[ordinal] is { } previous ? FlowState.Merge(previous, propagated) : propagated;
                if (incoming[ordinal] is null || !merged.Equivalent(incoming[ordinal]!))
                {
                    incoming[ordinal] = merged;
                    work.Enqueue(ordinal);
                }
            }

            void SeedHandlers(ControlFlowRegion? region, FlowState state)
            {
                for (; region is not null; region = region.EnclosingRegion)
                {
                    if (region.Kind == ControlFlowRegionKind.Try && region.EnclosingRegion is { Kind: ControlFlowRegionKind.TryAndCatch } parent)
                    {
                        foreach (ControlFlowRegion handler in parent.NestedRegions)
                        {
                            if (handler.Kind is ControlFlowRegionKind.Catch or ControlFlowRegionKind.FilterAndHandler &&
                                handler.FirstBlockOrdinal >= first && handler.LastBlockOrdinal <= last)
                            {
                                Seed(handler.FirstBlockOrdinal, state);
                            }
                        }
                    }
                }
            }
        }

        private static bool CanThrow(IOperation operation)
            => operation is IInvocationOperation or IObjectCreationOperation or IPropertyReferenceOperation or IAwaitOperation or
                IInterpolatedStringOperation or IThrowOperation or IDynamicInvocationOperation ||
                operation.ChildOperations.Any(static child => CanThrow(child));

        private void EvaluateBlock(BasicBlock block, FlowState state)
        {
            foreach (IOperation operation in block.Operations)
            {
                _ = Evaluate(operation, state);
            }

            if (block.BranchValue is { } condition)
            {
                Value value = Evaluate(condition, state);
                if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return)
                {
                    state.Returned = value;
                }
            }
        }

        private Value Evaluate(IOperation operation, FlowState state)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.ConstantValue.HasValue)
            {
                return operation.Type?.TypeKind == TypeKind.Enum ? Value.UnknownConstant() : Value.Constant(operation.ConstantValue.Value);
            }

            switch (operation)
            {
                case ILocalReferenceOperation reference:
                    return state.Locals.TryGetValue(reference.Local, out Value? local) ? local : Value.Raw();
                case IParameterReferenceOperation reference:
                    return state.Locals.TryGetValue(reference.Parameter, out Value? parameter) ? parameter : Value.Raw();
                case IFlowCaptureReferenceOperation reference:
                    return state.Captures.TryGetValue(reference.Id, out Value? captured) ? captured : Value.Raw();
                case IFlowCaptureOperation capture:
                    Value capturedValue = Evaluate(capture.Value, state);
                    state.Captures[capture.Id] = capturedValue;
                    if (capture.Value is ILocalReferenceOperation capturedLocal)
                    {
                        state.CaptureTargets[capture.Id] = [capturedLocal.Local];
                    }
                    else if (capture.Value is IParameterReferenceOperation capturedParameter)
                    {
                        state.CaptureTargets[capture.Id] = [capturedParameter.Parameter];
                    }

                    return capturedValue;
                case IExpressionStatementOperation statement:
                    return Evaluate(statement.Operation, state);
                case IParenthesizedOperation parentheses:
                    return Evaluate(parentheses.Operand, state);
                case IConversionOperation conversion:
                    Value converted = Evaluate(conversion.Operand, state);
                    return conversion.OperatorMethod is null ? converted : Value.Unspecified(converted);
                case IDelegateCreationOperation creation:
                    return Evaluate(creation.Target, state);
                case IFlowAnonymousFunctionOperation function:
                    int functionSite = function.Syntax.SpanStart;
                    _functions[functionSite] = _graph.GetAnonymousFunctionControlFlowGraph(function, cancellationToken);
                    _functionSymbols[functionSite] = function.Symbol;
                    return Value.Function(functionSite);
                case IMethodReferenceOperation reference when _localFunctions.TryGetValue(reference.Method.OriginalDefinition, out ControlFlowGraph? localGraph):
                    int localSite = reference.Method.DeclaringSyntaxReferences[0].Span.Start;
                    _functions[localSite] = localGraph;
                    _functionSymbols[localSite] = reference.Method.OriginalDefinition;
                    return Value.Function(localSite);
                case ISimpleAssignmentOperation assignment:
                    Value assigned = Evaluate(assignment.Value, state);
                    if (assignment.IsRef && assignment.Target is ILocalReferenceOperation alias && assignment.Value is ILocalReferenceOperation aliased)
                    {
                        state.Aliases[alias.Local] = state.Aliases.TryGetValue(aliased.Local, out ImmutableArray<ISymbol> targets) ? targets : [aliased.Local];
                    }

                    Assign(assignment.Target, assigned, state);
                    return assigned;
                case IDeconstructionAssignmentOperation assignment:
                    Value tupleValue = Evaluate(assignment.Value, state);
                    Assign(assignment.Target, tupleValue, state);
                    return tupleValue;
                case ITupleOperation tuple:
                    return Value.Array([.. tuple.Elements.Select(element => Evaluate(element, state))]);
                case IArrayCreationOperation { Initializer: { } initializer }:
                    return state.Allocate(operation.Syntax.SpanStart * 2,
                        Value.Array([.. initializer.ElementValues.Select(element => Evaluate(element, state))]));
                case IArrayElementReferenceOperation element:
                    Value elements = ReadBuilders(Evaluate(element.ArrayReference, state), state);
                    if (!elements.Elements.IsDefault && element.Indices.Length == 1 && Evaluate(element.Indices[0], state) is
                        { HasConstant: true, ConstantObject: int elementIndex } && elementIndex >= 0 && elementIndex < elements.Elements.Length)
                    {
                        return elements.Elements[elementIndex];
                    }

                    return Value.Raw();
                case ICompoundAssignmentOperation assignment:
                    Value combined = Join(Evaluate(assignment.Target, state), Evaluate(assignment.Value, state));
                    Assign(assignment.Target, combined, state);
                    return combined;
                case IInterpolatedStringOperation interpolation:
                    Value text = Value.Constant(string.Empty);
                    foreach (IInterpolatedStringContentOperation part in interpolation.Parts)
                    {
                        if (part is IInterpolatedStringTextOperation literal)
                        {
                            text = Join(text, Evaluate(literal.Text, state));
                        }
                        else if (part is IInterpolationOperation hole)
                        {
                            Value holeValue = Evaluate(hole.Expression, state);
                            text = Join(text, holeValue);
                            if (hole.Alignment is not null || hole.FormatString is not null)
                            {
                                text = text.WithUnsafe();
                            }
                        }
                    }

                    return text;
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String } addition:
                    return Join(Evaluate(addition.LeftOperand, state), Evaluate(addition.RightOperand, state));
                case IInvocationOperation invocation:
                    return Invoke(invocation, state);
                case IObjectCreationOperation { Constructor: { } constructor } creation when IsBuilder(constructor.ContainingType):
                    int site = creation.Syntax.SpanStart * 2;
                    List<(IArgumentOperation Argument, Value Value)> construction = [.. creation.Arguments.Select(argument =>
                        (Argument: argument, Value: Evaluate(argument.Value, state)))];
                    (IArgumentOperation Argument, Value Value) initial = construction.FirstOrDefault(static pair => pair.Argument.Parameter?.Name == "value");
                    Value initialText = initial.Argument is null ? Value.Constant(string.Empty) : Join(Value.Constant(string.Empty), initial.Value);
                    if (construction.Any(static pair => pair.Argument.Parameter?.Name == "startIndex"))
                    {
                        initialText = Slice(initialText, construction);
                    }

                    return state.Allocate(site, initialText);
                case IFieldReferenceOperation { Field: { Name: "Empty", ContainingType.SpecialType: SpecialType.System_String } }:
                    return Value.Constant(string.Empty);
            }

            foreach (IOperation child in operation.ChildOperations)
            {
                _ = Evaluate(child, state);
            }

            return Value.Raw();
        }

        private Value Invoke(IInvocationOperation invocation, FlowState state)
        {
            Value? receiver = invocation.Instance is { } instance ? Evaluate(instance, state) : null;
            List<(IArgumentOperation Argument, Value Value)> arguments = [];
            foreach (IArgumentOperation argument in invocation.Arguments)
            {
                arguments.Add((argument, Evaluate(argument.Value, state)));
            }


            IMethodSymbol method = invocation.TargetMethod;
            if (_localFunctions.TryGetValue(method.OriginalDefinition, out ControlFlowGraph? localFunction))
            {
                return Execute(localFunction, state, method.OriginalDefinition, arguments);
            }

            if (method.MethodKind == MethodKind.DelegateInvoke && receiver is { } functionValue)
            {
                FlowState? effects = null;
                Value? returned = null;
                foreach (int site in functionValue.Functions)
                {
                    FlowState called = state.Clone();
                    Value result = Execute(_functions[site], called, _functionSymbols[site], arguments);
                    effects = effects is null ? called : FlowState.Merge(effects, called);
                    returned = returned is null ? result : Value.Merge(returned, result);
                }

                if (effects is not null)
                {
                    state.Adopt(effects);
                }

                return returned ?? Value.Raw();
            }

            if (IsQuotedCall(invocation, spi))
            {
                return Value.Quote(method.Name == "QuoteLiteral" ? '\'' : '"');
            }

            if (IsBuilder(method.ContainingType) && receiver is { } builder)
            {
                Value current = ReadBuilders(builder, state);
                if (method.Name == "ToString" && arguments.Count == 0)
                {
                    return current;
                }

                if (method.Name == "ToString" && arguments.Count == 2)
                {
                    return Slice(current, arguments);
                }

                Value updated;
                switch (method.Name)
                {
                    case "Clear" when arguments.Count == 0:
                        updated = Value.Constant(string.Empty);
                        break;
                    case "Append" when arguments.Count == 1:
                        updated = Join(current, arguments[0].Value);
                        break;
                    case "AppendLine" when arguments.Count <= 1:
                        updated = Join(arguments.Count == 0 ? current : Join(current, arguments[0].Value), Value.Constant("\n"));
                        break;
                    case "AppendFormat":
                        updated = Join(current, Format(invocation, arguments, state));
                        break;
                    default:
                        updated = Value.Unspecified(current);
                        break;
                }

                foreach (int id in builder.References)
                {
                    state.Builders[id] = builder.References.Length == 1 && !state.Summaries.Contains(id) ? updated
                        : Value.Merge(state.Builders.TryGetValue(id, out Value? previous) ? previous : Value.Raw(), updated);
                }

                return builder;
            }

            if (method.Name == "Format" && method.ContainingType.SpecialType == SpecialType.System_String)
            {
                return Format(invocation, arguments, state);
            }

            if (_report && (SymbolEqualityComparer.Default.Equals(method.ContainingType, spi) ||
                SymbolEqualityComparer.Default.Equals(method.ContainingType, session)))
            {
                foreach ((IArgumentOperation argument, Value value) in arguments)
                {
                    if (argument.Parameter is { Name: "commandText", Type.SpecialType: SpecialType.System_String })
                    {
                        bool error = RequiresDiagnostic(value);
                        (SyntaxTree Tree, TextSpan Span) key = (argument.Value.Syntax.SyntaxTree, argument.Value.Syntax.Span);
                        _results[key] = error || _results.TryGetValue(key, out bool existing) && existing;
                    }
                }
            }

            foreach ((IArgumentOperation argument, _) in arguments)
            {
                if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
                {
                    Assign(argument.Value, Value.Unspecified(Evaluate(argument.Value, state)), state);
                }
            }

            foreach ((_, Value value) in arguments)
            {
                foreach (int site in value.References)
                {
                    if (state.Builders.TryGetValue(site, out Value? previous))
                    {
                        state.Builders[site] = Value.Unspecified(previous);
                    }
                }

                foreach (int site in value.Functions)
                {
                    FlowState called = state.Clone();
                    _ = Execute(_functions[site], called);
                    FlowState effects = SymbolEqualityComparer.Default.Equals(method.ContainingType, spi) && method.Name == "Connect"
                        ? called : FlowState.Merge(state, called);
                    foreach (KeyValuePair<ISymbol, Value> pair in effects.Locals)
                    {
                        state.Locals[pair.Key] = pair.Value;
                    }

                    foreach (KeyValuePair<int, Value> pair in effects.Builders)
                    {
                        state.Builders[pair.Key] = pair.Value;
                    }
                }
            }

            return Value.Raw();
        }

        private Value Execute(ControlFlowGraph function, FlowState state, IMethodSymbol? signature = null,
            List<(IArgumentOperation Argument, Value Value)>? arguments = null)
        {
            if (!_active.Add(function))
            {
                foreach (ISymbol symbol in state.Locals.Keys.ToArray())
                {
                    state.Locals[symbol] = Value.Unspecified(state.Locals[symbol]);
                }

                return Value.Raw().WithUnsafe();
            }

            FlowState initial = state.Clone();
            initial.Returned = null;
            if (signature is not null && arguments is not null)
            {
                foreach ((IArgumentOperation argument, Value value) in arguments)
                {
                    if (argument.Parameter is { } parameter && parameter.Ordinal < signature.Parameters.Length)
                    {
                        initial.Locals[signature.Parameters[parameter.Ordinal]] = value;
                    }
                }
            }

            FlowState result = Solve(function, 0, function.Blocks.Length - 1, initial);
            _active.Remove(function);
            foreach (KeyValuePair<ISymbol, Value> pair in result.Locals)
            {
                state.Locals[pair.Key] = pair.Value;
            }

            foreach (KeyValuePair<int, Value> pair in result.Builders)
            {
                state.Builders[pair.Key] = pair.Value;
            }

            state.Summaries.UnionWith(result.Summaries);
            if (signature is not null && arguments is not null)
            {
                foreach ((IArgumentOperation argument, _) in arguments)
                {
                    if (argument.Parameter is { RefKind: RefKind.Ref or RefKind.Out } parameter &&
                        result.Locals.TryGetValue(signature.Parameters[parameter.Ordinal], out Value? value))
                    {
                        Assign(argument.Value, value, state);
                    }
                }
            }

            return result.Returned ?? Value.Raw();
        }

        private Value Format(IInvocationOperation invocation, List<(IArgumentOperation Argument, Value Value)> arguments, FlowState state)
        {
            (IArgumentOperation Argument, Value Value) template = arguments.FirstOrDefault(static pair => pair.Argument.Parameter?.Name == "format");
            if (template.Argument?.Value.ConstantValue is not { HasValue: true, Value: string format } ||
                arguments.Any(pair => pair.Argument.Parameter?.Name == "provider" && !IsInvariantProvider(pair.Argument.Value, core)))
            {
                return Value.Raw().WithUnsafe();
            }

            try
            {
                // A template using no arguments cannot format their runtime values into SQL.
                return Value.Constant(string.Format(CultureInfo.InvariantCulture, format, []));
            }
            catch (FormatException)
            {
                // Continue with the actual arity and argument-use check.
            }

            List<Value> values = [];
            foreach ((IArgumentOperation argument, Value value) in arguments)
            {
                if (argument.Parameter?.Name is "format" or "provider")
                {
                    continue;
                }

                IOperation raw = argument.Value;
                while (raw is IConversionOperation { OperatorMethod: null } conversion)
                {
                    raw = conversion.Operand;
                }

                if (argument.Parameter?.Type is IArrayTypeSymbol)
                {
                    Value elements = value.References.IsEmpty ? value : ReadBuilders(value, state);
                    if (elements.Elements.IsDefault)
                    {
                        return Value.Raw().WithUnsafe();
                    }

                    values.AddRange(elements.Elements);
                    continue;
                }

                values.Add(value);
            }

            HashSet<int> used = [];
            object?[] probes = [.. Enumerable.Range(0, values.Count).Select(index => new FormatUse(index, used))];
            try
            {
                _ = string.Format(CultureInfo.InvariantCulture, format, probes);
            }
            catch (FormatException)
            {
                return Value.Known([]);
            }

            for (int index = 0; index < values.Count; index++)
            {
                if (!used.Contains(index))
                {
                    values[index] = Value.Constant(string.Empty);
                }
            }

            if (values.Any(static value => value.Unknown || value.Unsafe || !value.References.IsEmpty))
            {
                return Value.Raw().WithUnsafe();
            }

            List<Text> results = [];
            Compose(0, [], []);
            return Value.Known(results);

            void Compose(int index, List<object?> replacements, List<string> markers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (results.Count > Value.MaxLayouts)
                {
                    return;
                }

                if (index == values.Count)
                {
                    string command;
                    try
                    {
                        object?[] formattedValues = [.. replacements];
                        command = string.Format(CultureInfo.InvariantCulture, format, formattedValues);
                    }
                    catch (FormatException)
                    {
                        return;
                    }

                    List<int> positions = [];
                    foreach (string marker in markers)
                    {
                        int offset = 0;
                        while ((offset = command.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
                        {
                            positions.Add(offset - 1);
                            offset += marker.Length;
                        }
                    }

                    positions.Sort();
                    results.Add(new(command, [.. positions]));
                    return;
                }

                foreach (Text text in values[index].Texts)
                {
                    if (text.Positions.IsEmpty)
                    {
                        replacements.Add(values[index].HasConstant ? values[index].ConstantObject : text.Sql);
                        Compose(index + 1, replacements, markers);
                        replacements.RemoveAt(replacements.Count - 1);
                    }
                    else if (text.Positions.Length == 1 && text.Positions[0] == 0 && text.Sql is "'__ankus_fragment__'" or "\"__ankus_fragment__\"")
                    {
                        string marker = "$999999999999999999" + index.ToString("D10", CultureInfo.InvariantCulture);
                        while (format.IndexOf(marker, StringComparison.Ordinal) >= 0 ||
                            values.Any(value => value.Texts.Any(text => text.Sql.IndexOf(marker, StringComparison.Ordinal) >= 0)))
                        {
                            marker += "9";
                        }

                        replacements.Add(text.Sql[0] + marker + text.Sql[0]);
                        markers.Add(marker);
                        Compose(index + 1, replacements, markers);
                        markers.RemoveAt(markers.Count - 1);
                        replacements.RemoveAt(replacements.Count - 1);
                    }
                }
            }
        }

        private bool IsBuilder(INamedTypeSymbol type)
            => type.ToDisplayString() == "System.Text.StringBuilder" && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, core);

        private static Value Slice(Value text, List<(IArgumentOperation Argument, Value Value)> arguments)
        {
            Value? start = arguments.FirstOrDefault(static pair => pair.Argument.Parameter?.Name == "startIndex").Value;
            Value? length = arguments.FirstOrDefault(static pair => pair.Argument.Parameter?.Name == "length").Value;
            if (length is { HasConstant: true, ConstantObject: 0 })
            {
                return Value.Constant(string.Empty);
            }

            if (text.Unknown || text.Quoted || text.Unsafe || start is not { HasConstant: true, ConstantObject: int offset } ||
                length is not { HasConstant: true, ConstantObject: int count })
            {
                return Value.Unspecified(text);
            }

            return Value.Known(text.Texts.Where(value => offset >= 0 && count >= 0 && offset <= value.Sql.Length && count <= value.Sql.Length - offset)
                .Select(value => new Text(value.Sql.Substring(offset, count), [])));
        }

        private static bool RequiresDiagnostic(Value value)
            => value.Unsafe || value.Unknown && value.Quoted || value.Texts.Any(static text =>
                SpiSqlTokenLayout.Check(text.Sql, text.Positions, allowLiteralParameters: true, quotedFragments: true) != SqlInterpolationFailure.None);

        private static Value ReadBuilders(Value references, FlowState state)
            => references.References.Select(id => state.Builders.TryGetValue(id, out Value? value) ? value : Value.Raw())
                .Aggregate((Value?)null, static (current, value) => current is null ? value : Value.Merge(current, value)) ?? Value.Raw();

        private void Assign(IOperation target, Value value, FlowState state)
        {
            if (target is ILocalReferenceOperation local)
            {
                state.Locals[local.Local] = value;
                if (state.Aliases.TryGetValue(local.Local, out ImmutableArray<ISymbol> aliases))
                {
                    foreach (ISymbol alias in aliases)
                    {
                        state.Locals[alias] = value;
                    }
                }

                foreach (KeyValuePair<ISymbol, ImmutableArray<ISymbol>> pair in state.Aliases)
                {
                    if (pair.Value.Any(alias => SymbolEqualityComparer.Default.Equals(alias, local.Local)))
                    {
                        state.Locals[pair.Key] = value;
                    }
                }
            }
            else if (target is IParameterReferenceOperation parameter)
            {
                state.Locals[parameter.Parameter] = value;
            }
            else if (target is IFlowCaptureReferenceOperation capture)
            {
                state.Captures[capture.Id] = value;
                if (state.CaptureTargets.TryGetValue(capture.Id, out ImmutableArray<ISymbol> targets))
                {
                    foreach (ISymbol symbol in targets)
                    {
                        state.Locals[symbol] = targets.Length == 1 ? value : Value.Merge(state.Locals[symbol], value);
                    }
                }

            }
            else if (target is ITupleOperation tuple && !value.Elements.IsDefault && tuple.Elements.Length == value.Elements.Length)
            {
                for (int index = 0; index < tuple.Elements.Length; index++)
                {
                    Assign(tuple.Elements[index], value.Elements[index], state);
                }
            }
            else if (target is IArrayElementReferenceOperation element)
            {
                Value references = Evaluate(element.ArrayReference, state);
                Value index = element.Indices.Length == 1 ? Evaluate(element.Indices[0], state) : Value.Raw();
                foreach (int site in references.References)
                {
                    Value previous = state.Builders[site];
                    Value updated;
                    if (!previous.Elements.IsDefault && index is { HasConstant: true, ConstantObject: int offset } && offset >= 0 && offset < previous.Elements.Length)
                    {
                        updated = Value.Array(previous.Elements.SetItem(offset, value));
                    }
                    else
                    {
                        updated = Value.Unspecified(previous);
                    }

                    state.Builders[site] = references.References.Length == 1 && !state.Summaries.Contains(site)
                        ? updated : Value.Merge(previous, updated);
                }
            }
            else if (target is IPropertyReferenceOperation { Property.Name: "Length", Instance: { } instance } property &&
                IsBuilder(property.Property.ContainingType))
            {
                Value? references = instance is ILocalReferenceOperation localReference && state.Locals.TryGetValue(localReference.Local, out Value? builder)
                    ? builder : null;
                if (references is not null)
                {
                    foreach (int site in references.References)
                    {
                        Value previous = state.Builders[site];
                        Value updated = value is { HasConstant: true, ConstantObject: 0 }
                            ? Value.Constant(string.Empty) : Value.Unspecified(previous);
                        state.Builders[site] = references.References.Length == 1 && !state.Summaries.Contains(site)
                            ? updated : Value.Merge(previous, updated);
                    }
                }
            }
        }

        private static IEnumerable<ControlFlowBranch> Successors(BasicBlock block)
        {
            if (block.FallThroughSuccessor is { } falling)
            {
                yield return falling;
            }

            if (block.ConditionalSuccessor is { } conditional)
            {
                yield return conditional;
            }
        }

        private static Value Join(Value left, Value right)
        {
            bool unknown = left.Unknown || right.Unknown || !left.References.IsEmpty || !right.References.IsEmpty;
            bool unsafeText = left.Unsafe || right.Unsafe || left.Input || right.Input;
            if (unknown)
            {
                return new([], unsafeText, true, left.Quoted || right.Quoted, false, []);
            }

            return new([.. left.Texts.SelectMany(first => right.Texts.Select(second =>
                new Text(first.Sql + second.Sql, [.. first.Positions, .. second.Positions.Select(position => position + first.Sql.Length)]))).Take(Value.MaxLayouts + 1)],
                unsafeText, false, left.Quoted || right.Quoted, false, []);
        }

        /// <summary>
        /// Keeps inert SQL text and semantic positions of complete PostgreSQL-quoted fragments together.
        /// </summary>
        /// <param name="Sql">The literal text with inert complete-fragment surrogates.</param>
        /// <param name="Positions">The opening positions of those complete fragments.</param>
        private sealed record Text(string Sql, ImmutableArray<int> Positions);

        /// <summary>
        /// Observes framework formatting of inert values without executing any consumer formatter.
        /// </summary>
        private sealed class FormatUse(int index, HashSet<int> used) : IFormattable
        {
            /// <summary>
            /// Records the selected argument and returns inert text for syntax validation.
            /// </summary>
            /// <param name="format">The framework's requested format.</param>
            /// <param name="formatProvider">The framework's invariant provider.</param>
            /// <returns>An empty surrogate string.</returns>
            public string ToString(string? format, IFormatProvider? formatProvider)
            {
                used.Add(index);
                return string.Empty;
            }
        }

        /// <summary>
        /// Describes possible text layouts, external input and identities in the bounded abstract heap.
        /// </summary>
        /// <param name="texts">The exact known literal/quoted layouts.</param>
        /// <param name="unsafeText">Whether a path formats unquoted runtime data.</param>
        /// <param name="unknown">Whether complete text or placement is unresolved.</param>
        /// <param name="quoted">Whether complete quoting results must retain their token boundaries.</param>
        /// <param name="input">Whether an external value has not been quoted or bound.</param>
        /// <param name="references">The possible builder or array heap identities.</param>
        /// <param name="elements">The known tuple or array element values.</param>
        /// <param name="functions">The possible local callback identities.</param>
        private sealed class Value(ImmutableArray<Text> texts, bool unsafeText, bool unknown, bool quoted, bool input, ImmutableArray<int> references,
            ImmutableArray<Value> elements = default, ImmutableArray<int> functions = default)
        {
            /// <summary>
            /// Bounds exact alternatives before preserving a conservative unknown layout.
            /// </summary>
            internal const int MaxLayouts = 128;
            private readonly ImmutableArray<Text> _layouts = [.. texts.GroupBy(static text => TextKey(text), StringComparer.Ordinal)
                .Select(static group => group.First()).Take(MaxLayouts + 1)];
            /// <summary>
            /// Gets the exact alternatives when their bounded representation is retained.
            /// </summary>
            internal ImmutableArray<Text> Texts => _layouts.Length <= MaxLayouts ? _layouts : [];
            /// <summary>
            /// Gets whether a reaching path formats unquoted external data.
            /// </summary>
            internal bool Unsafe { get; } = unsafeText;
            /// <summary>
            /// Gets whether complete text or token placement remains unresolved.
            /// </summary>
            internal bool Unknown => unknown || _layouts.Length > MaxLayouts;
            /// <summary>
            /// Gets whether the value contains complete PostgreSQL-quoted fragments.
            /// </summary>
            internal bool Quoted { get; } = quoted;
            /// <summary>
            /// Gets whether the value is an unbound, unquoted external input.
            /// </summary>
            internal bool Input { get; } = input;
            /// <summary>
            /// Gets the possible identities of mutable builders or arrays.
            /// </summary>
            internal ImmutableArray<int> References { get; } = [.. references.Distinct().OrderBy(static item => item)];
            /// <summary>
            /// Gets the tuple or array contents, or a default array for unresolved contents.
            /// </summary>
            internal ImmutableArray<Value> Elements { get; } = elements;
            /// <summary>
            /// Gets the possible local callback targets.
            /// </summary>
            internal ImmutableArray<int> Functions { get; } = functions.IsDefault ? [] : functions;
            /// <summary>
            /// Gets whether the exact compiler constant is retained for inert framework formatting.
            /// </summary>
            internal bool HasConstant
            {
                get;
                private init;
            }

            /// <summary>
            /// Gets the inert compiler constant without invoking any consumer conversion.
            /// </summary>
            internal object? ConstantObject
            {
                get;
                private init;
            }

            /// <summary>
            /// Gets the deterministic comparison representation used by the flow worklist.
            /// </summary>
            internal string Key => string.Join("|", Texts.Select(static text => TextKey(text)).OrderBy(static item => item, StringComparer.Ordinal)) +
                $"\0{Unsafe},{Unknown},{Quoted},{Input}\0" + string.Join(",", References) +
                (Elements.IsDefault ? "" : string.Join(";", Elements.Select(static value => value.Key.Length + ":" + value.Key))) + "\0" + string.Join(",", Functions);

            private static string TextKey(Text text) => text.Sql.Length + ":" + text.Sql + ":" + string.Join(",", text.Positions);

            /// <summary>
            /// Preserves an inert compiler constant and its exact formatting type.
            /// </summary>
            internal static Value Constant(object? value) => new([new(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, [])], false, false, false, false, [])
            {
                HasConstant = true,
                ConstantObject = value,
            };

            /// <summary>
            /// Represents an external value that has not been quoted or bound.
            /// </summary>
            internal static Value Raw() => new([], false, true, false, true, []);
            /// <summary>
            /// Represents a compiler constant whose formatted text is not modeled.
            /// </summary>
            internal static Value UnknownConstant() => new([], false, true, false, false, []);
            /// <summary>
            /// Identifies one recent object in the abstract heap.
            /// </summary>
            internal static Value Builder(int site) => new([], false, false, false, false, [site]);
            /// <summary>
            /// Preserves known tuple or array elements independently of their formatting.
            /// </summary>
            internal static Value Array(ImmutableArray<Value> values) => new([], false, false, false, false, [], values);
            /// <summary>
            /// Identifies a local function or lambda without invoking consumer code.
            /// </summary>
            internal static Value Function(int site) => new([], false, false, false, false, [], functions: [site]);
            /// <summary>
            /// Marks an inert complete SQL atom produced by an actual PostgreSQL quoting method.
            /// </summary>
            internal static Value Quote(char quote) => new([new(quote + "__ankus_fragment__" + quote, [0])], false, false, true, false, []);
            /// <summary>
            /// Keeps bounded exact text alternatives and their complete-fragment requirements.
            /// </summary>
            internal static Value Known(IEnumerable<Text> values)
            {
                ImmutableArray<Text> bounded = [.. values.Take(MaxLayouts + 1)];
                return new(bounded, false, false, bounded.Any(static value => !value.Positions.IsEmpty), false, []);
            }

            /// <summary>
            /// Retains known hazards and identities after an unmodeled transformation.
            /// </summary>
            internal static Value Unspecified(Value previous) => new([], previous.Unsafe, true, previous.Quoted, true, previous.References);
            /// <summary>
            /// Marks a formatted unquoted value without discarding its other flow properties.
            /// </summary>
            internal Value WithUnsafe() => new(Texts, true, Unknown, Quoted, Input, References);
            /// <summary>
            /// Converges changing loop text while retaining hazards and object identities.
            /// </summary>
            internal Value Widen() => new([], Unsafe, true, Quoted, Input, References);
            /// <summary>
            /// Redirects aliases when a prior recent object moves into an allocation summary.
            /// </summary>
            internal Value ReplaceReference(int from, int to) => new(Texts, Unsafe, Unknown, Quoted, Input,
                [.. References.Select(reference => reference == from ? to : reference)],
                Elements.IsDefault ? default : [.. Elements.Select(value => value.ReplaceReference(from, to))], Functions)
            {
                HasConstant = HasConstant,
                ConstantObject = ConstantObject,
            };

            /// <summary>
            /// Unites alternative reaching values without treating one branch as certain.
            /// </summary>
            internal static Value Merge(Value left, Value right)
            {
                if (left.Key == right.Key)
                {
                    return left;
                }

                return new(left.Unknown || right.Unknown ? [] : [.. left.Texts.Concat(right.Texts).Take(MaxLayouts + 1)], left.Unsafe || right.Unsafe,
                    left.Unknown || right.Unknown, left.Quoted || right.Quoted, left.Input || right.Input, [.. left.References, .. right.References],
                    !left.Elements.IsDefault && !right.Elements.IsDefault && left.Elements.Length == right.Elements.Length
                        ? [.. left.Elements.Zip(right.Elements, static (first, second) => Merge(first, second))] : default,
                    [.. left.Functions.Concat(right.Functions).Distinct().OrderBy(static site => site)]);
            }
        }

        /// <summary>
        /// Keeps reaching variables, compiler captures, alias targets and mutable heap contents together.
        /// </summary>
        private sealed class FlowState
        {
            /// <summary>
            /// Gets reaching values of parameters and local variables using semantic symbol identity.
            /// </summary>
            internal Dictionary<ISymbol, Value> Locals { get; } = new(SymbolEqualityComparer.Default);
            /// <summary>
            /// Gets the values of compiler-created control-flow captures.
            /// </summary>
            internal Dictionary<CaptureId, Value> Captures { get; } = [];
            /// <summary>
            /// Gets the current contents of mutable builder and format-argument array objects.
            /// </summary>
            internal Dictionary<int, Value> Builders { get; } = [];
            /// <summary>
            /// Gets possible referents of scalar reference aliases.
            /// </summary>
            internal Dictionary<ISymbol, ImmutableArray<ISymbol>> Aliases { get; } = new(SymbolEqualityComparer.Default);
            /// <summary>
            /// Gets scalar assignment targets retained by lowered lvalue captures.
            /// </summary>
            internal Dictionary<CaptureId, ImmutableArray<ISymbol>> CaptureTargets { get; } = [];
            /// <summary>
            /// Gets allocation identities that can represent several older objects and require weak updates.
            /// </summary>
            internal HashSet<int> Summaries { get; } = [];
            /// <summary>
            /// Gets or sets the value produced by a reachable return branch.
            /// </summary>
            internal Value? Returned
            {
                get;
                set;
            }

            /// <summary>
            /// Applies possible call effects without replacing the caller's pending return value.
            /// </summary>
            internal void Adopt(FlowState other)
            {
                foreach (KeyValuePair<ISymbol, Value> pair in other.Locals)
                {
                    Locals[pair.Key] = pair.Value;
                }

                foreach (KeyValuePair<int, Value> pair in other.Builders)
                {
                    Builders[pair.Key] = pair.Value;
                }

                Summaries.UnionWith(other.Summaries);
            }

            /// <summary>
            /// Creates a recent heap object while preserving aliases to older objects from the same site.
            /// </summary>
            internal Value Allocate(int site, Value content)
            {
                if (Builders.TryGetValue(site, out Value? previous))
                {
                    int summary = site + 1;
                    Summaries.Add(summary);
                    Builders[summary] = Builders.TryGetValue(summary, out Value? older) ? Value.Merge(older, previous) : previous;
                    foreach (ISymbol symbol in Locals.Keys.ToArray())
                    {
                        Locals[symbol] = Locals[symbol].ReplaceReference(site, summary);
                    }

                    foreach (CaptureId capture in Captures.Keys.ToArray())
                    {
                        Captures[capture] = Captures[capture].ReplaceReference(site, summary);
                    }

                    foreach (int existingSite in Builders.Keys.ToArray())
                    {
                        Builders[existingSite] = Builders[existingSite].ReplaceReference(site, summary);
                    }
                }

                Builders[site] = content;
                return Value.Builder(site);
            }

            /// <summary>
            /// Creates independent reaching maps for one branch or call interpretation.
            /// </summary>
            internal FlowState Clone()
            {
                FlowState copy = new();
                Copy(Locals, copy.Locals);
                Copy(Captures, copy.Captures);
                Copy(Builders, copy.Builders);
                copy.Summaries.UnionWith(Summaries);
                copy.Returned = Returned;
                foreach (KeyValuePair<ISymbol, ImmutableArray<ISymbol>> pair in Aliases)
                {
                    copy.Aliases.Add(pair.Key, pair.Value);
                }

                foreach (KeyValuePair<CaptureId, ImmutableArray<ISymbol>> pair in CaptureTargets)
                {
                    copy.CaptureTargets.Add(pair.Key, pair.Value);
                }

                return copy;
            }

            /// <summary>
            /// Combines possible branch states, aliases, return values and heap summaries.
            /// </summary>
            internal static FlowState Merge(FlowState left, FlowState right)
            {
                FlowState merged = left.Clone();
                MergeMap(right.Locals, merged.Locals);
                MergeMap(right.Captures, merged.Captures);
                MergeMap(right.Builders, merged.Builders);
                merged.Summaries.UnionWith(right.Summaries);
                merged.Returned = merged.Returned is { } prior && right.Returned is { } returned ? Value.Merge(prior, returned) : merged.Returned ?? right.Returned;
                foreach (KeyValuePair<ISymbol, ImmutableArray<ISymbol>> pair in right.Aliases)
                {
                    merged.Aliases[pair.Key] = merged.Aliases.TryGetValue(pair.Key, out ImmutableArray<ISymbol> old)
                        ? [.. old.Concat(pair.Value).Distinct(SymbolEqualityComparer.Default)] : pair.Value;
                }

                foreach (KeyValuePair<CaptureId, ImmutableArray<ISymbol>> pair in right.CaptureTargets)
                {
                    merged.CaptureTargets[pair.Key] = merged.CaptureTargets.TryGetValue(pair.Key, out ImmutableArray<ISymbol> old)
                        ? [.. old.Concat(pair.Value).Distinct(SymbolEqualityComparer.Default)] : pair.Value;
                }

                return merged;
            }

            /// <summary>
            /// Converges changing values on a back edge without discarding known unsafe paths.
            /// </summary>
            internal void Widen(FlowState previous)
            {
                WidenMap(Locals, previous.Locals);
                WidenMap(Captures, previous.Captures);
                WidenMap(Builders, previous.Builders);
                if (Returned is { } value && previous.Returned is { } old && value.Key != old.Key)
                {
                    Returned = value.Widen();
                }
            }

            /// <summary>
            /// Tests equality of every component that can affect subsequent commands.
            /// </summary>
            internal bool Equivalent(FlowState other)
                => Same(Locals, other.Locals) && Same(Captures, other.Captures) && Same(Builders, other.Builders) &&
                    Returned?.Key == other.Returned?.Key &&
                    Summaries.SetEquals(other.Summaries) &&
                    Aliases.Count == other.Aliases.Count && Aliases.All(pair => other.Aliases.TryGetValue(pair.Key, out ImmutableArray<ISymbol> aliases) &&
                        pair.Value.Length == aliases.Length && pair.Value.All(item => aliases.Contains(item, SymbolEqualityComparer.Default))) &&
                    CaptureTargets.Count == other.CaptureTargets.Count && CaptureTargets.All(pair => other.CaptureTargets.TryGetValue(pair.Key, out ImmutableArray<ISymbol> targets) &&
                        pair.Value.Length == targets.Length && pair.Value.All(item => targets.Contains(item, SymbolEqualityComparer.Default)));

            private static void Copy<TKey>(Dictionary<TKey, Value> source, Dictionary<TKey, Value> target) where TKey : notnull
            {
                foreach (KeyValuePair<TKey, Value> pair in source)
                {
                    target.Add(pair.Key, pair.Value);
                }
            }

            private static void MergeMap<TKey>(Dictionary<TKey, Value> source, Dictionary<TKey, Value> target) where TKey : notnull
            {
                foreach (KeyValuePair<TKey, Value> pair in source)
                {
                    target[pair.Key] = target.TryGetValue(pair.Key, out Value? previous) ? Value.Merge(previous, pair.Value) : pair.Value;
                }
            }

            private static void WidenMap<TKey>(Dictionary<TKey, Value> current, Dictionary<TKey, Value> previous) where TKey : notnull
            {
                foreach (KeyValuePair<TKey, Value> pair in current.ToArray())
                {
                    if (previous.TryGetValue(pair.Key, out Value? old) && pair.Value.Key != old.Key)
                    {
                        current[pair.Key] = pair.Value.Widen();
                    }
                }
            }

            private static bool Same<TKey>(Dictionary<TKey, Value> left, Dictionary<TKey, Value> right) where TKey : notnull
                => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out Value? value) && pair.Value.Key == value.Key);
        }
    }
}

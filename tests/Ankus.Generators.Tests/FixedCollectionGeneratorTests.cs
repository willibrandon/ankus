using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Inline owners expose ordinary borrowed views and remain unmanaged shared-lock values.
    /// </summary>
    [TestMethod]
    public void FixedCollectionOwnersCompileWithSharedLocks()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            using Ankus;
            using System.Runtime.CompilerServices;
            using System.Diagnostics.CodeAnalysis;
            [InlineArray(4)]
            public struct Values { private int _element; }
            [InlineArray(4)]
            public struct Entries { private PgFixedMapEntry<int, long> _element; }
            public struct State
            {
                private Values _list, _deque, _indices;
                private Entries _entries;
                private int _listCount, _dequeCount, _head, _mapCount;
                [UnscopedRef] public PgFixedList<int> List() => new(_list, ref _listCount);
                [UnscopedRef] public PgFixedDeque<int> Deque() => new(_deque, ref _dequeCount, ref _head);
                [UnscopedRef] public PgFixedMap<int, long> Map() => new(_entries, _indices, ref _mapCount);
            }
            public static class Functions
            {
                private static readonly PgLwLock<State> Data = new("test.collections");
                [PgModuleLoad] public static void Load() => PgSharedMemory.Initialize(Data);
                [PgFunction] public static long Update(int key, long value)
                {
                    using PgLwLockExclusiveGuard<State> guard = Data.Exclusive();
                    State state = guard.Value;
                    state.List().Add(key);
                    state.Deque().PushFront(key);
                    state.Map().Set(key, value);
                    guard.Value = state;
                    return state.Map()[state.Deque().PopBack()];
                }
            }
            """);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
    }

    /// <summary>
    /// Native row conversion retains the originating function identity until typed result conversion finishes.
    /// </summary>
    [TestMethod]
    public void SetRowsRetainFunctionIdentityDuringCustomTypeConversion()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate("""
            using System.Collections.Generic;
            using Ankus;
            [PgType]
            public readonly record struct Item(int Value);
            public static class Functions
            {
                [PgFunction]
                public static IEnumerable<Item> Rows() => [new Item(73)];
            }
            """);
        AssertInitializationCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource");
        string next = native[native.IndexOf("ankus_set_next(AnkusSetState", StringComparison.Ordinal)..native.IndexOf("ankus_set_execute(FunctionCallInfo", StringComparison.Ordinal)];
        AssertOrdered(next, ["Oid previous_function = ankus_function_oid;", "ankus_function_oid = state->function;",
            "PG_TRY();", "ankus_set_call(state, 1", "ankus_parameter_datum(&cell)", "PG_FINALLY();",
            "ankus_function_oid = previous_function;", "ankus_set_release_row(state)"]);
    }

    /// <summary>
    /// The compiler rejects views that escape stack buffers or metadata, boxing and captured views.
    /// </summary>
    [TestMethod]
    [DataRow("public static PgFixedList<int> Escape() { Values values = default; return new(values, ref s_count); }", "CS8347")]
    [DataRow("public static PgFixedList<int> Escape() { int count = 0; return new(new int[4], ref count); }", "CS8168")]
    [DataRow("public static object Escape() => new PgFixedList<int>(new int[4], ref s_count);", "CS0029")]
    [DataRow("public static Action Escape() { var view = new PgFixedList<int>(new int[4], ref s_count); return () => view.Add(1); }", "CS8175")]
    public void FixedCollectionViewsPreserveBufferLifetimes(string declaration, string expected)
    {
        (Compilation compilation, _) = Generate($$"""
            using System;
            using Ankus;
            using System.Runtime.CompilerServices;
            [InlineArray(4)]
            public struct Values { private int _element; }
            public static class Functions
            {
                private static int s_count;
                {{declaration}}
            }
            """);
        Diagnostic[] errors = [.. compilation.GetDiagnostics(context.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Assert.Contains(expected, errors.Select(static diagnostic => diagnostic.Id),
            string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
    }
}

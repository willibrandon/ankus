using System.Diagnostics;
using Ankus.Testing;
using Npgsql;

namespace Ankus.IntegrationTests;

public sealed partial class ToolCommandTests
{
    /// <summary>
    /// The pgrx shared collection operations retain exact custom values across real Native AOT backends.
    /// </summary>
    [TestMethod]
    public async Task BoundedSharedCollectionsPreserveValuesAcrossBackends()
    {
        CancellationToken token = context.CancellationToken;
        string output = await PublishColdGucConsumerAsync("FixedCollections", "ankus_shared_probe", FixedCollectionSource, token);
        await using PostgresTestCluster cluster = await StartPublishedClusterAsync(output, token, sharedPreload: true);
        await using NpgsqlConnection first = await cluster.OpenConnectionAsync(token);
        await using NpgsqlConnection second = await cluster.OpenConnectionAsync(token);
        Assert.AreNotEqual(first.ProcessID, second.ProcessID);
        await ExecutePackageGucAsync(first, "CREATE EXTENSION ankus_shared_probe");
        Assert.AreEqual("0|0|0", await PackageGucScalarAsync(first, "SELECT collection_counts()"));
        Assert.AreEqual(DBNull.Value, await PackageGucScalarAsync(first, "SELECT vec_pop()::text"));
        Assert.AreEqual(DBNull.Value, await PackageGucScalarAsync(first, "SELECT deque_pop_front()::text"));
        Assert.AreEqual(DBNull.Value, await PackageGucScalarAsync(first, "SELECT deque_pop_back()::text"));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(first, "SELECT vec_push(11, -13)")));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(second, "SELECT vec_push(17, -19)")));
        Assert.AreEqual("11:-13,17:-19", await PackageGucScalarAsync(second, FixedCollectionRows("vec_select")));
        Assert.AreEqual(2, await PackageGucScalarAsync(second, "SELECT array_length(value, 1) FROM vec_arrays() AS item(value)"));
        await ExecutePackageGucAsync(first, "CREATE SCHEMA moved_collections; ALTER EXTENSION ankus_shared_probe SET SCHEMA moved_collections");
        await ExecutePackageGucAsync(second, "SET search_path = pg_catalog");
        Assert.AreEqual("11:-13,17:-19", await PackageGucScalarAsync(second, FixedCollectionRows("moved_collections.vec_select")));
        Assert.AreEqual(2, await PackageGucScalarAsync(second, "SELECT array_length(value, 1) FROM moved_collections.vec_arrays() AS item(value)"));
        await ExecutePackageGucAsync(first, "ALTER EXTENSION ankus_shared_probe SET SCHEMA public; DROP SCHEMA moved_collections");
        await ExecutePackageGucAsync(second, "RESET search_path");
        PostgresException rowFailure = await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            ExecutePackageGucAsync(second, "SELECT count(*) FROM vec_failed_rows()"));
        Assert.AreEqual("P7831", rowFailure.SqlState);
        Assert.AreEqual(1, await PackageGucScalarAsync(second, "SELECT vec_disposals()"));
        Assert.AreEqual("11:-13,17:-19", await PackageGucScalarAsync(second, FixedCollectionRows("vec_select")));
        await ExecutePackageGucAsync(first, "SELECT vec_modify_copy()");
        Assert.AreEqual("11:-13,17:-19", await PackageGucScalarAsync(second, FixedCollectionRows("vec_select")));
        Assert.AreEqual("17:-19", await PackageGucScalarAsync(first, FixedCollectionRows("vec_pop")));
        Assert.AreEqual("11:-13", await PackageGucScalarAsync(second, FixedCollectionRows("vec_drain")));
        Assert.AreEqual("0|0|0", await PackageGucScalarAsync(first, "SELECT collection_counts()"));

        await ExecutePackageGucAsync(first, "SELECT vec_fill()");
        Assert.AreEqual("400|0|0", await PackageGucScalarAsync(second, "SELECT collection_counts()"));
        Assert.IsFalse(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(second, "SELECT vec_push(401, -401)")));
        PostgresException full = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecutePackageGucAsync(first, "SELECT vec_require(401, -401)"));
        Assert.Contains("fixed list is full", full.MessageText);
        Assert.AreEqual("399:-399", await PackageGucScalarAsync(first, FixedCollectionRows("vec_pop")));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(second, "SELECT vec_push(73, 79)")));
        string expectedList = string.Join(',', Enumerable.Range(0, 399).Select(value => $"{value}:{-value}")) + ",73:79";
        Assert.AreEqual(expectedList, await PackageGucScalarAsync(second, FixedCollectionRows("vec_drain")));
        Assert.AreEqual("0|0|0", await PackageGucScalarAsync(first, "SELECT collection_counts()"));

        await ExecutePackageGucAsync(first, "SELECT deque_fill()");
        Assert.IsFalse(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(second, "SELECT deque_push_front(401, -401)")));
        Assert.IsFalse(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(second, "SELECT deque_push_back(401, -401)")));
        Assert.AreEqual("0:0", await PackageGucScalarAsync(first, FixedCollectionRows("deque_pop_front")));
        Assert.AreEqual("1:-1", await PackageGucScalarAsync(second, FixedCollectionRows("deque_pop_front")));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(second, "SELECT deque_push_back(400, -400)")));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(first, "SELECT deque_push_front(-1, 1)")));
        string expectedQueue = "-1:1," + string.Join(',', Enumerable.Range(2, 399).Select(value => $"{value}:{-value}"));
        Assert.AreEqual(expectedQueue, await PackageGucScalarAsync(second, FixedCollectionRows("deque_select")));
        Assert.AreEqual(expectedQueue, await PackageGucScalarAsync(first, FixedCollectionRows("deque_drain")));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(second, "SELECT deque_push_front(73, 79)")));
        Assert.AreEqual("73:79", await PackageGucScalarAsync(first, FixedCollectionRows("deque_pop_back")));
        Assert.AreEqual("0|0|0", await PackageGucScalarAsync(second, "SELECT collection_counts()"));

        await ExecutePackageGucAsync(first, "SELECT hash_set(11, 13); SELECT hash_set(17, 19); SELECT hash_set(23, 29); SELECT hash_set(31, 37)");
        Assert.AreEqual(19, await PackageGucScalarAsync(second, "SELECT hash_get(17)"));
        Assert.AreEqual(DBNull.Value, await PackageGucScalarAsync(second, "SELECT hash_get(41)"));
        Assert.AreEqual(19, await PackageGucScalarAsync(second, "SELECT hash_set(17, 43)"));
        Assert.AreEqual(43, await PackageGucScalarAsync(first, "SELECT hash_get(17)"));
        PostgresException fullMap = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecutePackageGucAsync(first, "SELECT hash_set(41, 47)"));
        Assert.Contains("fixed dictionary is full", fullMap.MessageText);
        Assert.AreEqual("11:13,17:43,23:29,31:37", await PackageGucScalarAsync(second, "SELECT hash_snapshot()"));
        Assert.AreEqual(43, await PackageGucScalarAsync(first, "SELECT hash_remove(17)"));
        Assert.AreEqual("11:13,31:37,23:29", await PackageGucScalarAsync(second, "SELECT hash_snapshot()"));
        Assert.AreEqual(DBNull.Value, await PackageGucScalarAsync(second, "SELECT hash_set(41, 47)"));
        Assert.AreEqual("11:13,31:37,23:29,41:47", await PackageGucScalarAsync(first, "SELECT hash_snapshot()"));

        foreach ((bool native, int value, string state) in new (bool, int, string)[] { (false, 97, "P7830"), (true, 101, "22012") })
        {
            PostgresException failure = await Assert.ThrowsExactlyAsync<PostgresException>(() => ExecutePackageGucAsync(first, $"SELECT collections_fail({native}, {value})"));
            Assert.AreEqual(state, failure.SqlState);
            Assert.AreEqual($"{value}:{-value}", await PackageGucScalarAsync(second, FixedCollectionRows("vec_drain")));
            Assert.AreEqual(42, await PackageGucScalarAsync(first, "SELECT 42"));
        }

        await ExecutePackageGucAsync(first, "BEGIN; SELECT vec_push(107, -109); ROLLBACK");
        Assert.AreEqual("107:-109", await PackageGucScalarAsync(second, FixedCollectionRows("vec_select")));
        int logLength = cluster.ReadServerLog().Length;
        using (Process backend = Process.GetProcessById(first.ProcessID))
        {
            backend.Kill();
            await backend.WaitForExitAsync(token);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!cluster.ReadServerLog()[logLength..].Contains("database system is ready to accept connections", StringComparison.Ordinal))
        {
            await Task.Delay(10, timeout.Token);
        }

        await using NpgsqlConnection recovered = await cluster.OpenConnectionAsync(timeout.Token);
        Assert.AreEqual("0|0|0", await PackageGucScalarAsync(recovered, "SELECT collection_counts()"));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(await PackageGucScalarAsync(recovered, "SELECT vec_push(113, -127)")));
        Assert.AreEqual("113:-127", await PackageGucScalarAsync(recovered, FixedCollectionRows("vec_drain")));
        Assert.AreEqual(DBNull.Value, await PackageGucScalarAsync(recovered, "SELECT hash_set(131, 137)"));
        Assert.AreEqual(137, await PackageGucScalarAsync(recovered, "SELECT hash_get(131)"));
        Assert.AreEqual(42, await PackageGucScalarAsync(recovered, "SELECT 42"));
    }

    /// <summary>
    /// Reads both custom-type fields in SQL while preserving set-returning function order.
    /// </summary>
    private static string FixedCollectionRows(string function) =>
        $"SELECT coalesce(string_agg((value::text::jsonb->>'Value1') || ':' || (value::text::jsonb->>'Value2'), ',' ORDER BY ordinal), '') FROM {function}() WITH ORDINALITY AS item(value, ordinal)";

    /// <summary>
    /// Ports pgrx's Vec, Deque and FnvIndexMap shared-memory example using ordinary C# inline storage.
    /// </summary>
    private const string FixedCollectionSource = """
        using Ankus;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;

        [PgType]
        public readonly record struct SharedItem(int Value1, int Value2);

        [InlineArray(400)]
        public struct Values
        {
            private SharedItem _element;
        }

        [InlineArray(4)]
        public struct Entries
        {
            private PgFixedMapEntry<int, int> _element;
        }

        [InlineArray(4)]
        public struct Indices
        {
            private int _element;
        }

        public struct CollectionState
        {
            private Values _list;
            private Values _deque;
            private Entries _entries;
            private Indices _indices;
            private int _listCount;
            private int _dequeCount;
            private int _head;
            private int _mapCount;

            [UnscopedRef]
            public PgFixedList<SharedItem> List() => new(_list, ref _listCount);

            [UnscopedRef]
            public PgFixedDeque<SharedItem> Deque() => new(_deque, ref _dequeCount, ref _head);

            [UnscopedRef]
            public PgFixedMap<int, int> Map() => new(_entries, _indices, ref _mapCount);
        }

        public static class CollectionFunctions
        {
            private static readonly PgLwLock<CollectionState> State = new("ankus_shared_probe.collections");
            private static int s_disposals;

            [PgModuleLoad]
            public static void Register() => PgSharedMemory.Initialize(State);

            [PgFunction]
            public static string CollectionCounts()
            {
                using PgLwLockShareGuard<CollectionState> guard = State.Share();
                CollectionState state = guard.Value;
                return $"{state.List().Count}|{state.Deque().Count}|{state.Map().Count}";
            }

            [PgFunction]
            public static bool VecPush(int value1, int value2)
            {
                using PgLwLockExclusiveGuard<CollectionState> guard = State.Exclusive();
                CollectionState state = guard.Value;
                bool added = state.List().TryAdd(new SharedItem(value1, value2));
                guard.Value = state;
                return added;
            }

            [PgFunction]
            public static void VecRequire(int value1, int value2)
            {
                using PgLwLockExclusiveGuard<CollectionState> guard = State.Exclusive();
                CollectionState state = guard.Value;
                state.List().Add(new SharedItem(value1, value2));
                guard.Value = state;
            }

            [PgFunction]
            public static void VecModifyCopy()
            {
                using PgLwLockShareGuard<CollectionState> guard = State.Share();
                CollectionState state = guard.Value;
                state.List().Clear();
            }

            [PgFunction]
            public static SharedItem? VecPop()
            {
                using PgLwLockExclusiveGuard<CollectionState> guard = State.Exclusive();
                CollectionState state = guard.Value;
                bool found = state.List().TryPop(out SharedItem value);
                guard.Value = state;
                return found ? value : null;
            }

            [PgFunction]
            public static IEnumerable<SharedItem> VecSelect() => Snapshot(false, false);

            [PgFunction]
            public static IEnumerable<SharedItem[]> VecArrays() => [Snapshot(false, false)];

            [PgFunction]
            public static int VecDisposals() => s_disposals;

            [PgFunction]
            public static IEnumerable<SharedItem> VecFailedRows()
            {
                try
                {
                    yield return new SharedItem(151, -157);
                    throw new PgException("P7831", "Collection iterator deliberately failed after a row.");
                }
                finally
                {
                    s_disposals++;
                }
            }

            [PgFunction(SetMode = PgSetMode.Materialize)]
            public static IEnumerable<SharedItem> VecDrain() => Snapshot(false, true);

            [PgFunction]
            public static IEnumerable<SharedItem> DequeSelect() => Snapshot(true, false);

            [PgFunction]
            public static IEnumerable<SharedItem> DequeDrain() => Snapshot(true, true);

            private static SharedItem[] Snapshot(bool deque, bool drain)
            {
                if (drain)
                {
                    using PgLwLockExclusiveGuard<CollectionState> guard = State.Exclusive();
                    CollectionState state = guard.Value;
                    SharedItem[] values = deque ? state.Deque().Drain() : state.List().Drain();
                    guard.Value = state;
                    return values;
                }

                using PgLwLockShareGuard<CollectionState> reader = State.Share();
                CollectionState copy = reader.Value;
                return deque ? copy.Deque().ToArray() : copy.List().ToArray();
            }

            [PgFunction]
            public static void VecFill() => Fill(false);

            [PgFunction]
            public static void DequeFill() => Fill(true);

            private static void Fill(bool deque)
            {
                using PgLwLockExclusiveGuard<CollectionState> guard = State.Exclusive();
                CollectionState state = guard.Value;
                for (int index = 0; index < 400; index++)
                {
                    if (deque)
                    {
                        state.Deque().PushBack(new SharedItem(index, -index));
                    }
                    else
                    {
                        state.List().Add(new SharedItem(index, -index));
                    }
                }

                guard.Value = state;
            }

            [PgFunction]
            public static bool DequePushFront(int value1, int value2) => DequePush(value1, value2, true);

            [PgFunction]
            public static bool DequePushBack(int value1, int value2) => DequePush(value1, value2, false);

            private static bool DequePush(int value1, int value2, bool front)
            {
                using PgLwLockExclusiveGuard<CollectionState> guard = State.Exclusive();
                CollectionState state = guard.Value;
                bool added = front ? state.Deque().TryPushFront(new SharedItem(value1, value2)) : state.Deque().TryPushBack(new SharedItem(value1, value2));
                guard.Value = state;
                return added;
            }

            [PgFunction]
            public static SharedItem? DequePopFront() => DequePop(true);

            [PgFunction]
            public static SharedItem? DequePopBack() => DequePop(false);

            private static SharedItem? DequePop(bool front)
            {
                using PgLwLockExclusiveGuard<CollectionState> guard = State.Exclusive();
                CollectionState state = guard.Value;
                SharedItem value;
                bool found = front ? state.Deque().TryPopFront(out value) : state.Deque().TryPopBack(out value);
                guard.Value = state;
                return found ? value : null;
            }

            [PgFunction]
            public static int? HashSet(int key, int value)
            {
                using PgLwLockExclusiveGuard<CollectionState> guard = State.Exclusive();
                CollectionState state = guard.Value;
                int? previous = state.Map().Set(key, value);
                guard.Value = state;
                return previous;
            }

            [PgFunction]
            public static int? HashGet(int key)
            {
                using PgLwLockShareGuard<CollectionState> guard = State.Share();
                CollectionState state = guard.Value;
                return state.Map().TryGetValue(key, out int value) ? value : null;
            }

            [PgFunction]
            public static int? HashRemove(int key)
            {
                using PgLwLockExclusiveGuard<CollectionState> guard = State.Exclusive();
                CollectionState state = guard.Value;
                bool found = state.Map().Remove(key, out int value);
                guard.Value = state;
                return found ? value : null;
            }

            [PgFunction]
            public static string HashSnapshot()
            {
                using PgLwLockShareGuard<CollectionState> guard = State.Share();
                CollectionState state = guard.Value;
                return string.Join(',', state.Map().ToArray().Select(pair => $"{pair.Key}:{pair.Value}"));
            }

            [PgFunction]
            public static void CollectionsFail(bool native, int value)
            {
                using PgLwLockExclusiveGuard<CollectionState> guard = State.Exclusive();
                CollectionState state = guard.Value;
                state.List().Add(new SharedItem(value, -value));
                guard.Value = state;
                if (native)
                {
                    Spi.Execute("SELECT 1/0");
                }

                throw new PgException("P7830", "Collection update deliberately failed after publication.");
            }
        }
        """;
}

namespace Ankus.Build.Tests;

public sealed partial class NativeBindingNativeTests
{
    private const string IndirectModelHeaders = """
        typedef unsigned long long (*Arithmetic)(unsigned long long, int);
        typedef Arithmetic ArithmeticAlias;
        typedef const Arithmetic ConstArithmetic;
        typedef int Adjusted(int values[3], Arithmetic callback);
        struct Missing;
        typedef struct Callbacks {
            Arithmetic first;
            ConstArithmetic second;
            int (*nested[2])(double);
            Adjusted *adjusted;
            void (*empty)(void);
            int (*variadic)(int, ...);
            int (*unprototyped)();
            struct Missing (*incomplete)(void);
        } Callbacks;
        extern Callbacks registry;
        extern ArithmeticAlias current;
        extern int ordinary(int value);
        """;

    /// <summary>
    /// Aliases, qualification and every graph location retain one canonical callable identity and exact adjusted storage.
    /// </summary>
    [TestMethod]
    public async Task IndirectCallsPreserveCompleteSignatureIdentity()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-indirect-model-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(IndirectModelHeaders,
                [new("registry", "registry", false), new("current", "current", false), new("ordinary", "ordinary", true)], directory);
            IReadOnlyList<NativeBindingIndirectCall> calls = NativeBindingIndirectModel.Describe(records.Graph);
            Assert.HasCount(7, calls);
            Assert.HasCount(4, calls.Where(static call => call.CanInvoke));
            NativeBindingIndirectCall arithmetic = calls.Single(static call => call.Name == "Arithmetic");
            NativeRecordType current = records.Graph.Types[records.Graph.Types[records.Graph.Roots["current"]].Canonical];
            Assert.AreEqual(records.Graph.Types[current.Element!.Value].Canonical, arithmetic.FunctionType);
            Assert.AreEqual(records.Graph.Target.PointerSize, records.Graph.Types[arithmetic.PointerType].Size);
            Assert.HasCount(2, arithmetic.Parameters);
            Assert.AreEqual(8L, records.Graph.Types[arithmetic.Result!.Value].Size);
            Assert.AreSequenceEqual(calls.Select(static call => call.FunctionType).Order(), calls.Select(static call => call.FunctionType));
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<NativeBindingIndirectCall>)calls).Clear());
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<int>)arithmetic.Parameters).Clear());
            NativeBindingIndirectCall adjusted = calls.Single(static call => call.Name == "Adjusted");
            Assert.HasCount(2, adjusted.Parameters);
            Assert.AreEqual("pointer", records.Graph.Types[records.Graph.Types[adjusted.Parameters[0]].Canonical].Kind);
            NativeRecordType callback = records.Graph.Types[records.Graph.Types[adjusted.Parameters[1]].Canonical];
            Assert.AreEqual(arithmetic.FunctionType, records.Graph.Types[callback.Element!.Value].Canonical);
            int[] chosen = [.. calls.Where(static call => call.CanInvoke).Select(static call => call.FunctionType)];
            IReadOnlyList<NativeBindingIndirectCall> selected = NativeBindingIndirectModel.Select(records, [.. chosen.Reverse()]);
            Assert.AreSequenceEqual(chosen, selected.Select(static call => call.FunctionType));
            Assert.AreSequenceEqual(arithmetic.Parameters, selected.Single(call => call.FunctionType == arithmetic.FunctionType).Parameters);
            Assert.HasCount(3, records.Graph.Roots);
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }

    /// <summary>
    /// Invalid, duplicated and unsupported signatures fail without bypassing unselected root validation or poisoning a retry.
    /// </summary>
    [TestMethod]
    public async Task IndirectCallsRejectInvalidSelectionsAndRecover()
    {
        string directory = Directory.CreateTempSubdirectory("ankus-indirect-rejection-").FullName;
        try
        {
            NativeHeaderRecords records = await CollectCallRecordsAsync(IndirectModelHeaders,
                [new("registry", "registry", false), new("current", "current", false), new("ordinary", "ordinary", true)], directory);
            IReadOnlyList<NativeBindingIndirectCall> calls = NativeBindingIndirectModel.Describe(records.Graph);
            int valid = calls.First(static call => call.CanInvoke).FunctionType;
            foreach (int[] selection in new int[][]
            {
                [-1], [records.Graph.Types.Count], [valid, valid], [records.Graph.Roots["current"]], [records.Graph.Roots["ordinary"]],
            })
            {
                Assert.ThrowsExactly<FormatException>(() => NativeBindingIndirectModel.Select(records, selection));
            }

            foreach (NativeBindingIndirectCall unsupported in calls.Where(static call => !call.CanInvoke))
            {
                Assert.ThrowsExactly<FormatException>(() => NativeBindingIndirectModel.Select(records, [unsupported.FunctionType]));
            }

            Dictionary<string, int> changed = records.Graph.Roots.ToDictionary();
            changed["ordinary"] = records.Graph.Types.Count;
            NativeHeaderRecords invalid = records with { Graph = records.Graph with { Roots = changed } };
            Assert.ThrowsExactly<FormatException>(() => NativeBindingIndirectModel.Select(invalid, [valid]));
            Assert.ThrowsExactly<FormatException>(() => NativeBindingIndirectModel.Select(invalid, []));
            Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingIndirectModel.Select(null!, []));
            Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingIndirectModel.Select(records, null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingIndirectModel.Describe(null!));
            Assert.IsEmpty(NativeBindingIndirectModel.Select(records, []));
            Assert.AreEqual(valid, NativeBindingIndirectModel.Select(records, [valid]).Single().FunctionType);
            int storage = calls.First(static call => call.CanInvoke).PointerType;
            string declaration = NativeBindingRecordChecks.DeclareValues(records, [(storage, "selected_value")]);
            foreach ((int Type, string Name)[] values in new (int, string)[][]
            {
                [(-1, "invalid")], [(records.Graph.Types.Count, "invalid")], [(storage, "not-a-name")],
                [(storage, "duplicate"), (storage, "duplicate")], [(valid, "function_value")],
            })
            {
                Assert.ThrowsExactly<FormatException>(() => NativeBindingRecordChecks.DeclareValues(records, values));
            }

            Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingRecordChecks.DeclareValues(null!, []));
            Assert.ThrowsExactly<ArgumentNullException>(() => NativeBindingRecordChecks.DeclareValues(records, null!));
            Assert.AreEqual("", NativeBindingRecordChecks.DeclareValues(records, []));
            Assert.AreEqual(declaration, NativeBindingRecordChecks.DeclareValues(records, [(storage, "selected_value")]));
        }
        finally
        {
            await DeleteDirectoryAsync(directory);
        }
    }
}

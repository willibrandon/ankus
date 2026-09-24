using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Ankus.Generators.Tests;

public sealed partial class PgFunctionGeneratorTests
{
    /// <summary>
    /// Backend-capable extensions emit one permanent native dispatcher with stable managed event values.
    /// </summary>
    [TestMethod]
    public void NativeBridgeMapsEveryTransactionAndSubtransactionEvent()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgFunction] public static int Value() => 1; }");
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        Assert.Contains("RegisterXactCallback(ankus_transaction_callback, NULL);", native);
        Assert.Contains("RegisterSubXactCallback(ankus_subtransaction_callback, NULL);", native);
        Assert.Contains("case XACT_EVENT_ABORT:\n            ankus_transaction_dispatch(0, 0, 0, 0, false, false);", native);
        Assert.Contains("case XACT_EVENT_COMMIT:\n            ankus_transaction_dispatch(0, 1, 0, 0, false, false);", native);
        Assert.Contains("case XACT_EVENT_PRE_COMMIT:\n            ankus_transaction_dispatch(0, 2, 0, 0, true, true);", native);
        Assert.Contains("case XACT_EVENT_PARALLEL_ABORT:\n            ankus_transaction_dispatch(0, 3, 0, 0, false, false);", native);
        Assert.Contains("case XACT_EVENT_PARALLEL_COMMIT:\n            ankus_transaction_dispatch(0, 4, 0, 0, false, false);", native);
        Assert.Contains("case XACT_EVENT_PARALLEL_PRE_COMMIT:\n            ankus_transaction_dispatch(0, 5, 0, 0, true, false);", native);
        Assert.Contains("case XACT_EVENT_PREPARE:\n            ankus_transaction_dispatch(0, 6, 0, 0, false, false);", native);
        Assert.Contains("case XACT_EVENT_PRE_PREPARE:\n            ankus_transaction_dispatch(0, 7, 0, 0, true, true);", native);
        Assert.Contains("case SUBXACT_EVENT_ABORT_SUB:\n            ankus_transaction_dispatch(1, 0, subtransaction_id, parent_subtransaction_id, false, false);", native);
        Assert.Contains("case SUBXACT_EVENT_COMMIT_SUB:\n            ankus_transaction_dispatch(1, 1, subtransaction_id, parent_subtransaction_id, false, false);", native);
        Assert.Contains("case SUBXACT_EVENT_PRE_COMMIT_SUB:\n            ankus_transaction_dispatch(1, 2, subtransaction_id, parent_subtransaction_id, true, true);", native);
        Assert.Contains("case SUBXACT_EVENT_START_SUB:\n            ankus_transaction_dispatch(1, 3, subtransaction_id, parent_subtransaction_id, true, true);", native);
    }

    /// <summary>
    /// Registration travels through the guarded request ABI and hides guard-owned subtransactions from consumers.
    /// </summary>
    [TestMethod]
    public void NativeRegistrationUsesGuardAndSuppressesImplementationSubtransactions()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgFunction] public static void Register() => " +
            "Ankus.PgTransaction.RegisterCallback(Ankus.PgTransactionEvent.Commit, () => { }); }");
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        Assert.Contains("ANKUS_SPI_GUC_READ,\n    ANKUS_SPI_TRANSACTION_CALLBACKS", native);
        Assert.Contains("SPIPlanPtr plan;\n    intptr_t callback;\n    int64 cursor_id;", native);
        Assert.Contains("bool transaction_callbacks = request->operation == ANKUS_SPI_TRANSACTION_CALLBACKS;", native);
        Assert.Contains("ankus_transaction_ensure((AnkusTransactionManaged) request->callback,", native);
        Assert.Contains("request->scalar_operation);", native);
        int suppression = native.IndexOf("ankus_internal_subtransaction_depth++;", StringComparison.Ordinal);
        int begin = native.IndexOf("BeginInternalSubTransaction(NULL);", suppression, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, suppression);
        Assert.IsGreaterThan(suppression, begin);
        Assert.Contains("if (ankus_internal_subtransaction_depth != 0)\n    {\n        return;\n    }", native);
        Assert.Contains("ankus_internal_subtransaction_depth = caller_internal_subtransaction_depth;", native);
    }

    /// <summary>
    /// Managed failures unwind callback scopes before native ERROR or FATAL reporting, with SQL limited to reversible phases.
    /// </summary>
    [TestMethod]
    public void TransactionDispatchRestoresManagedAndNativeScopesBeforeReporting()
    {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Generate(
            "public static class Functions { [Ankus.PgFunction] public static int Value() => 1; }");
        AssertMemoryCompilationSucceeds(compilation, diagnostics);
        string native = ManifestValue(compilation, "Ankus.NativeSource").ReplaceLineEndings("\n");
        Assert.Contains("&error, sql ? ankus_spi_execute : NULL, (intptr_t) ankus_transaction_log, &memory);", native);
        int callbackGuard = native.IndexOf("if (!transaction_direct_spi)", StringComparison.Ordinal);
        int guardedSubtransaction = native.IndexOf("ankus_internal_subtransaction_depth++;", callbackGuard,
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, callbackGuard);
        Assert.IsGreaterThan(callbackGuard, guardedSubtransaction);
        Assert.Contains("if (transaction_direct_spi && transaction_frame->failed)", native);
        Assert.Contains("ankus_capture_error(data, &transaction_frame->failure);", native);
        Assert.Contains("ankus_memory_protection = protection.previous;\n    if (snapshot_owned)\n    {\n        PopActiveSnapshot();", native);
        Assert.Contains("ankus_transaction_report(&error, reversible ? ERROR : FATAL);", native);
        Assert.Contains("PG_FINALLY();\n    {\n        ankus_release_error(error);\n    }", native);
        Assert.Contains("Transaction callbacks require an active PostgreSQL transaction", native);
    }
}

using System.Globalization;
using System.Runtime.InteropServices;
using Ankus.Postgres;

namespace Ankus.Examples.WalDecoder;

/// <summary>
/// Ports pgrx's <c>wal_decoder</c> example: a logical decoding output plugin that writes each committed transaction's
/// row changes as JSON.
/// </summary>
/// <remarks>
/// PostgreSQL replays committed changes from the write-ahead log and calls the plugin's callbacks: one BEGIN, one change
/// per inserted, updated or deleted row, and one COMMIT. Each callback appends one JSON document to the decoding
/// context's output. Use the plugin by library name, for example
/// <c>pg_create_logical_replication_slot('slot', 'Ankus.Examples.WalDecoder')</c>. Integer and text columns are
/// serialized; other types raise SQLSTATE <c>0A000</c>.
/// </remarks>
public static unsafe partial class WalDecoder
{
    /// <summary>
    /// Supplies the startup callback with the selected headers' exact signature.
    /// </summary>
    [PgNativeCallback(nameof(Startup))]
    private static partial OutputPluginCallbacks_startup_cbCallback StartupCallback { get; }

    /// <summary>
    /// Supplies the transaction-begin callback.
    /// </summary>
    [PgNativeCallback(nameof(BeginTransaction))]
    private static partial OutputPluginCallbacks_begin_cbCallback BeginCallback { get; }

    /// <summary>
    /// Supplies the row-change callback.
    /// </summary>
    [PgNativeCallback(nameof(Change))]
    private static partial OutputPluginCallbacks_change_cbCallback ChangeCallback { get; }

    /// <summary>
    /// Supplies the transaction-commit callback.
    /// </summary>
    [PgNativeCallback(nameof(CommitTransaction))]
    private static partial OutputPluginCallbacks_commit_cbCallback CommitCallback { get; }

    /// <summary>
    /// Supplies the shutdown callback.
    /// </summary>
    [PgNativeCallback(nameof(Shutdown))]
    private static partial OutputPluginCallbacks_shutdown_cbCallback ShutdownCallback { get; }

    /// <summary>
    /// Registers the plugin's callbacks when PostgreSQL loads the library as an output plugin.
    /// </summary>
    /// <param name="callbacks">PostgreSQL's callback table, valid only during this call.</param>
    [PgOutputPlugin]
    public static void Initialize(OutputPluginCallbacks* callbacks)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        callbacks->startup_cb = StartupCallback;
        callbacks->begin_cb = BeginCallback;
        callbacks->change_cb = ChangeCallback;
        callbacks->commit_cb = CommitCallback;
        callbacks->shutdown_cb = ShutdownCallback;
        PgLog.Debug1("wal_decoder: output plugin initialized");
    }

    /// <summary>
    /// Selects text output and allocates the per-context state in the decoding context's memory.
    /// </summary>
    private static void Startup(LogicalDecodingContext* context, OutputPluginOptions* options, bool isInit)
    {
        options->output_type = OutputPluginOutputType.OUTPUT_PLUGIN_TEXTUAL_OUTPUT;
        context->output_plugin_private = NativeMethods.MemoryContextAllocZero(context->context, (ulong)sizeof(DecodingState));
    }

    /// <summary>
    /// Resets the change count and writes a BEGIN document.
    /// </summary>
    private static void BeginTransaction(LogicalDecodingContext* context, ReorderBufferTXN* transaction)
    {
        State(context)->TransactionChangeCount = 0;
        Write(context, DecodedAction.Begin());
    }

    /// <summary>
    /// Counts and writes one inserted, updated or deleted row.
    /// </summary>
    private static void Change(LogicalDecodingContext* context, ReorderBufferTXN* transaction, RelationData* relation,
        ReorderBufferChange* change)
    {
        State(context)->TransactionChangeCount++;

        // Detoasted values and conversions live in a child context deleted after each change.
        PgMemoryContext.RunTransient("wal_decoder change", _ => Write(context, DecodeChange(relation, change)));
    }

    /// <summary>
    /// Writes a COMMIT document with the commit time and the transaction's change count.
    /// </summary>
    private static void CommitTransaction(LogicalDecodingContext* context, ReorderBufferTXN* transaction, ulong commitLsn)
    {
#if ANKUS_PG13 || ANKUS_PG14 || ANKUS_PG19
        // PostgreSQL 13 and 14 store the time directly; 19 makes the union anonymous, so Ankus promotes its members.
        long committed = transaction->commit_time;
#else
        long committed = transaction->xact_time.commit_time;
#endif
        Write(context, DecodedAction.Commit(committed, State(context)->TransactionChangeCount));
    }

    /// <summary>
    /// Releases the per-context state.
    /// </summary>
    private static void Shutdown(LogicalDecodingContext* context)
    {
        if (context->output_plugin_private != null)
        {
            NativeMethods.pfree(context->output_plugin_private);
            context->output_plugin_private = null;
        }
    }

    /// <summary>
    /// Builds the JSON action for one row change.
    /// </summary>
    /// <remarks>
    /// The old tuple of an UPDATE exists only for REPLICA IDENTITY FULL, a changed key, or a changed identity index
    /// column; otherwise PostgreSQL logs none and the action reports an empty <c>old</c> object, as pgrx does.
    /// </remarks>
    private static DecodedAction DecodeChange(RelationData* relation, ReorderBufferChange* change)
    {
        using PgRelation borrowed = PgRelation.DangerousBorrow(relation)
            ?? throw new InvalidOperationException("PostgreSQL passed no relation to the change callback.");
        string name = Spi.QuoteQualifiedIdentifier(borrowed.NamespaceName, borrowed.Name);
        PgTupleDescriptor descriptor = borrowed.TupleDescriptor;
        ReorderBufferChangeType action = change->action;
        bool hasOld = action is ReorderBufferChangeType.REORDER_BUFFER_CHANGE_UPDATE or ReorderBufferChangeType.REORDER_BUFFER_CHANGE_DELETE;
        bool hasNew = action is ReorderBufferChangeType.REORDER_BUFFER_CHANGE_UPDATE or ReorderBufferChangeType.REORDER_BUFFER_CHANGE_INSERT;
        string type = action switch
        {
            ReorderBufferChangeType.REORDER_BUFFER_CHANGE_DELETE => "DELETE",
            ReorderBufferChangeType.REORDER_BUFFER_CHANGE_INSERT => "INSERT",
            ReorderBufferChangeType.REORDER_BUFFER_CHANGE_UPDATE => "UPDATE",
            _ => "Unknown",
        };
        return new(type, Relation: name,
            Old: hasOld ? DecodeRow(name, relation, descriptor, Tuple(change->data.tp.oldtuple)) : null,
            New: hasNew ? DecodeRow(name, relation, descriptor, Tuple(change->data.tp.newtuple)) : null);
    }

    /// <summary>
    /// Serializes a row image's integer and text columns, skipping NULL and dropped columns.
    /// </summary>
    private static DecodedRow DecodeRow(string relationName, RelationData* relation, PgTupleDescriptor descriptor, HeapTupleData* tuple)
    {
        if (tuple == null)
        {
            return DecodedRow.Empty;
        }

        var columns = new List<(string Name, object Value)>(descriptor.Attributes.Count);
        for (int index = 0; index < descriptor.Attributes.Count; index++)
        {
            PgTupleAttributeInfo attribute = descriptor.Attributes[index];
            if (attribute.IsDropped)
            {
                continue;
            }

            bool isNull;
            ulong datum = NativeMethods.heap_getattr(tuple, index + 1, relation->rd_att, &isNull);
            if (isNull)
            {
                continue;
            }

            string column = Spi.QuoteIdentifier(attribute.Name);
            object value = attribute.TypeOid switch
            {
                (uint)PgBuiltInOid.Int4Oid => unchecked((int)datum),
                (uint)PgBuiltInOid.TextOid => PgDatum.DangerousCreate((nuint)datum, attribute.TypeOid, PgMemoryContext.Current).Read<string>(),
                _ => throw new PgException(PgSqlStates.FeatureNotSupported, string.Create(CultureInfo.InvariantCulture,
                    $"wal_decoder serializes only integer and text columns; {relationName}.{column} has type OID {attribute.TypeOid}")),
            };
            columns.Add((column, value));
        }

        return new(columns);
    }

    /// <summary>
    /// Appends one JSON document to the decoding context's output in the database encoding.
    /// </summary>
    private static void Write(LogicalDecodingContext* context, DecodedAction action)
    {
        byte[] json = action.ToUtf8Json();
        NativeMethods.OutputPluginPrepareWrite(context, true);
        fixed (byte* utf8 = json)
        {
            // pg_any_to_server returns its validated input when no conversion is needed, otherwise a terminated copy.
            sbyte* text = NativeMethods.pg_any_to_server((sbyte*)utf8, json.Length, (int)pg_enc.PG_UTF8);
            int length = text == (sbyte*)utf8 ? json.Length : MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)text).Length;
            NativeMethods.appendBinaryStringInfo(context->@out, text, length);
            if (text != (sbyte*)utf8)
            {
                NativeMethods.pfree(text);
            }
        }

        NativeMethods.OutputPluginWrite(context, true);
    }

    /// <summary>
    /// Reads the per-context state allocated by the startup callback.
    /// </summary>
    private static DecodingState* State(LogicalDecodingContext* context)
        => context->output_plugin_private != null ? (DecodingState*)context->output_plugin_private
            : throw new InvalidOperationException("The wal_decoder startup callback did not run for this decoding context.");

#if ANKUS_PG13 || ANKUS_PG14 || ANKUS_PG15 || ANKUS_PG16
    /// <summary>
    /// Selects the heap tuple embedded in PostgreSQL 13–16's reorder-buffer tuple wrapper.
    /// </summary>
    private static HeapTupleData* Tuple(ReorderBufferTupleBuf* buffer) => buffer == null ? null : &buffer->tuple;
#else
    /// <summary>
    /// Returns PostgreSQL 17 and later's reorder-buffer heap tuple.
    /// </summary>
    private static HeapTupleData* Tuple(HeapTupleData* tuple) => tuple;
#endif
}

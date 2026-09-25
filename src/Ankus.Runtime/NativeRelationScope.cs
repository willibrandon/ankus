using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace Ankus;

/// <summary>
/// Owns explicitly converted relation arguments and results for generated callback cleanup.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NativeRelationScope : IDisposable
{
    private List<PgRelation>? _relations = [];
    private NativeRelationScope? _retainedArguments;

    /// <summary>
    /// Creates a result owner that preserves yielded references also retained as iterator arguments.
    /// </summary>
    /// <param name="iterator">The generated iterator handle.</param>
    /// <returns>A result scope to create before advancing the iterator.</returns>
    public static NativeRelationScope ForIterator(nint iterator)
        => new() { _retainedArguments = NativeSet.RelationArguments(iterator) };

    /// <summary>
    /// Captures a relation, vector, or shaped array after conversion, including cleanup when recording ownership fails.
    /// </summary>
    /// <typeparam name="T">The generated scalar or collection type.</typeparam>
    /// <param name="value">The converted value, including null.</param>
    /// <returns>The same value.</returns>
    public T Add<T>(T value)
    {
        try
        {
            ObjectDisposedException.ThrowIf(_relations is null, this);
            if (value is PgRelation relation)
            {
                Capture(relation);
            }
            else if (value is PgRelation?[] vector)
            {
                foreach (PgRelation? item in vector)
                {
                    if (item is not null) { Capture(item); }
                }
            }
            else if (value is PgArray<PgRelation?> array)
            {
                foreach (PgRelation? item in array)
                {
                    if (item is not null) { Capture(item); }
                }
            }

            return value;
        }
        catch (Exception primary)
        {
            Release(value, primary, _retainedArguments);
            throw;
        }
    }

    /// <summary>
    /// Releases every returned column if capturing a complete generated table row fails.
    /// </summary>
    /// <typeparam name="T">The generated relation column type.</typeparam>
    /// <param name="value">The column, including any references not yet recorded in this scope.</param>
    /// <param name="primary">The capture failure to preserve.</param>
    public void ReleaseFailed<T>(T value, Exception primary) => Release(value, primary, _retainedArguments);

    /// <summary>
    /// Leaves successfully converted values owned by the caller without allocating another owner.
    /// </summary>
    internal void Relinquish() => _relations = null;

    /// <summary>
    /// Releases a partially converted scalar result while preserving its conversion or native-result cleanup failure.
    /// </summary>
    internal void ReleaseAfterFailure(Exception primary)
    {
        List<PgRelation>? relations = _relations;
        _relations = null;
        if (relations is not null)
        {
            for (int index = relations.Count - 1; index >= 0; index--)
            {
                Close(relations[index], primary, _retainedArguments);
            }
        }
    }

    /// <summary>
    /// Transfers all close obligations to a new scope, leaving this scope empty.
    /// </summary>
    /// <returns>The scope owned by an iterator after successful creation.</returns>
    public NativeRelationScope Detach()
    {
        ObjectDisposedException.ThrowIf(_relations is null, this);
        var result = new NativeRelationScope { _relations = _relations };
        _relations = null;
        return result;
    }

    /// <summary>
    /// Releases every captured reference, attempting remaining closes even if one fails.
    /// </summary>
    public void Dispose()
    {
        List<PgRelation>? relations = _relations;
        _relations = null;
        Exception? failure = null;
        if (relations is not null)
        {
            for (int index = relations.Count - 1; index >= 0; index--)
            {
                try { relations[index].Dispose(); }
                catch (Exception exception) { failure ??= exception; }
            }
        }

        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    /// <summary>
    /// Releases only relation values acquired by a failed conversion while preserving its original exception.
    /// </summary>
    internal static void Release<T>(T value, Exception primary, NativeRelationScope? retained = null)
    {
        if (value is PgRelation relation)
        {
            Close(relation, primary, retained);
        }
        else if (value is PgRelation?[] vector)
        {
            foreach (PgRelation? item in vector) { Close(item, primary, retained); }
        }
        else if (value is PgArray<PgRelation?> array)
        {
            foreach (PgRelation? item in array) { Close(item, primary, retained); }
        }
    }

    private void Capture(PgRelation relation)
    {
        if (_retainedArguments?._relations?.Contains(relation) != true && !_relations!.Contains(relation))
        {
            _relations.Add(relation);
        }
    }

    private static void Close(PgRelation? relation, Exception primary, NativeRelationScope? retained)
    {
        if (relation is not null && retained?._relations?.Contains(relation) == true) { return; }

        try { relation?.Dispose(); }
        catch (Exception cleanup) { primary.Data["Ankus.RelationCleanup"] = cleanup; }
    }
}

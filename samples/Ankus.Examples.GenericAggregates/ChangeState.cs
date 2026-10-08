using Ankus.Postgres;

namespace Ankus.Examples.GenericAggregates;

/// <summary>
/// Retains one group's change count and a private copy of its most recent non-null value.
/// </summary>
/// <remarks>
/// The element type is known only at run time. Its storage contract comes from PostgreSQL's
/// <c>get_typlenbyval</c>, and values are compared byte for byte with <c>datumIsEqual</c> from
/// <c>utils/datum.h</c>. Retained values live in a child of the aggregate's memory context, so they
/// survive the per-row context and are released when PostgreSQL resets or deletes the group's state.
/// </remarks>
public sealed class ChangeState
{
    private readonly PgMemoryContext _storage;
    private readonly short _typeLength;
    private readonly bool _byValue;
    private PgAnyElement? _previous;

    /// <summary>
    /// Resolves the element type's storage contract and creates the group's private value storage.
    /// </summary>
    /// <param name="aggregateContext">The aggregate's long-lived state context.</param>
    /// <param name="typeOid">The element type resolved for this aggregate call.</param>
    public ChangeState(PgMemoryContext aggregateContext, uint typeOid)
    {
        short typeLength;
        bool byValue;
        unsafe
        {
            NativeMethods.get_typlenbyval(typeOid, &typeLength, &byValue);
        }

        _typeLength = typeLength;
        _byValue = byValue;
        _storage = PgMemoryContext.Create("count_changes previous value", aggregateContext, PgMemoryContextOptions.Small);
    }

    /// <summary>
    /// Gets the number of times a non-null value differed from the previous non-null value.
    /// </summary>
    public long Changes
    {
        get;
        private set;
    }

    /// <summary>
    /// Counts a change when the value differs from the retained value, then retains the new value.
    /// </summary>
    /// <param name="value">The current row's non-null value, owned by the transition call.</param>
    public void Observe(PgAnyElement value)
    {
        // Copying detoasts and flattens the value, so equal values compare equal regardless of their incoming
        // storage form. This copy belongs to the transition call and is released after it returns.
        PgAnyElement current = value.CopyTo(PgMemoryContext.Current);
        if (_previous is not null)
        {
            if (IsEqual(_previous, current))
            {
                return;
            }

            Changes = checked(Changes + 1);
        }

        // Release the previous copy before retaining the new one, as pgrx does with pfree and datumCopy.
        _previous = null;
        _storage.Reset();
        _previous = current.CopyTo(_storage);
    }

    /// <summary>
    /// Compares two values of the resolved element type byte for byte.
    /// </summary>
    private bool IsEqual(PgAnyElement left, PgAnyElement right)
    {
        unsafe
        {
            return NativeMethods.datumIsEqual(left.Datum.DangerousGetBits(), right.Datum.DangerousGetBits(), _byValue, _typeLength);
        }
    }
}

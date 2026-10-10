using System.Globalization;

namespace Ankus.Build;

/// <summary>
/// Follows bindgen 0.72's <c>StructLayoutTracker</c> over one record's fields to place the padding fields bindgen
/// declares where Clang's layout leaves a gap that Rust's own alignment would not reproduce.
/// </summary>
/// <param name="record">The record's size and alignment, as Clang lays it out.</param>
/// <param name="packed">Whether bindgen treats the record as packed.</param>
/// <param name="union">Whether the record is a union.</param>
/// <param name="pointerSize">The target pointer size, which bounds bindgen's padding blob alignment.</param>
internal sealed class LayoutTracker((long Size, long Alignment) record, bool packed, bool union, long pointerSize)
{
    /// <summary>
    /// The largest alignment bindgen assumes Rust guarantees without an explicit representation.
    /// </summary>
    internal const long MaxGuaranteedAlignment = 8;

    private long _latestOffset;
    private (long Size, long Alignment)? _latestField;
    private bool _lastFieldWasBitfield;
    private int _paddingCount;

    /// <summary>
    /// Records a bitfield allocation unit.
    /// </summary>
    /// <param name="layout">The unit's size and alignment.</param>
    internal void SawBitfieldUnit((long Size, long Alignment) layout)
    {
        _ = AlignToLatestField(layout);
        _latestOffset += layout.Size;
        _latestField = layout;
        _lastFieldWasBitfield = true;
    }

    /// <summary>
    /// Records an ordinary field.
    /// </summary>
    /// <param name="layout">The field's size and alignment.</param>
    /// <param name="offset">The field's offset in bits, as Clang lays it out, or null when libclang reports none.</param>
    /// <returns>The layout of a padding field to declare before it, if bindgen declares one.</returns>
    internal (long Size, long Alignment)? SawField((long Size, long Alignment) layout, long? offset)
    {
        bool merges = AlignToLatestField(layout);
        long padding = offset is long bits && bits / 8 > _latestOffset ? bits / 8 - _latestOffset :
            merges || layout.Alignment == 0 || union ? 0 :
            PaddingBytes(packed ? Math.Min(layout.Alignment, record.Alignment) : layout.Alignment);
        _latestOffset += padding;
        (long Size, long Alignment)? field = null;
        if (!packed && !union && padding != 0 && (padding >= layout.Alignment || layout.Alignment > MaxGuaranteedAlignment))
        {
            field = (padding, Math.Min(layout.Alignment, MaxGuaranteedAlignment));
        }

        _latestOffset = union ? Math.Max(_latestOffset, layout.Size) : _latestOffset + layout.Size;
        _latestField = layout;
        _lastFieldWasBitfield = false;
        return field;
    }

    /// <summary>
    /// Gets the padding bindgen declares after the last field to reach the record's size.
    /// </summary>
    /// <returns>The padding field's layout, if bindgen declares one.</returns>
    internal (long Size, long Alignment)? PadStruct()
    {
        (long size, long alignment) = record;
        if (size <= _latestOffset)
        {
            return null;
        }

        long padding = size - _latestOffset;
        if (padding < alignment && !(_lastFieldWasBitfield && padding >= _latestField!.Value.Alignment))
        {
            return null;
        }

        return packed ? (padding, 1) :
            _lastFieldWasBitfield || alignment > MaxGuaranteedAlignment ? ForSize(padding) : (padding, alignment);
    }

    /// <summary>
    /// Declares the next padding field, a blob of the widest unsigned integer its alignment allows.
    /// </summary>
    /// <param name="layout">The padding's size and alignment.</param>
    /// <returns>The field.</returns>
    internal NativeBindingField PaddingField((long Size, long Alignment) layout)
    {
        string name = "__bindgen_padding_" + _paddingCount++.ToString(CultureInfo.InvariantCulture);
        string? element = layout.Alignment switch
        {
            16 => "u128",
            8 => "u64",
            4 => "u32",
            2 => "u16",
            1 => "u8",
            _ => null,
        };
        long length = element is null ? layout.Size : layout.Size / Math.Max(layout.Alignment, 1);
        element ??= "u8";
        return new NativeBindingField(name, name,
            length == 1 ? element : "[" + element + "; " + length.ToString(CultureInfo.InvariantCulture) + "usize]");
    }

    /// <summary>
    /// Aligns to the previous field the obvious way, unless the new field fits in a bitfield unit's remaining bytes.
    /// </summary>
    /// <returns>Whether the new field merges into the bitfield unit.</returns>
    private bool AlignToLatestField((long Size, long Alignment) layout)
    {
        if (packed || _latestField is not (long size, long alignment))
        {
            return false;
        }

        long unit = Math.Max(1, alignment);
        if (_lastFieldWasBitfield && layout.Alignment <= size % unit && layout.Size <= size % unit)
        {
            return true;
        }

        _latestOffset += PaddingBytes(alignment);
        return false;
    }

    private long PaddingBytes(long alignment)
        => alignment == 0 || _latestOffset % alignment == 0 ? 0 : alignment - _latestOffset % alignment;

    /// <summary>
    /// Gets the widest alignment up to the pointer size that divides a size, as bindgen's <c>Layout::for_size</c> does.
    /// </summary>
    private (long Size, long Alignment) ForSize(long size)
    {
        long next = 2;
        while (size % next == 0 && next <= pointerSize)
        {
            next *= 2;
        }

        return (size, next / 2);
    }
}

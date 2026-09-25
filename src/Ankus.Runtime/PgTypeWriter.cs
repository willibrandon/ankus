using System.Buffers;
using System.ComponentModel;
using System.Formats.Cbor;
using System.Text.Json;

namespace Ankus;

/// <summary>
/// Writes bounded JSON or CBOR tokens from statically generated type contracts.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PgTypeWriter : IDisposable
{
    private readonly IBufferWriter<byte> _destination;
    private readonly Utf8JsonWriter? _json;
    private readonly CborWriter? _cbor;
    private int _depth;

    /// <summary>
    /// Selects JSON text or compact, definite-length CBOR storage.
    /// </summary>
    internal PgTypeWriter(IBufferWriter<byte> destination, bool json)
    {
        _destination = destination;
        if (json)
        {
            _json = new Utf8JsonWriter(destination, new JsonWriterOptions { MaxDepth = 64 });
        }
        else
        {
            _cbor = new CborWriter(CborConformanceMode.Strict);
        }
    }

    /// <summary>
    /// Writes a null member or element.
    /// </summary>
    public void WriteNull()
    {
        if (_cbor is not null)
        {
            _cbor.WriteNull();
        }
        else
        {
            _json!.WriteNullValue();
        }
    }

    /// <summary>
    /// Writes a Boolean.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteBoolean(bool value)
    {
        if (_cbor is not null)
        {
            _cbor.WriteBoolean(value);
        }
        else
        {
            _json!.WriteBooleanValue(value);
        }
    }

    /// <summary>
    /// Writes an exact signed integer.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteInt64(long value)
    {
        if (_cbor is not null)
        {
            _cbor.WriteInt64(value);
        }
        else
        {
            _json!.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// Writes an exact unsigned integer.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteUInt64(ulong value)
    {
        if (_cbor is not null)
        {
            _cbor.WriteUInt64(value);
        }
        else
        {
            _json!.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// Writes binary32 storage; JSON rejects non-finite numbers.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteSingle(float value)
    {
        if (_cbor is not null)
        {
            _cbor.WriteSingle(value);
        }
        else
        {
            _json!.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// Writes binary64 storage; JSON rejects non-finite numbers.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteDouble(double value)
    {
        if (_cbor is not null)
        {
            _cbor.WriteDouble(value);
        }
        else
        {
            _json!.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// Writes exact decimal-fraction storage or a JSON decimal number; rejects unrepresentable negative zero.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteDecimal(decimal value)
    {
        if (value == 0 && decimal.IsNegative(value))
        {
            throw new ArgumentException("Negative decimal zero cannot be represented losslessly in custom-type storage.", nameof(value));
        }

        if (_cbor is not null)
        {
            _cbor.WriteDecimal(value);
        }
        else
        {
            _json!.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// Writes a non-null Unicode string without replacement of malformed UTF-16.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = PgSerializationText.Utf8.GetByteCount(value);
        if (_cbor is not null)
        {
            _cbor.WriteTextString(value);
        }
        else
        {
            _json!.WriteStringValue(value);
        }
    }

    /// <summary>
    /// Starts an object with its statically known member count.
    /// </summary>
    /// <param name="count">The number of key/value pairs.</param>
    public void WriteStartObject(int count)
    {
        Enter();
        if (_cbor is not null)
        {
            _cbor.WriteStartMap(count);
        }
        else
        {
            _json!.WriteStartObject();
        }
    }

    /// <summary>
    /// Writes a member or dictionary key.
    /// </summary>
    /// <param name="name">The exact case-sensitive key.</param>
    public void WritePropertyName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _ = PgSerializationText.Utf8.GetByteCount(name);
        if (_cbor is not null)
        {
            _cbor.WriteTextString(name);
        }
        else
        {
            _json!.WritePropertyName(name);
        }
    }

    /// <summary>
    /// Ends the current object.
    /// </summary>
    public void WriteEndObject()
    {
        if (_cbor is not null)
        {
            _cbor.WriteEndMap();
        }
        else
        {
            _json!.WriteEndObject();
        }

        _depth--;
    }

    /// <summary>
    /// Starts a sequence with its known element count.
    /// </summary>
    /// <param name="count">The number of elements.</param>
    public void WriteStartArray(int count)
    {
        Enter();
        if (_cbor is not null)
        {
            _cbor.WriteStartArray(count);
        }
        else
        {
            _json!.WriteStartArray();
        }
    }

    /// <summary>
    /// Ends the current sequence.
    /// </summary>
    public void WriteEndArray()
    {
        if (_cbor is not null)
        {
            _cbor.WriteEndArray();
        }
        else
        {
            _json!.WriteEndArray();
        }

        _depth--;
    }

    /// <summary>
    /// Releases any JSON output buffers.
    /// </summary>
    public void Dispose() => _json?.Dispose();

    /// <summary>
    /// Flushes exactly one complete value to the caller's buffer.
    /// </summary>
    internal void Complete()
    {
        if (_depth != 0 || (_json is not null && _json.BytesCommitted + _json.BytesPending == 0))
        {
            throw new InvalidOperationException("Incomplete custom-type output.");
        }

        if (_cbor is not null)
        {
            Span<byte> destination = _destination.GetSpan(_cbor.BytesWritten);
            int written = _cbor.Encode(destination);
            _destination.Advance(written);
        }
        else
        {
            _json!.Flush();
        }
    }

    /// <summary>
    /// Bounds recursive contracts and cyclic object graphs before descending further.
    /// </summary>
    private void Enter()
    {
        if (++_depth > 64)
        {
            throw new InvalidOperationException("Custom-type nesting exceeds 64 levels.");
        }
    }
}

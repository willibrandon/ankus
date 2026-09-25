using System.Buffers;
using System.Globalization;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies custom SQL text codecs remain independent from generated CBOR storage and default JSON behavior.
/// </summary>
[TestClass]
public sealed class PgTypeTextCodecTests
{
    /// <summary>
    /// Keeps custom text, structural bytes and their separate callback counts observable through one retained codec.
    /// </summary>
    [TestMethod]
    public void CustomTextAndGeneratedBinaryUseIndependentFormats()
    {
        int constructions = 0;
        var textCodec = new DelegateTextCodec(
            static text => new Reading(long.Parse(text.AsSpan("reading:".Length), CultureInfo.InvariantCulture)),
            static value => "reading:" + value.Value.ToString(CultureInfo.InvariantCulture));
        var codec = new GeneratedReadingCodec(() =>
        {
            constructions++;
            return textCodec;
        });

        Assert.AreEqual(new Reading(42), codec.Read(Convert.FromHexString("A16556616C7565182A")));
        Assert.AreSequenceEqual(Convert.FromHexString("A16556616C756526"), Encode(codec, new Reading(-7)));
        Assert.AreEqual(0, constructions);
        Assert.AreEqual(0, textCodec.ParseCalls);
        Assert.AreEqual(0, textCodec.FormatCalls);

        Reading parsed = codec.Parse("reading:42");
        Assert.AreEqual(new Reading(42), parsed);
        Assert.AreEqual("reading:42", codec.Format(parsed));
        Assert.AreEqual("reading:-7", codec.Format(new Reading(-7)));
        Assert.AreEqual(1, constructions);
        Assert.AreEqual(1, textCodec.ParseCalls);
        Assert.AreEqual(2, textCodec.FormatCalls);

        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => codec.Read([0xFF])).SqlState);
        Assert.AreEqual(new Reading(-7), codec.Read(Convert.FromHexString("A16556616C756526")));
        Assert.AreSequenceEqual(Convert.FromHexString("A16556616C7565182A"), Encode(codec, new Reading(42)));
        Assert.AreEqual(1, constructions);
        Assert.AreEqual(1, textCodec.ParseCalls);
        Assert.AreEqual(2, textCodec.FormatCalls);
    }

    /// <summary>
    /// Initializes the text codec once when formatting happens first and retains that instance for later parsing.
    /// </summary>
    [TestMethod]
    public void CustomTextFactoryIsSharedWhenFormattingFirst()
    {
        int constructions = 0;
        var textCodec = new DelegateTextCodec(
            static text => new Reading(long.Parse(text, CultureInfo.InvariantCulture)),
            static value => "value=" + value.Value.ToString(CultureInfo.InvariantCulture));
        var codec = new GeneratedReadingCodec(() =>
        {
            constructions++;
            return textCodec;
        });

        Assert.AreEqual("value=7", codec.Format(new Reading(7)));
        Assert.AreEqual("value=42", codec.Format(new Reading(42)));
        Assert.AreEqual(new Reading(-3), codec.Parse("-3"));
        Assert.AreEqual(1, constructions);
        Assert.AreEqual(1, textCodec.ParseCalls);
        Assert.AreEqual(2, textCodec.FormatCalls);
    }

    /// <summary>
    /// Defers and caches a failed factory while allowing independent binary operations before and after its failure.
    /// </summary>
    [TestMethod]
    public void FailingTextFactoryIsDeferredCachedAndBypassedByBinary()
    {
        int constructions = 0;
        var failure = new FormatException("Text codec construction failed.");
        var codec = new GeneratedReadingCodec(() =>
        {
            constructions++;
            throw failure;
        });

        Assert.AreEqual(0, constructions);
        Assert.AreEqual(new Reading(42), codec.Read(Convert.FromHexString("A16556616C7565182A")));
        Assert.AreSequenceEqual(Convert.FromHexString("A16556616C7565182A"), Encode(codec, new Reading(42)));
        Assert.AreEqual(0, constructions);
        Assert.AreSame(failure, Assert.ThrowsExactly<FormatException>(() => codec.Parse("text")));
        Assert.AreSame(failure, Assert.ThrowsExactly<FormatException>(() => codec.Format(new Reading(42))));
        Assert.AreEqual(1, constructions);
        Assert.AreEqual(new Reading(-7), codec.Read(Convert.FromHexString("A16556616C756526")));
        Assert.AreSequenceEqual(Convert.FromHexString("A16556616C756526"), Encode(codec, new Reading(-7)));
        Assert.AreEqual(1, constructions);
    }

    /// <summary>
    /// Rejects a null factory result consistently instead of silently falling back to JSON or repeatedly constructing it.
    /// </summary>
    [TestMethod]
    public void NullTextFactoryResultIsRejectedAndCached()
    {
        int constructions = 0;
        var codec = new GeneratedReadingCodec(() =>
        {
            constructions++;
            return null!;
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => codec.Parse("text"));
        Assert.ThrowsExactly<InvalidOperationException>(() => codec.Format(new Reading(42)));
        Assert.AreEqual(1, constructions);
        Assert.AreEqual(new Reading(42), codec.Read(Convert.FromHexString("A16556616C7565182A")));
        Assert.AreSequenceEqual(Convert.FromHexString("A16556616C7565182A"), Encode(codec, new Reading(42)));
        Assert.AreEqual(1, constructions);
    }

    /// <summary>
    /// Preserves a user's PostgreSQL exception object and all of its chosen diagnostics through either text callback.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CustomPgExceptionsRetainTheirIdentityAndDiagnostics(bool format)
    {
        var cause = new ArgumentException("Original cause.");
        var failure = new PgException("22003", "Custom range failure.", "Value exceeds the domain.", "Choose a smaller value.", cause)
        {
            Context = "custom text conversion",
            DataTypeName = "reading",
        };
        var textCodec = new DelegateTextCodec(_ => throw failure, _ => throw failure);
        var codec = new GeneratedReadingCodec(() => textCodec);
        Action invoke = format ? () => codec.Format(new Reading(42)) : () => codec.Parse("reading:42");

        PgException actual = Assert.ThrowsExactly<PgException>(invoke);

        Assert.AreSame(failure, actual);
        Assert.AreEqual("22003", actual.SqlState);
        Assert.AreEqual("Custom range failure.", actual.Message);
        Assert.AreEqual("Value exceeds the domain.", actual.Detail);
        Assert.AreEqual("Choose a smaller value.", actual.Hint);
        Assert.AreEqual("custom text conversion", actual.Context);
        Assert.AreEqual("reading", actual.DataTypeName);
        Assert.AreSame(cause, actual.InnerException);
        Assert.AreEqual(format ? 0 : 1, textCodec.ParseCalls);
        Assert.AreEqual(format ? 1 : 0, textCodec.FormatCalls);
    }

    /// <summary>
    /// Lets ordinary user exceptions reach the normal managed error boundary without claiming they are malformed JSON.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OrdinaryTextExceptionsAreNotWrappedAsJsonErrors(bool format)
    {
        var failure = new FormatException("Invalid domain-specific text.");
        var textCodec = new DelegateTextCodec(_ => throw failure, _ => throw failure);
        var codec = new GeneratedReadingCodec(() => textCodec);
        Action invoke = format ? () => codec.Format(new Reading(42)) : () => codec.Parse("reading:42");

        Assert.AreSame(failure, Assert.ThrowsExactly<FormatException>(invoke));
        Assert.AreEqual(format ? 0 : 1, textCodec.ParseCalls);
        Assert.AreEqual(format ? 1 : 0, textCodec.FormatCalls);
    }

    /// <summary>
    /// Refuses a null result for present SQL text or a null text representation for a present managed value.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NullCustomTextResultsAreRejected(bool format)
    {
        var textCodec = new DelegateTextCodec(static _ => null!, static _ => null!);
        var codec = new GeneratedReadingCodec(() => textCodec);
        Action invoke = format ? () => codec.Format(new Reading(42)) : () => codec.Parse("reading:42");

        Assert.ThrowsExactly<InvalidOperationException>(invoke);
        Assert.AreEqual(format ? 0 : 1, textCodec.ParseCalls);
        Assert.AreEqual(format ? 1 : 0, textCodec.FormatCalls);
    }

    /// <summary>
    /// Rejects absent direct arguments before invoking the text codec and keeps null text from constructing it.
    /// </summary>
    [TestMethod]
    public void NullArgumentsDoNotReachCustomTextMethods()
    {
        int constructions = 0;
        var textCodec = new DelegateTextCodec(static _ => new Reading(42), static _ => "present");
        var codec = new GeneratedReadingCodec(() =>
        {
            constructions++;
            return textCodec;
        });

        Assert.AreEqual("text", Assert.ThrowsExactly<ArgumentNullException>(() => codec.Parse(null!)).ParamName);
        Assert.AreEqual(0, constructions);
        Assert.ThrowsExactly<InvalidOperationException>(() => codec.Format(null!));
        Assert.AreEqual(0, constructions);
        Assert.AreEqual(0, textCodec.ParseCalls);
        Assert.AreEqual(0, textCodec.FormatCalls);
    }

    /// <summary>
    /// Retains the existing JSON and CBOR contracts when no text factory was supplied.
    /// </summary>
    [TestMethod]
    public void DefaultSerializationKeepsJsonAndCborContracts()
    {
        var codec = new GeneratedReadingCodec();
        PgTypeTextCodec<Reading> textContract = codec;

        Assert.AreEqual(new Reading(42), textContract.Parse("{\"Value\":42}"));
        Assert.AreEqual("{\"Value\":42}", textContract.Format(new Reading(42)));
        Assert.AreEqual(new Reading(42), codec.Read(Convert.FromHexString("A16556616C7565182A")));
        Assert.AreSequenceEqual(Convert.FromHexString("A16556616C7565182A"), Encode(codec, new Reading(42)));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => textContract.Parse("reading:42")).SqlState);
    }

    /// <summary>
    /// Materializes the generated storage payload independently of its text representation.
    /// </summary>
    private static byte[] Encode(PgTypeCodec<Reading> codec, Reading value)
    {
        var destination = new ArrayBufferWriter<byte>();
        codec.Write(value, destination);
        return destination.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Carries a present reference contract whose null output can be tested separately from its value.
    /// </summary>
    private sealed record Reading(long Value);

    /// <summary>
    /// Supplies observable custom text callbacks without participating in generated binary storage.
    /// </summary>
    private sealed class DelegateTextCodec(Func<string, Reading> parse, Func<Reading, string> format) : PgTypeTextCodec<Reading>
    {
        /// <summary>
        /// Gets how many input conversions reached this specific instance.
        /// </summary>
        public int ParseCalls { get; private set; }

        /// <summary>
        /// Gets how many output conversions reached this specific instance.
        /// </summary>
        public int FormatCalls { get; private set; }

        /// <inheritdoc />
        public override Reading Parse(string text)
        {
            ParseCalls++;
            return parse(text);
        }

        /// <inheritdoc />
        public override string Format(Reading value)
        {
            FormatCalls++;
            return format(value);
        }
    }

    /// <summary>
    /// Emulates generated direct member access while inheriting the shared custom text and format boundaries.
    /// </summary>
    private sealed class GeneratedReadingCodec(Func<PgTypeTextCodec<Reading>>? createTextCodec = null) : PgSerializedTypeCodec<Reading>(createTextCodec)
    {
        /// <inheritdoc />
        protected override Reading ReadValue(ref PgTypeReader reader)
        {
            reader.ReadStartObject();
            if (reader.ReadPropertyName() != "Value")
            {
                throw new FormatException("Expected the Value member.");
            }

            long value = reader.ReadInt64();
            if (reader.ReadPropertyName() is not null)
            {
                throw new FormatException("Unexpected member.");
            }

            return new(value);
        }

        /// <inheritdoc />
        protected override void WriteValue(PgTypeWriter writer, Reading value)
        {
            writer.WriteStartObject(1);
            writer.WritePropertyName("Value");
            writer.WriteInt64(value.Value);
            writer.WriteEndObject();
        }
    }
}

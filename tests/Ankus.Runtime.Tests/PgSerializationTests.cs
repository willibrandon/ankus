using System.Buffers;
using System.Globalization;
using System.Text;

namespace Ankus.Runtime.Tests;

/// <summary>
/// Verifies the generated serializer's owned JSON and CBOR token boundaries directly.
/// </summary>
[TestClass]
public sealed class PgSerializationTests
{
    /// <summary>
    /// Checks signed integer width transitions against independently specified CBOR bytes.
    /// </summary>
    [TestMethod]
    [DataRow(0L, "00")]
    [DataRow(23L, "17")]
    [DataRow(24L, "1818")]
    [DataRow(255L, "18FF")]
    [DataRow(256L, "190100")]
    [DataRow(65535L, "19FFFF")]
    [DataRow(65536L, "1A00010000")]
    [DataRow(4294967295L, "1AFFFFFFFF")]
    [DataRow(4294967296L, "1B0000000100000000")]
    [DataRow(long.MaxValue, "1B7FFFFFFFFFFFFFFF")]
    [DataRow(-1L, "20")]
    [DataRow(-24L, "37")]
    [DataRow(-25L, "3818")]
    [DataRow(-256L, "38FF")]
    [DataRow(-257L, "390100")]
    [DataRow(-65536L, "39FFFF")]
    [DataRow(-65537L, "3A00010000")]
    [DataRow(long.MinValue, "3B7FFFFFFFFFFFFFFF")]
    public void SignedIntegerFixturesPreserveExactValues(long value, string hex)
    {
        var codec = new ScalarCodec<long>(static (ref PgTypeReader reader) => reader.ReadInt64(), static (writer, item) => writer.WriteInt64(item));
        Assert.AreEqual(value, codec.Read(Convert.FromHexString(hex)));
        Assert.AreSequenceEqual(Convert.FromHexString(hex), Encode(codec, value));
        string json = value.ToString(CultureInfo.InvariantCulture);
        Assert.AreEqual(value, codec.Parse(json));
        Assert.AreEqual(json, codec.Format(value));
    }

    /// <summary>
    /// Keeps the full unsigned range distinct from signed values and floating-point storage.
    /// </summary>
    [TestMethod]
    [DataRow(0UL, "00")]
    [DataRow(9223372036854775808UL, "1B8000000000000000")]
    [DataRow(ulong.MaxValue, "1BFFFFFFFFFFFFFFFF")]
    public void UnsignedIntegerFixturesPreserveExactValues(ulong value, string hex)
    {
        var codec = new ScalarCodec<ulong>(static (ref PgTypeReader reader) => reader.ReadUInt64(), static (writer, item) => writer.WriteUInt64(item));
        Assert.AreEqual(value, codec.Read(Convert.FromHexString(hex)));
        Assert.AreSequenceEqual(Convert.FromHexString(hex), Encode(codec, value));
        string json = value.ToString(CultureInfo.InvariantCulture);
        Assert.AreEqual(value, codec.Parse(json));
        Assert.AreEqual(json, codec.Format(value));
    }

    /// <summary>
    /// Rejects range overflow and token coercion at the signed integer boundary.
    /// </summary>
    [TestMethod]
    [DataRow("9223372036854775808", "1B8000000000000000")]
    [DataRow("-9223372036854775809", "3B8000000000000000")]
    [DataRow("1.0", "F93C00")]
    [DataRow("true", "F5")]
    [DataRow("\"1\"", "6131")]
    [DataRow("null", "F6")]
    public void SignedIntegersRejectOverflowAndWrongTokenKinds(string json, string hex)
    {
        var codec = new ScalarCodec<long>(static (ref PgTypeReader reader) => reader.ReadInt64(), static (writer, item) => writer.WriteInt64(item));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse(json)).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => codec.Read(Convert.FromHexString(hex))).SqlState);
    }

    /// <summary>
    /// Refuses negative and out-of-range unsigned values without wrapping.
    /// </summary>
    [TestMethod]
    public void UnsignedIntegersRejectNegativeAndOverflowingValues()
    {
        var codec = new ScalarCodec<ulong>(static (ref PgTypeReader reader) => reader.ReadUInt64(), static (writer, item) => writer.WriteUInt64(item));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse("-1")).SqlState);
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse("18446744073709551616")).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => codec.Read([0x20])).SqlState);
    }

    /// <summary>
    /// Encodes Boolean tokens independently of integer and string conventions.
    /// </summary>
    [TestMethod]
    [DataRow(false, "false", "F4")]
    [DataRow(true, "true", "F5")]
    public void BooleanFixturesRequireBooleanTokens(bool value, string json, string hex)
    {
        var codec = new ScalarCodec<bool>(static (ref PgTypeReader reader) => reader.ReadBoolean(), static (writer, item) => writer.WriteBoolean(item));
        Assert.AreEqual(value, codec.Parse(json));
        Assert.AreEqual(json, codec.Format(value));
        Assert.AreEqual(value, codec.Read(Convert.FromHexString(hex)));
        Assert.AreSequenceEqual(Convert.FromHexString(hex), Encode(codec, value));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse("1")).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => codec.Read([1])).SqlState);
    }

    /// <summary>
    /// Reads decimal fractions from the standard exponent/mantissa representation without binary floating point.
    /// </summary>
    [TestMethod]
    [DataRow("123.45", "C48221193039")]
    [DataRow("-123.45", "C48221393038")]
    [DataRow("12300", "C48202187B")]
    [DataRow("10000000000000000000000000000", "C482181C01")]
    [DataRow("18446744073709551615", "1BFFFFFFFFFFFFFFFF")]
    [DataRow("-18446744073709551616", "3BFFFFFFFFFFFFFFFF")]
    public void DecimalFixturesRemainExact(string json, string hex)
    {
        var codec = new ScalarCodec<decimal>(static (ref PgTypeReader reader) => reader.ReadDecimal(), static (writer, item) => writer.WriteDecimal(item));
        decimal expected = decimal.Parse(json, CultureInfo.InvariantCulture);
        Assert.AreEqual(expected, codec.Read(Convert.FromHexString(hex)));
        Assert.AreEqual(expected, codec.Parse(json));
        Assert.AreEqual(json, codec.Format(expected));
        Assert.AreEqual(expected, codec.Read(Encode(codec, expected)));
    }

    /// <summary>
    /// Writes the standard decimal-fraction tag and retains the decimal scale and extrema.
    /// </summary>
    [TestMethod]
    public void DecimalEncodingPreservesScaleAndNinetySixBitRange()
    {
        var codec = new ScalarCodec<decimal>(static (ref PgTypeReader reader) => reader.ReadDecimal(), static (writer, item) => writer.WriteDecimal(item));
        Assert.AreSequenceEqual(Convert.FromHexString("C48221193039"), Encode(codec, 123.45m));
        decimal[] values = [decimal.MinValue, decimal.MaxValue, 0.0000000000000000000000000001m, 123.4500m];
        foreach (decimal value in values)
        {
            Assert.AreSequenceEqual(decimal.GetBits(value), decimal.GetBits(codec.Read(Encode(codec, value))));
            Assert.AreSequenceEqual(decimal.GetBits(value), decimal.GetBits(codec.Parse(codec.Format(value))));
        }
    }

    /// <summary>
    /// Preserves positive decimal zero's exact scale at the minimum, interior and maximum boundaries.
    /// </summary>
    [TestMethod]
    [DataRow(0, "0", "C4820000")]
    [DataRow(2, "0.00", "C4822100")]
    [DataRow(28, "0.0000000000000000000000000000", "C482381B00")]
    public void DecimalZeroPreservesExactScale(int scale, string json, string hex)
    {
        var codec = new ScalarCodec<decimal>(static (ref PgTypeReader reader) => reader.ReadDecimal(), static (writer, item) => writer.WriteDecimal(item));
        decimal value = new(0, 0, 0, false, (byte)scale);
        int[] expectedBits = [0, 0, 0, scale << 16];
        Assert.AreSequenceEqual(expectedBits, decimal.GetBits(codec.Read(Convert.FromHexString(hex))));
        Assert.AreSequenceEqual(expectedBits, decimal.GetBits(codec.Parse(json)));
        Assert.AreSequenceEqual(Convert.FromHexString(hex), Encode(codec, value));
        Assert.AreEqual(json, codec.Format(value));
    }

    /// <summary>
    /// Rejects negative decimal zero before JSON or CBOR could silently discard its sign.
    /// </summary>
    [TestMethod]
    [DataRow(0, "-0")]
    [DataRow(2, "-0.00")]
    [DataRow(28, "-0.0000000000000000000000000000")]
    public void NegativeDecimalZeroIsRejectedAtEveryFormatBoundary(int scale, string json)
    {
        var codec = new ScalarCodec<decimal>(static (ref PgTypeReader reader) => reader.ReadDecimal(), static (writer, item) => writer.WriteDecimal(item));
        decimal value = new(0, 0, 0, true, (byte)scale);
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse(json)).SqlState);
        Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentException>(() => codec.Format(value)).ParamName);
        Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentException>(() => Encode(codec, value)).ParamName);
    }

    /// <summary>
    /// Requires rejection when a JSON decimal would otherwise round or underflow silently.
    /// </summary>
    [TestMethod]
    [DataRow("0.12345678901234567890123456789")]
    [DataRow("1e-29")]
    [DataRow("79228162514264337593543950336")]
    public void DecimalInputRejectsLossyConversions(string json)
    {
        var codec = new ScalarCodec<decimal>(static (ref PgTypeReader reader) => reader.ReadDecimal(), static (writer, item) => writer.WriteDecimal(item));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse(json)).SqlState);
    }

    /// <summary>
    /// Accepts equivalent decimal spellings whose insignificant zeros do not change the value.
    /// </summary>
    [TestMethod]
    [DataRow("1.23000000000000000000000000000", "1.23")]
    [DataRow("123e-2", "1.23")]
    [DataRow("12300e-4", "1.23")]
    [DataRow("-0.0012300E3", "-1.23")]
    [DataRow("0e-9999999999999999999999999999999999999", "0")]
    public void DecimalInputAcceptsExactEquivalentSpellings(string json, string expected)
    {
        var codec = new ScalarCodec<decimal>(static (ref PgTypeReader reader) => reader.ReadDecimal(), static (writer, item) => writer.WriteDecimal(item));
        Assert.AreEqual(decimal.Parse(expected, CultureInfo.InvariantCulture), codec.Parse(json));
    }

    /// <summary>
    /// Rejects decimal fractional exponents outside the representable range and binary floats.
    /// </summary>
    [TestMethod]
    [DataRow("C482381C01")]
    [DataRow("C482181D01")]
    [DataRow("C482181C08")]
    [DataRow("FB3FF0000000000000")]
    [DataRow("C4822001FF")]
    [DataRow("C482381C00")]
    [DataRow("C5822001")]
    [DataRow("C48100")]
    [DataRow("C482F9000001")]
    [DataRow("C482206130")]
    [DataRow("C48220C06178")]
    public void DecimalBinaryInputRejectsInvalidAndLossyValues(string hex)
    {
        var codec = new ScalarCodec<decimal>(static (ref PgTypeReader reader) => reader.ReadDecimal(), static (writer, item) => writer.WriteDecimal(item));
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => codec.Read(Convert.FromHexString(hex))).SqlState);
    }

    /// <summary>
    /// Keeps negative zero's sign in both text and binary conversions.
    /// </summary>
    [TestMethod]
    public void FloatingPointNegativeZeroPreservesItsSign()
    {
        var singleCodec = new ScalarCodec<float>(static (ref PgTypeReader reader) => reader.ReadSingle(), static (writer, item) => writer.WriteSingle(item));
        var doubleCodec = new ScalarCodec<double>(static (ref PgTypeReader reader) => reader.ReadDouble(), static (writer, item) => writer.WriteDouble(item));
        float negativeSingle = BitConverter.Int32BitsToSingle(int.MinValue);
        double negativeDouble = BitConverter.Int64BitsToDouble(long.MinValue);
        Assert.AreEqual(int.MinValue, BitConverter.SingleToInt32Bits(singleCodec.Read(Convert.FromHexString("F98000"))));
        Assert.AreEqual(long.MinValue, BitConverter.DoubleToInt64Bits(doubleCodec.Read(Convert.FromHexString("F98000"))));
        Assert.AreEqual(int.MinValue, BitConverter.SingleToInt32Bits(singleCodec.Parse("-0")));
        Assert.AreEqual(long.MinValue, BitConverter.DoubleToInt64Bits(doubleCodec.Parse("-0")));
        Assert.AreEqual(int.MinValue, BitConverter.SingleToInt32Bits(singleCodec.Read(Encode(singleCodec, negativeSingle))));
        Assert.AreEqual(long.MinValue, BitConverter.DoubleToInt64Bits(doubleCodec.Read(Encode(doubleCodec, negativeDouble))));
        Assert.AreEqual("-0", singleCodec.Format(negativeSingle));
        Assert.AreEqual("-0", doubleCodec.Format(negativeDouble));
    }

    /// <summary>
    /// Rejects integer and binary64 inputs that a requested floating-point type cannot represent exactly.
    /// </summary>
    [TestMethod]
    public void FloatingPointInputRejectsBinaryPrecisionLoss()
    {
        var singleCodec = new ScalarCodec<float>(static (ref PgTypeReader reader) => reader.ReadSingle(), static (writer, item) => writer.WriteSingle(item));
        var doubleCodec = new ScalarCodec<double>(static (ref PgTypeReader reader) => reader.ReadDouble(), static (writer, item) => writer.WriteDouble(item));
        Assert.AreEqual(16777216f, singleCodec.Read(Convert.FromHexString("1A01000000")));
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => singleCodec.Read(Convert.FromHexString("1A01000001"))).SqlState);
        Assert.AreEqual(9007199254740992d, doubleCodec.Read(Convert.FromHexString("1B0020000000000000")));
        Assert.AreEqual(-9007199254740992d, doubleCodec.Read(Convert.FromHexString("3B001FFFFFFFFFFFFF")));
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => doubleCodec.Read(Convert.FromHexString("1B0020000000000001"))).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => doubleCodec.Read(Convert.FromHexString("3B0020000000000000"))).SqlState);
        Assert.AreEqual(1.5f, singleCodec.Read(Convert.FromHexString("FB3FF8000000000000")));
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => singleCodec.Read(Convert.FromHexString("FB3FF0000000000001"))).SqlState);
    }

    /// <summary>
    /// Preserves CBOR nonfinite values while rejecting JSON's unrepresentable numeric range.
    /// </summary>
    [TestMethod]
    public void FloatingPointSpecialValuesRespectFormatLimits()
    {
        var singleCodec = new ScalarCodec<float>(static (ref PgTypeReader reader) => reader.ReadSingle(), static (writer, item) => writer.WriteSingle(item));
        var doubleCodec = new ScalarCodec<double>(static (ref PgTypeReader reader) => reader.ReadDouble(), static (writer, item) => writer.WriteDouble(item));
        Assert.AreEqual(float.PositiveInfinity, singleCodec.Read(Convert.FromHexString("F97C00")));
        Assert.AreEqual(double.NegativeInfinity, doubleCodec.Read(Convert.FromHexString("F9FC00")));
        Assert.IsTrue(double.IsNaN(doubleCodec.Read(Convert.FromHexString("F97E00"))));
        Assert.AreEqual(float.PositiveInfinity, singleCodec.Read(Encode(singleCodec, float.PositiveInfinity)));
        Assert.AreEqual(double.NegativeInfinity, doubleCodec.Read(Encode(doubleCodec, double.NegativeInfinity)));
        Assert.IsTrue(double.IsNaN(doubleCodec.Read(Encode(doubleCodec, double.NaN))));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => singleCodec.Parse("3.5e38")).SqlState);
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => doubleCodec.Parse("1e309")).SqlState);
        Assert.ThrowsExactly<ArgumentException>(() => singleCodec.Format(float.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentException>(() => doubleCodec.Format(double.NaN));
    }

    /// <summary>
    /// Keeps representable subnormal values and rejects a nonzero number that would become zero.
    /// </summary>
    [TestMethod]
    public void FloatingPointUnderflowCannotSilentlyBecomeZero()
    {
        var singleCodec = new ScalarCodec<float>(static (ref PgTypeReader reader) => reader.ReadSingle(), static (writer, item) => writer.WriteSingle(item));
        var doubleCodec = new ScalarCodec<double>(static (ref PgTypeReader reader) => reader.ReadDouble(), static (writer, item) => writer.WriteDouble(item));
        Assert.AreEqual(float.Epsilon, singleCodec.Parse("1.401298464324817e-45"));
        Assert.AreEqual(double.Epsilon, doubleCodec.Parse("4.9406564584124654e-324"));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => singleCodec.Parse("1e-46")).SqlState);
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => singleCodec.Parse("-1e-46")).SqlState);
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => doubleCodec.Parse("1e-324")).SqlState);
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => doubleCodec.Parse("-1e-324")).SqlState);
        Assert.AreEqual(0f, singleCodec.Parse("0e100"));
        Assert.AreEqual(0d, doubleCodec.Parse("0e100"));
    }

    /// <summary>
    /// Uses independent UTF-8 fixtures and detaches decoded strings from borrowed storage.
    /// </summary>
    [TestMethod]
    [DataRow("", "60")]
    [DataRow("a\0é😀", "686100C3A9F09F9880")]
    public void UnicodeStringFixturesPreserveEveryCodePoint(string value, string hex)
    {
        var codec = new ScalarCodec<string>(static (ref PgTypeReader reader) => reader.ReadString(), static (writer, item) => writer.WriteString(item));
        byte[] input = Convert.FromHexString(hex);
        string decoded = codec.Read(input);
        input.AsSpan().Clear();
        Assert.AreEqual(value, decoded);
        Assert.AreSequenceEqual(Convert.FromHexString(hex), Encode(codec, value));
        Assert.AreEqual(value, codec.Parse(codec.Format(value)));
    }

    /// <summary>
    /// Rejects ill-formed Unicode rather than replacing it with another stored value.
    /// </summary>
    [TestMethod]
    public void InvalidUnicodeIsRejectedAtTextAndBinaryBoundaries()
    {
        var codec = new ScalarCodec<string>(static (ref PgTypeReader reader) => reader.ReadString(), static (writer, item) => writer.WriteString(item));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse("\"\\uD800\"")).SqlState);
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse("\"\uD800\"")).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => codec.Read(Convert.FromHexString("62C080"))).SqlState);
        Assert.ThrowsExactly<EncoderFallbackException>(() => codec.Format("\uD800"));
        Assert.ThrowsExactly<EncoderFallbackException>(() => Encode(codec, "\uD800"));
    }

    /// <summary>
    /// Requires one complete root value and reports the format-specific PostgreSQL error.
    /// </summary>
    [TestMethod]
    [DataRow("", "")]
    [DataRow("1 2", "0102")]
    [DataRow("\"unterminated", "1B00")]
    [DataRow("[1]", "8101")]
    public void MalformedAndTrailingInputHasFormatSpecificSqlState(string json, string hex)
    {
        var codec = new ScalarCodec<long>(static (ref PgTypeReader reader) => reader.ReadInt64(), static (writer, item) => writer.WriteInt64(item));
        PgException textError = Assert.ThrowsExactly<PgException>(() => codec.Parse(json));
        PgException binaryError = Assert.ThrowsExactly<PgException>(() => codec.Read(Convert.FromHexString(hex)));
        Assert.AreEqual("22P02", textError.SqlState);
        Assert.AreEqual("22P03", binaryError.SqlState);
        Assert.IsNotNull(textError.InnerException);
        Assert.IsNotNull(binaryError.InnerException);
        Assert.AreEqual(42L, codec.Parse("42"));
        Assert.AreEqual(42L, codec.Read([0x18, 0x2A]));
    }

    /// <summary>
    /// Allows nested null values while refusing to convert a present root value into SQL NULL.
    /// </summary>
    [TestMethod]
    public void NullableElementsAndRequiredRootRemainDistinct()
    {
        var codec = new NullableStringsCodec();
        string?[] expected = [null, "", "value"];
        Assert.AreSequenceEqual(expected, codec.Parse("[null,\"\",\"value\"]"));
        Assert.AreSequenceEqual(expected, codec.Read(Convert.FromHexString("83F6606576616C7565")));
        Assert.AreEqual("[null,\"\",\"value\"]", codec.Format(expected));
        Assert.AreSequenceEqual(Convert.FromHexString("83F6606576616C7565"), Encode(codec, expected));
        Assert.IsEmpty(codec.Parse("[]"));
        Assert.IsEmpty(codec.Read([0x80]));
        Assert.AreSequenceEqual<string?>([null], codec.Parse("[null]"));
        Assert.AreSequenceEqual<string?>([null], codec.Read([0x81, 0xF6]));
        Assert.IsEmpty(codec.Read([0x9F, 0xFF]));
        var nullCodec = new ScalarCodec<string?>(static (ref PgTypeReader reader) => reader.ReadNull() ? null : reader.ReadString(), static (writer, item) => writer.WriteString(item!));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => nullCodec.Parse("null")).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => nullCodec.Read([0xF6])).SqlState);
    }

    /// <summary>
    /// Skips unknown containers and continues at the next known property in either format.
    /// </summary>
    [TestMethod]
    public void UnknownMembersPreserveFollowingValues()
    {
        var codec = new KnownMemberCodec();
        Assert.AreEqual(42L, codec.Parse("{\"Unknown\":[{\"nested\":[true,null,\"é\"]}],\"Known\":42}"));
        Assert.AreEqual(42L, codec.Read(Convert.FromHexString("A267556E6B6E6F776E81A1666E657374656483F5F662C3A9654B6E6F776E182A")));
        Assert.AreEqual("{\"Known\":42}", codec.Format(42));
        Assert.AreSequenceEqual(Convert.FromHexString("A1654B6E6F776E182A"), Encode(codec, 42L));
        Assert.AreEqual(42L, codec.Read(Convert.FromHexString("BF66556E757365649F0102FF654B6E6F776E182AFF")));
    }

    /// <summary>
    /// Applies Unicode validation even to values omitted from the generated contract.
    /// </summary>
    [TestMethod]
    public void UnknownMembersCannotHideInvalidUnicode()
    {
        var codec = new KnownMemberCodec();
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse("{\"Unknown\":[\"\\uD800\"],\"Known\":42}")).SqlState);
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse("{\"\\uD800\":1,\"Known\":42}")).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => codec.Read(Convert.FromHexString("A267556E6B6E6F776E8162C080654B6E6F776E182A"))).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => codec.Read(Convert.FromHexString("A262C08001654B6E6F776E182A"))).SqlState);
    }

    /// <summary>
    /// Applies the same strict Unicode validation to dictionary keys and serialized string values.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WriterRejectsInvalidUnicodePropertyNames(bool json)
    {
        var destination = new ArrayBufferWriter<byte>();
        using var writer = new PgTypeWriter(destination, json);
        writer.WriteStartObject(1);
        Assert.ThrowsExactly<EncoderFallbackException>(() => writer.WritePropertyName("\uD800"));
    }

    /// <summary>
    /// Accepts the exact depth boundary and rejects the immediately deeper value on input and output.
    /// </summary>
    [TestMethod]
    public void NestingLimitAcceptsSixtyFourLevelsAndRejectsSixtyFive()
    {
        var codec = new NestingCodec();
        string atLimit = new string('[', 64) + new string(']', 64);
        string beyondLimit = new string('[', 65) + new string(']', 65);
        byte[] binaryAtLimit = [.. Enumerable.Repeat((byte)0x81, 63), 0x80];
        byte[] binaryBeyondLimit = [.. Enumerable.Repeat((byte)0x81, 64), 0x80];
        Assert.AreEqual(64, codec.Parse(atLimit));
        Assert.AreEqual(64, codec.Read(binaryAtLimit));
        Assert.AreEqual(atLimit, codec.Format(64));
        Assert.AreSequenceEqual(binaryAtLimit, Encode(codec, 64));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse(beyondLimit)).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => codec.Read(binaryBeyondLimit)).SqlState);
        Assert.ThrowsExactly<InvalidOperationException>(() => codec.Format(65));
        Assert.ThrowsExactly<InvalidOperationException>(() => Encode(codec, 65));
    }

    /// <summary>
    /// Counts skipped nesting together with the enclosing known object.
    /// </summary>
    [TestMethod]
    public void UnknownMemberDepthSharesTheKnownContainerLimit()
    {
        var codec = new KnownMemberCodec();
        string atLimit = "{\"Unknown\":" + new string('[', 63) + new string(']', 63) + ",\"Known\":42}";
        string beyondLimit = "{\"Unknown\":" + new string('[', 64) + new string(']', 64) + ",\"Known\":42}";
        byte[] binaryAtLimit = [.. Convert.FromHexString("A267556E6B6E6F776E"), .. Enumerable.Repeat((byte)0x81, 62), 0x80, .. Convert.FromHexString("654B6E6F776E182A")];
        byte[] binaryBeyondLimit = [.. Convert.FromHexString("A267556E6B6E6F776E"), .. Enumerable.Repeat((byte)0x81, 63), 0x80, .. Convert.FromHexString("654B6E6F776E182A")];
        Assert.AreEqual(42L, codec.Parse(atLimit));
        Assert.AreEqual(42L, codec.Read(binaryAtLimit));
        string populatedAtLimit = "{\"Unknown\":" + new string('[', 63) + "0" + new string(']', 63) + ",\"Known\":42}";
        byte[] binaryPopulatedAtLimit = [.. Convert.FromHexString("A267556E6B6E6F776E"), .. Enumerable.Repeat((byte)0x81, 63), 0, .. Convert.FromHexString("654B6E6F776E182A")];
        Assert.AreEqual(42L, codec.Parse(populatedAtLimit));
        Assert.AreEqual(42L, codec.Read(binaryPopulatedAtLimit));
        byte[] binaryTaggedAtLimit = [.. Convert.FromHexString("A267556E6B6E6F776E"), .. Enumerable.Repeat((byte)0x81, 63), 0xC1, 0, .. Convert.FromHexString("654B6E6F776E182A")];
        Assert.AreEqual(42L, codec.Read(binaryTaggedAtLimit));
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => codec.Parse(beyondLimit)).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => codec.Read(binaryBeyondLimit)).SqlState);
    }

    /// <summary>
    /// Appends to caller-owned storage and rejects absent required API arguments.
    /// </summary>
    [TestMethod]
    public void CodecWritesAppendAndValidateArguments()
    {
        var codec = new ScalarCodec<long>(static (ref PgTypeReader reader) => reader.ReadInt64(), static (writer, item) => writer.WriteInt64(item));
        var destination = new ArrayBufferWriter<byte>();
        destination.Write<byte>([0xFF]);
        codec.Write(42, destination);
        Assert.AreSequenceEqual(new byte[] { 0xFF, 0x18, 0x2A }, destination.WrittenSpan.ToArray());
        Assert.AreEqual("text", Assert.ThrowsExactly<ArgumentNullException>(() => codec.Parse(null!)).ParamName);
        Assert.AreEqual("destination", Assert.ThrowsExactly<ArgumentNullException>(() => codec.Write(1, null!)).ParamName);
    }

    /// <summary>
    /// Requires the generated callback to consume and emit one complete root value.
    /// </summary>
    [TestMethod]
    public void CodecRejectsAbsentAndIncompleteRootValues()
    {
        var emptyCodec = new ScalarCodec<int>(static (ref PgTypeReader reader) => 1, static (writer, item) => { });
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => emptyCodec.Parse("")).SqlState);
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => emptyCodec.Parse(" \r\n\t")).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => emptyCodec.Read([])).SqlState);
        Assert.AreEqual("22P02", Assert.ThrowsExactly<PgException>(() => emptyCodec.Parse("1")).SqlState);
        Assert.AreEqual("22P03", Assert.ThrowsExactly<PgException>(() => emptyCodec.Read([1])).SqlState);
        Assert.ThrowsExactly<InvalidOperationException>(() => emptyCodec.Format(1));
        Assert.ThrowsExactly<InvalidOperationException>(() => Encode(emptyCodec, 1));
        var incompleteCodec = new ScalarCodec<int>(static (ref PgTypeReader reader) => 1, static (writer, item) =>
        {
            writer.WriteStartArray(1);
            writer.WriteInt64(item);
        });
        Assert.ThrowsExactly<InvalidOperationException>(() => incompleteCodec.Format(1));
        Assert.ThrowsExactly<InvalidOperationException>(() => Encode(incompleteCodec, 1));
    }

    /// <summary>
    /// Materializes one independently owned binary payload from the public codec contract.
    /// </summary>
    private static byte[] Encode<T>(PgTypeCodec<T> codec, T value)
    {
        var destination = new ArrayBufferWriter<byte>();
        codec.Write(value, destination);
        return destination.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Represents one statically bound token read without boxing the ref struct cursor.
    /// </summary>
    private delegate T ReadScalar<T>(ref PgTypeReader reader);

    /// <summary>
    /// Selects scalar tokens directly while exercising the common codec format boundary.
    /// </summary>
    private sealed class ScalarCodec<T>(ReadScalar<T> read, Action<PgTypeWriter, T> write) : PgSerializedTypeCodec<T>
    {
        /// <inheritdoc />
        protected override T ReadValue(ref PgTypeReader reader) => read(ref reader);

        /// <inheritdoc />
        protected override void WriteValue(PgTypeWriter writer, T value) => write(writer, value);
    }

    /// <summary>
    /// Exercises nullable scalar elements inside required containers.
    /// </summary>
    private sealed class NullableStringsCodec : PgSerializedTypeCodec<string?[]>
    {
        /// <inheritdoc />
        protected override string?[] ReadValue(ref PgTypeReader reader)
        {
            reader.ReadStartArray();
            var values = new List<string?>();
            while (!reader.ReadEndArray())
            {
                values.Add(reader.ReadNull() ? null : reader.ReadString());
            }

            return [.. values];
        }

        /// <inheritdoc />
        protected override void WriteValue(PgTypeWriter writer, string?[] value)
        {
            writer.WriteStartArray(value.Length);
            foreach (string? item in value)
            {
                if (item is null)
                {
                    writer.WriteNull();
                }
                else
                {
                    writer.WriteString(item);
                }
            }

            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Uses one known property to prove traversal after unknown values.
    /// </summary>
    private sealed class KnownMemberCodec : PgSerializedTypeCodec<long>
    {
        /// <inheritdoc />
        protected override long ReadValue(ref PgTypeReader reader)
        {
            reader.ReadStartObject();
            long value = 0;
            while (reader.ReadPropertyName() is { } name)
            {
                if (name == "Known")
                {
                    value = reader.ReadInt64();
                }
                else
                {
                    reader.Skip();
                }
            }

            return value;
        }

        /// <inheritdoc />
        protected override void WriteValue(PgTypeWriter writer, long value)
        {
            writer.WriteStartObject(1);
            writer.WritePropertyName("Known");
            writer.WriteInt64(value);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Traverses one nested array per level without introducing object identity behavior.
    /// </summary>
    private sealed class NestingCodec : PgSerializedTypeCodec<int>
    {
        /// <inheritdoc />
        protected override int ReadValue(ref PgTypeReader reader)
        {
            reader.ReadStartArray();
            if (reader.ReadEndArray())
            {
                return 1;
            }

            int depth = ReadValue(ref reader) + 1;
            if (!reader.ReadEndArray())
            {
                throw new FormatException("Expected exactly one nested array.");
            }

            return depth;
        }

        /// <inheritdoc />
        protected override void WriteValue(PgTypeWriter writer, int value)
        {
            writer.WriteStartArray(value == 1 ? 0 : 1);
            if (value > 1)
            {
                WriteValue(writer, value - 1);
            }

            writer.WriteEndArray();
        }
    }
}

using System.Buffers.Binary;
using System.Text;

namespace Ankus.Build.Tests;

/// <summary>
/// Exercises bounded native metadata, auxiliary records, extended indices and rejection without partial results.
/// </summary>
[TestClass]
public sealed class NativeObjectSymbolTests
{
    /// <summary>
    /// Each independently encoded container preserves names, byte order and identity while excluding non-import records.
    /// </summary>
    /// <param name="kind">The native metadata representation.</param>
    [TestMethod]
    [DataRow("coff")]
    [DataRow("big-coff")]
    [DataRow("elf32")]
    [DataRow("elf64")]
    [DataRow("elf64-be")]
    [DataRow("mach32")]
    [DataRow("mach64")]
    [DataRow("mach64-be")]
    public void NativeObjectSymbolsRetainContracts(string kind)
    {
        Sample sample = Create(kind);
        byte[] original = [.. sample.Image];
        NativeObjectImports imports = NativeObjectSymbols.Read(sample.Image, "ankus_");
        Assert.AreEqual(sample.Format, imports.Format);
        Assert.AreEqual(sample.Architecture, imports.Architecture);
        Assert.AreEqual(sample.LittleEndian, imports.IsLittleEndian);
        Assert.AreSequenceEqual(sample.Expected, imports.Symbols);
        Assert.IsEmpty(NativeObjectSymbols.Read(sample.Image, "not_a_symbol").Symbols);
        Assert.AreSequenceEqual(original, sample.Image);
        IList<string> names = Assert.IsInstanceOfType<IList<string>>(imports.Symbols);
        Assert.ThrowsExactly<NotSupportedException>(() => names[0] = "changed");
        Assert.AreSequenceEqual(sample.Expected, NativeObjectSymbols.Read(sample.Image, "ankus_").Symbols);
    }

    /// <summary>
    /// Corrupt metadata and truncation reject before returning a selection; a later valid read retains all imports.
    /// </summary>
    /// <param name="kind">The native metadata representation.</param>
    [TestMethod]
    [DataRow("coff")]
    [DataRow("big-coff")]
    [DataRow("elf32")]
    [DataRow("elf64")]
    [DataRow("elf64-be")]
    [DataRow("mach32")]
    [DataRow("mach64")]
    [DataRow("mach64-be")]
    public void NativeObjectSymbolsRejectInvalidContracts(string kind)
    {
        Sample sample = Create(kind);
        foreach ((string name, int offset, int width, ulong value) in sample.Faults)
        {
            byte[] changed = [.. sample.Image];
            Write(changed, offset, width, value, sample.LittleEndian);
            Assert.ThrowsExactly<FormatException>(() => NativeObjectSymbols.Read(changed, "ankus_"), name);
            Assert.AreSequenceEqual(sample.Expected, NativeObjectSymbols.Read(sample.Image, "ankus_").Symbols, name);
        }

        for (int length = 0; length < sample.Image.Length; length++)
        {
            byte[] truncated = sample.Image[..length];
            Assert.ThrowsExactly<FormatException>(() => NativeObjectSymbols.Read(truncated, "ankus_"), $"Truncated at {length}.");
        }

        Assert.AreSequenceEqual(sample.Expected, NativeObjectSymbols.Read(sample.Image, "ankus_").Symbols);
    }

    /// <summary>
    /// The ELF section-zero count and extended symbol indices retain their defined-versus-import distinction.
    /// </summary>
    /// <param name="wide">Whether the ELF fields use 64-bit sizes.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeObjectSymbolsRetainExtendedElfSections(bool wide)
    {
        Sample sample = Elf(wide, true);
        int sectionOffset = (int)Read(sample.Image, wide ? 40 : 32, wide ? 8 : 4);
        Write(sample.Image, wide ? 60 : 48, 2, 0);
        Write(sample.Image, sectionOffset + (wide ? 32 : 20), wide ? 8 : 4, 4);
        Assert.AreSequenceEqual(sample.Expected, NativeObjectSymbols.Read(sample.Image, "ankus_").Symbols);
        Write(sample.Image, sectionOffset + (wide ? 32 : 20), wide ? 8 : 4, ulong.MaxValue >> (wide ? 0 : 32));
        Assert.ThrowsExactly<FormatException>(() => NativeObjectSymbols.Read(sample.Image, "ankus_"));
    }

    /// <summary>
    /// Invalid text in the generated namespace rejects, while unrelated native symbol bytes are not decoded.
    /// </summary>
    [TestMethod]
    public void NativeObjectSymbolsValidateOnlySelectedNameEncoding()
    {
        Sample sample = Coff(false);
        int position = sample.Image.AsSpan().IndexOf("ankus_external"u8);
        sample.Image[position + 7] = 0xff;
        Assert.ThrowsExactly<FormatException>(() => NativeObjectSymbols.Read(sample.Image, "ankus_"));
        Assert.IsEmpty(NativeObjectSymbols.Read(sample.Image, "unrelated_").Symbols);
    }

    /// <summary>
    /// Selection must name a nonempty generated namespace before examining object bytes.
    /// </summary>
    [TestMethod]
    public void NativeObjectSymbolsRequireNamespace()
    {
        ArgumentNullException missing = Assert.ThrowsExactly<ArgumentNullException>(() => NativeObjectSymbols.Read([], null!));
        Assert.AreEqual("prefix", missing.ParamName);
        ArgumentException empty = Assert.ThrowsExactly<ArgumentException>(() => NativeObjectSymbols.Read([], ""));
        Assert.AreEqual("prefix", empty.ParamName);
    }

    private static Sample Create(string kind) => kind switch
    {
        "coff" => Coff(false), "big-coff" => Coff(true),
        "elf32" => Elf(false, true), "elf64" => Elf(true, true), "elf64-be" => Elf(true, false),
        "mach32" => Mach(false, true), "mach64" => Mach(true, true), "mach64-be" => Mach(true, false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static Sample Coff(bool big)
    {
        int header = big ? 56 : 20;
        int width = big ? 20 : 18;
        int table = header + 40;
        int strings = table + 8 * width;
        byte[] names = Encoding.UTF8.GetBytes("ankus_external\0ankus_weak\0ankus_defined\0ankus_common\0ankus_local\0");
        byte[] image = new byte[strings + 4 + names.Length];
        var sample = new Sample(image, "coff", "x64", true, ["ankus_external", "ankus_weak", "ankus_x"]);
        if (big)
        {
            Write(image, 2, 2, 0xffff);
            Write(image, 4, 2, 2);
            Write(image, 6, 2, 0x8664);
            new Guid("d1baa1c7-baee-4ba9-af20-faf66aa4dcb8").TryWriteBytes(image.AsSpan(12, 16));
            Write(image, 44, 4, 1);
            Write(image, 48, 4, (uint)table);
            Write(image, 52, 4, 8);
            sample.Faults.AddRange([("big version", 4, 2, 1), ("big signature", 12, 1, 0)]);
        }
        else
        {
            Write(image, 0, 2, 0x8664);
            Write(image, 2, 2, 1);
            Write(image, 8, 4, (uint)table);
            Write(image, 12, 4, 8);
            sample.Faults.AddRange([("optional header", 16, 2, 1), ("executable", 18, 2, 2)]);
        }

        names.CopyTo(image, strings + 4);
        Write(image, strings, 4, (uint)(4 + names.Length));
        void Symbol(int index, string name, uint section, uint value, byte storage, byte auxiliaries = 0)
        {
            int start = table + index * width;
            Write(image, start + 4, 4, (uint)(4 + names.AsSpan().IndexOf(Encoding.UTF8.GetBytes(name))));
            Write(image, start + 8, 4, value);
            Write(image, start + 12, big ? 4 : 2, section);
            image[start + width - 2] = storage;
            image[start + width - 1] = auxiliaries;
        }

        Symbol(0, "ankus_external", 0, 0, 2);
        "ankus_x\0"u8.CopyTo(image.AsSpan(table + width, 8));
        image[table + 2 * width - 2] = 2;
        Symbol(2, "ankus_weak", 0, 0, 105, 1);
        // Weak-external auxiliary: symbol zero is the fallback and SEARCH_ALIAS is three.
        Write(image, table + 3 * width + 4, 4, 3);
        Symbol(4, "ankus_defined", 1, 0, 2);
        Symbol(5, "ankus_common", 0, 16, 2);
        Symbol(6, "ankus_local", 1, 0, 3);
        Symbol(7, "ankus_external", 0, 0, 2);
        sample.Faults.AddRange([
            ("magic", 0, 1, 0xff), ("machine", big ? 6 : 0, 2, 0xffff),
            ("section count", big ? 44 : 2, big ? 4 : 2, big ? uint.MaxValue : ushort.MaxValue),
            ("symbol offset", big ? 48 : 8, 4, uint.MaxValue), ("symbol count", big ? 52 : 12, 4, uint.MaxValue),
            ("string size short", strings, 4, 3), ("string size long", strings, 4, uint.MaxValue),
            ("name range", table + 4, 4, uint.MaxValue), ("name header", table + 4, 4, 1),
            ("section index", table + 12, big ? 4 : 2, 2), ("auxiliary count", table + width - 1, 1, 255),
            ("missing weak auxiliary", table + 3 * width - 1, 1, 0), ("unterminated name", image.Length - 1, 1, 1)]);
        return sample;
    }

    private static Sample Elf(bool wide, bool little)
    {
        int header = wide ? 64 : 52;
        int width = wide ? 24 : 16;
        int sectionWidth = wide ? 64 : 40;
        string[] names = ["", "ankus_external", "ankus_weak", "ankus_defined", "ankus_common", "ankus_local", "ankus_extended", "ankus_external"];
        byte[] text = Encoding.UTF8.GetBytes(string.Join('\0', names) + '\0');
        int strings = header + names.Length * width;
        int extended = strings + text.Length;
        int sections = extended + names.Length * 4;
        byte[] image = new byte[sections + 4 * sectionWidth];
        var sample = new Sample(image, "elf", wide ? little ? "x64" : "arm64" : "x86", little, ["ankus_external", "ankus_weak"]);
        void Put(int offset, int size, ulong value) => Write(image, offset, size, value, little);
        "\u007fELF"u8.CopyTo(image);
        image[4] = wide ? (byte)2 : (byte)1;
        image[5] = little ? (byte)1 : (byte)2;
        image[6] = 1;
        Put(16, 2, 1);
        Put(18, 2, wide ? little ? 62UL : 183UL : 3UL);
        Put(20, 4, 1);
        Put(wide ? 40 : 32, wide ? 8 : 4, (uint)sections);
        Put(wide ? 52 : 40, 2, (uint)header);
        Put(wide ? 58 : 46, 2, (uint)sectionWidth);
        Put(wide ? 60 : 48, 2, 4);
        text.CopyTo(image, strings);
        int nameIndex = 0;
        for (int index = 0; index < names.Length; index++)
        {
            int start = header + index * width;
            Put(start, 4, (uint)nameIndex);
            image[start + (wide ? 4 : 12)] = index is 0 or 5 ? (byte)0 : index == 2 ? (byte)0x22 : (byte)0x12;
            Put(start + (wide ? 6 : 14), 2, index switch { 3 or 5 => 1, 4 => 0xfff2, 6 => 0xffff, _ => 0 });
            Put(start + (wide ? 8 : 4), wide ? 8 : 4, index == 4 ? 8UL : 0UL);
            nameIndex += names[index].Length + 1;
        }

        Put(extended + 6 * 4, 4, 1);
        void Section(int index, uint type, int offset, int size, uint link, uint stride)
        {
            int start = sections + index * sectionWidth;
            Put(start + 4, 4, type);
            Put(start + (wide ? 24 : 16), wide ? 8 : 4, (uint)offset);
            Put(start + (wide ? 32 : 20), wide ? 8 : 4, (uint)size);
            Put(start + (wide ? 40 : 24), 4, link);
            Put(start + (wide ? 56 : 36), wide ? 8 : 4, stride);
        }

        Section(1, 2, header, names.Length * width, 2, (uint)width);
        Section(2, 3, strings, text.Length, 0, 0);
        Section(3, 18, extended, names.Length * 4, 1, 4);
        int symbolSection = sections + sectionWidth;
        int stringSection = sections + 2 * sectionWidth;
        int extendedSection = sections + 3 * sectionWidth;
        ulong max = wide ? ulong.MaxValue : uint.MaxValue;
        sample.Faults.AddRange([
            ("magic", 0, 1, 0xff), ("class", 4, 1, 0), ("byte order", 5, 1, 0), ("identity version", 6, 1, 0),
            ("executable", 16, 2, 2), ("machine", 18, 2, 0xffff), ("version", 20, 4, 2),
            ("header size", wide ? 52 : 40, 2, 1), ("section size", wide ? 58 : 46, 2, 0),
            ("section offset", wide ? 40 : 32, wide ? 8 : 4, max), ("section count", wide ? 60 : 48, 2, ushort.MaxValue),
            ("section zero", sections + 4, 4, 1), ("symbol stride", symbolSection + (wide ? 56 : 36), wide ? 8 : 4, 1),
            ("symbol offset", symbolSection + (wide ? 24 : 16), wide ? 8 : 4, max),
            ("symbol size", symbolSection + (wide ? 32 : 20), wide ? 8 : 4, max),
            ("symbol strings", symbolSection + (wide ? 40 : 24), 4, uint.MaxValue), ("string kind", stringSection + 4, 4, 1),
            ("string size", stringSection + (wide ? 32 : 20), wide ? 8 : 4, max), ("symbol name", header, 4, uint.MaxValue),
            ("symbol section", header + (wide ? 6 : 14), 2, 7), ("unterminated name", strings + text.Length - 1, 1, 1),
            ("missing extended sections", extendedSection + 4, 4, 1), ("extended stride", extendedSection + (wide ? 56 : 36), wide ? 8 : 4, 1),
            ("extended size", extendedSection + (wide ? 32 : 20), wide ? 8 : 4, 4), ("extended null", extended + 6 * 4, 4, 0),
            ("extended range", extended + 6 * 4, 4, uint.MaxValue)]);
        return sample;
    }

    private static Sample Mach(bool wide, bool little)
    {
        int header = wide ? 32 : 28;
        int width = wide ? 16 : 12;
        int symbols = header + 48;
        string[] names = ["_ankus_external", "_ankus_private", "_ankus_weak", "_ankus_defined", "_ankus_common", "_ankus_local", "_ankus_debug", "ankus_undecorated"];
        byte[] text = Encoding.UTF8.GetBytes("\0" + string.Join('\0', names) + '\0');
        int strings = symbols + names.Length * width;
        byte[] image = new byte[strings + text.Length];
        var sample = new Sample(image, "mach-o", wide ? little ? "x64" : "arm64" : "x86", little,
            ["ankus_external", "ankus_private", "ankus_weak"]);
        void Put(int offset, int size, ulong value) => Write(image, offset, size, value, little);
        Put(0, 4, wide ? 0xfeedfacf : 0xfeedface);
        Put(4, 4, wide ? little ? 0x1000007UL : 0x100000cUL : 7UL);
        Put(12, 4, 1);
        Put(16, 4, 2);
        Put(20, 4, 48);
        Put(header, 4, 0x1b);
        Put(header + 4, 4, 24);
        int command = header + 24;
        Put(command, 4, 2);
        Put(command + 4, 4, 24);
        Put(command + 8, 4, (uint)symbols);
        Put(command + 12, 4, (uint)names.Length);
        Put(command + 16, 4, (uint)strings);
        Put(command + 20, 4, (uint)text.Length);
        text.CopyTo(image, strings);
        int nameIndex = 1;
        for (int index = 0; index < names.Length; index++)
        {
            int start = symbols + index * width;
            Put(start, 4, (uint)nameIndex);
            image[start + 4] = index switch { 1 => 0x11, 3 => 0xf, 5 => 0xe, 6 => 0xe0, _ => 1 };
            image[start + 5] = index is 3 or 5 ? (byte)1 : (byte)0;
            Put(start + 6, 2, index == 2 ? 0x40UL : 0UL);
            Put(start + 8, wide ? 8 : 4, index == 4 ? 16UL : 0UL);
            nameIndex += names[index].Length + 1;
        }

        sample.Faults.AddRange([
            ("magic", 0, 1, 0xff), ("machine", 4, 4, 0xffff), ("executable", 12, 4, 2),
            ("command count", 16, 4, uint.MaxValue), ("command size", 20, 4, uint.MaxValue),
            ("command zero", header + 4, 4, 0), ("command alignment", header + 4, 4, 9),
            ("command overflow", header + 4, 4, uint.MaxValue), ("trailing commands", 16, 4, 1),
            ("duplicate symbols", header, 4, 2), ("symbol command size", command + 4, 4, 16),
            ("symbol offset", command + 8, 4, uint.MaxValue), ("symbol count", command + 12, 4, uint.MaxValue),
            ("string offset", command + 16, 4, uint.MaxValue), ("string size", command + 20, 4, uint.MaxValue),
            ("symbol name", symbols, 4, uint.MaxValue), ("unterminated name", image.Length - 1, 1, 1)]);
        return sample;
    }

    private static void Write(byte[] image, int offset, int width, ulong value, bool little = true)
    {
        for (int index = 0; index < width; index++)
        {
            image[offset + index] = (byte)(value >> (8 * (little ? index : width - index - 1)));
        }
    }

    private static ulong Read(byte[] image, int offset, int width)
        => width == 8 ? BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(offset)) : BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset));

    private sealed record Sample(byte[] Image, string Format, string Architecture, bool LittleEndian, string[] Expected)
    {
        /// <summary>
        /// Describes independent metadata corruptions that must reject the complete object.
        /// </summary>
        internal List<(string Name, int Offset, int Width, ulong Value)> Faults { get; } = [];
    }
}

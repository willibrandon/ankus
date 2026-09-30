namespace Ankus.Build;

internal static partial class NativeObjectSymbols
{
    private ref partial struct Reader
    {
        /// <summary>
        /// Reads COFF imports and optionally restricts them to names without remaining references.
        /// </summary>
        internal readonly NativeObjectImports ReadCoff()
        {
            ReadOnlySpan<byte> initial = Slice(0, 20);
            bool big = UInt16(initial) == 0 && UInt16(initial[2..]) == ushort.MaxValue;
            ReadOnlySpan<byte> header = Slice(0, big ? 56UL : 20UL);
            ushort machine;
            uint sections;
            uint offset;
            uint count;
            if (big)
            {
                Require(UInt16(header[4..]) == 2 && header.Slice(12, 16).SequenceEqual<byte>(
                    [0xc7, 0xa1, 0xba, 0xd1, 0xee, 0xba, 0xa9, 0x4b, 0xaf, 0x20, 0xfa, 0xf6, 0x6a, 0xa4, 0xdc, 0xb8]),
                    "Unsupported COFF big-object header.");
                machine = UInt16(header[6..]);
                sections = UInt32(header[44..]);
                offset = UInt32(header[48..]);
                count = UInt32(header[52..]);
            }
            else
            {
                Require(UInt16(header[16..]) == 0 && (UInt16(header[18..]) & 0x2002) == 0, "Expected a relocatable COFF object.");
                machine = UInt16(header);
                sections = UInt16(header[2..]);
                offset = UInt32(header[8..]);
                count = UInt32(header[12..]);
            }

            string architecture = machine switch
            {
                0x8664 => "x64",
                0x14c => "x86",
                0xaa64 => "arm64",
                0x1c0 or 0x1c4 => "arm",
                _ => throw new FormatException("Unsupported COFF processor."),
            };
            ReadOnlySpan<byte> sectionTable = Table((ulong)header.Length, sections, 40);
            if (offset == 0)
            {
                Require(count == 0, "COFF symbols have no table.");
                return Result("coff", architecture);
            }

            int width = big ? 20 : 18;
            ReadOnlySpan<byte> symbols = Table(offset, count, width);
            ulong stringOffset = offset + (ulong)symbols.Length;
            uint stringSize = UInt32(Slice(stringOffset, 4));
            Require(stringSize >= 4, "Invalid COFF string-table size.");
            ReadOnlySpan<byte> strings = Slice(stringOffset, stringSize);
            HashSet<uint> references = _unreferencedOnly ? CoffReferences(sectionTable, symbols, width) : [];
            var retained = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < symbols.Length / width;)
            {
                ReadOnlySpan<byte> symbol = symbols.Slice(index * width, width);
                uint section = big ? UInt32(symbol[12..]) : UInt16(symbol[12..]);
                uint absolute = big ? uint.MaxValue : ushort.MaxValue;
                Require(section <= sections || section == absolute || section == absolute - 1, "Invalid COFF symbol section.");
                byte storage = symbol[width - 2];
                int auxiliaries = symbol[width - 1];
                Require(auxiliaries < symbols.Length / width - index, "COFF auxiliary records exceed the symbol table.");
                ReadOnlySpan<byte> name;
                if (UInt32(symbol) == 0)
                {
                    uint position = UInt32(symbol[4..]);
                    Require(position is 0 or >= 4, "COFF symbol points inside the string-table header.");
                    name = position == 0 ? [] : Name(strings, position);
                }
                else
                {
                    name = symbol[..8];
                    int end = name.IndexOf((byte)0);
                    if (end >= 0)
                    {
                        name = name[..end];
                    }
                }

                if (section == 0 && UInt32(symbol[8..]) == 0 && storage is 2 or 105)
                {
                    if (storage == 105)
                    {
                        Require(auxiliaries > 0, "A weak COFF import requires its auxiliary record.");
                    }

                    if (!_unreferencedOnly || (storage == 2 && !references.Contains((uint)index)))
                    {
                        Add(name, !_unreferencedOnly && architecture == "x86");
                    }
                    else
                    {
                        retained.Add(s_utf8.GetString(name));
                    }
                }

                index += auxiliaries + 1;
            }

            _symbols.ExceptWith(retained);
            return Result("coff", architecture);
        }

        /// <summary>
        /// Retains relocation targets and weak-symbol fallback identities before considering an import unused.
        /// </summary>
        private readonly HashSet<uint> CoffReferences(ReadOnlySpan<byte> sections, ReadOnlySpan<byte> symbols, int width)
        {
            var references = new HashSet<uint>();
            uint symbolCount = (uint)(symbols.Length / width);
            for (int offset = 0; offset < sections.Length; offset += 40)
            {
                ReadOnlySpan<byte> section = sections.Slice(offset, 40);
                uint position = UInt32(section[24..]);
                uint count = UInt16(section[32..]);
                Require(count == 0 || position != 0, "COFF relocations have no table.");
                bool overflow = (UInt32(section[36..]) & 0x01000000) != 0;
                if (overflow)
                {
                    Require(count == ushort.MaxValue, "Invalid COFF relocation overflow count.");
                    count = UInt32(Slice(position, 10));
                    Require(count > ushort.MaxValue, "Invalid COFF extended relocation count.");
                }

                ReadOnlySpan<byte> relocations = Table(position, count, 10);
                for (int index = overflow ? 10 : 0; index < relocations.Length; index += 10)
                {
                    uint symbol = UInt32(relocations[(index + 4)..]);
                    Require(symbol < symbolCount, "COFF relocation exceeds the symbol table.");
                    references.Add(symbol);
                }
            }

            for (int index = 0; index < symbols.Length / width;)
            {
                ReadOnlySpan<byte> symbol = symbols.Slice(index * width, width);
                int auxiliaries = symbol[width - 1];
                Require(auxiliaries < symbols.Length / width - index, "COFF auxiliary records exceed the symbol table.");
                if (symbol[width - 2] == 105)
                {
                    Require(auxiliaries > 0, "A weak COFF import requires its auxiliary record.");
                    uint fallback = UInt32(symbols[((index + 1) * width)..]);
                    Require(fallback < symbolCount, "COFF weak fallback exceeds the symbol table.");
                    references.Add(fallback);
                }

                index += auxiliaries + 1;
            }

            return references;
        }
    }
}

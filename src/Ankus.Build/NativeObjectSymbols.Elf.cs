namespace Ankus.Build;

internal static partial class NativeObjectSymbols
{
    private ref partial struct Reader
    {
        private NativeObjectImports ReadElf()
        {
            ReadOnlySpan<byte> identity = Slice(0, 16);
            Require(identity[4] is 1 or 2 && identity[5] is 1 or 2 && identity[6] == 1, "Invalid ELF class, byte order or version.");
            bool wide = identity[4] == 2;
            _littleEndian = identity[5] == 1;
            ReadOnlySpan<byte> header = Slice(0, wide ? 64UL : 52UL);
            Require(UInt16(header[16..]) == 1 && UInt32(header[20..]) == 1 && UInt16(header[(wide ? 52 : 40)..]) == header.Length,
                "Expected a current relocatable ELF object.");
            string architecture = UInt16(header[18..]) switch
            {
                3 when !wide => "x86", 40 when !wide => "arm", 62 when wide => "x64", 183 when wide => "arm64",
                _ => throw new FormatException("Unsupported ELF processor or word size."),
            };
            ulong sectionOffset = wide ? UInt64(header[40..]) : UInt32(header[32..]);
            int width = wide ? 64 : 40;
            uint count = UInt16(header[(wide ? 60 : 48)..]);
            if (sectionOffset == 0)
            {
                Require(count == 0, "ELF sections have no table.");
                return Result("elf", architecture);
            }

            Require(UInt16(header[(wide ? 58 : 46)..]) == width, "Invalid ELF section-header size.");
            ReadOnlySpan<byte> first = Slice(sectionOffset, (ulong)width);
            Require(UInt32(first[4..]) == 0, "ELF section zero must be null.");
            ulong actualCount = count == 0 ? ElfSize(first, wide) : count;
            Require(actualCount > 0, "Missing ELF extended section count.");
            ReadOnlySpan<byte> sections = Table(sectionOffset, actualCount, width);
            for (int index = 0; index < sections.Length / width; index++)
            {
                ReadOnlySpan<byte> section = sections.Slice(index * width, width);
                if (UInt32(section[4..]) != 2) { continue; }

                int symbolWidth = wide ? 24 : 16;
                ulong size = ElfSize(section, wide);
                ulong entrySize = wide ? UInt64(section[56..]) : UInt32(section[36..]);
                Require(entrySize == (ulong)symbolWidth && size % entrySize == 0, "Invalid ELF symbol-entry size.");
                ReadOnlySpan<byte> symbols = Slice(ElfOffset(section, wide), size);
                uint link = UInt32(section[(wide ? 40 : 24)..]);
                Require(link < actualCount, "ELF symbol strings reference an absent section.");
                ReadOnlySpan<byte> stringSection = sections.Slice((int)link * width, width);
                Require(UInt32(stringSection[4..]) == 3, "ELF symbol names require a string-table section.");
                ReadOnlySpan<byte> strings = Slice(ElfOffset(stringSection, wide), ElfSize(stringSection, wide));
                ReadOnlySpan<byte> extended = [];
                for (int other = 0; other < sections.Length / width; other++)
                {
                    ReadOnlySpan<byte> candidate = sections.Slice(other * width, width);
                    if (UInt32(candidate[4..]) == 18 && UInt32(candidate[(wide ? 40 : 24)..]) == index)
                    {
                        Require(extended.IsEmpty && ElfSize(candidate, wide) == (ulong)(symbols.Length / symbolWidth) * 4 &&
                            (wide ? UInt64(candidate[56..]) : UInt32(candidate[36..])) == 4, "Invalid ELF extended symbol sections.");
                        extended = Slice(ElfOffset(candidate, wide), ElfSize(candidate, wide));
                    }
                }

                for (int offset = 0; offset < symbols.Length; offset += symbolWidth)
                {
                    ReadOnlySpan<byte> symbol = symbols.Slice(offset, symbolWidth);
                    ReadOnlySpan<byte> name = Name(strings, UInt32(symbol));
                    uint sectionIndex = UInt16(symbol[(wide ? 6 : 14)..]);
                    if (sectionIndex == ushort.MaxValue)
                    {
                        Require(!extended.IsEmpty, "ELF symbol has no extended section table.");
                        sectionIndex = UInt32(extended[(offset / symbolWidth * 4)..]);
                        Require(sectionIndex > 0 && sectionIndex < actualCount, "Invalid ELF extended symbol section.");
                    }
                    else
                    {
                        Require(sectionIndex < actualCount || sectionIndex >= 0xff00, "Invalid ELF symbol section.");
                    }

                    int binding = symbol[wide ? 4 : 12] >> 4;
                    ulong value = wide ? UInt64(symbol[8..]) : UInt32(symbol[4..]);
                    if (sectionIndex == 0 && binding is 1 or 2 && value == 0) { Add(name, decorated: false); }
                }
            }

            return Result("elf", architecture);
        }

        private ulong ElfOffset(ReadOnlySpan<byte> section, bool wide) => wide ? UInt64(section[24..]) : UInt32(section[16..]);

        private ulong ElfSize(ReadOnlySpan<byte> section, bool wide) => wide ? UInt64(section[32..]) : UInt32(section[20..]);
    }
}

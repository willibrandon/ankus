namespace Ankus.Build;

internal static partial class NativeObjectSymbols
{
    private ref partial struct Reader
    {
        private NativeObjectImports ReadMach(uint magic)
        {
            _littleEndian = magic is 0xfeedface or 0xfeedfacf;
            bool wide = magic is 0xfeedfacf or 0xcffaedfe;
            ReadOnlySpan<byte> header = Slice(0, wide ? 32UL : 28UL);
            Require(UInt32(header[12..]) == 1, "Expected a relocatable Mach-O object.");
            string architecture = UInt32(header[4..]) switch
            {
                7 when !wide => "x86", 12 when !wide => "arm", 0x1000007 when wide => "x64", 0x100000c when wide => "arm64",
                _ => throw new FormatException("Unsupported Mach-O processor or word size."),
            };
            uint count = UInt32(header[16..]);
            ReadOnlySpan<byte> commands = Slice((ulong)header.Length, UInt32(header[20..]));
            Require(count <= commands.Length / 8, "Mach-O load-command count exceeds its table.");
            bool found = false;
            int position = 0;
            for (uint index = 0; index < count; index++)
            {
                Require(commands.Length - position >= 8, "Truncated Mach-O load command.");
                ReadOnlySpan<byte> command = commands[position..];
                uint size = UInt32(command[4..]);
                Require(size >= 8 && size <= command.Length && size % (wide ? 8 : 4) == 0, "Invalid Mach-O load-command size.");
                command = command[..(int)size];
                if (UInt32(command) == 2)
                {
                    Require(!found && size == 24, "Invalid or duplicate Mach-O symbol-table command.");
                    found = true;
                    int width = wide ? 16 : 12;
                    ReadOnlySpan<byte> symbols = Table(UInt32(command[8..]), UInt32(command[12..]), width);
                    ReadOnlySpan<byte> strings = Slice(UInt32(command[16..]), UInt32(command[20..]));
                    for (int offset = 0; offset < symbols.Length; offset += width)
                    {
                        ReadOnlySpan<byte> symbol = symbols.Slice(offset, width);
                        ReadOnlySpan<byte> name = Name(strings, UInt32(symbol));
                        ulong value = wide ? UInt64(symbol[8..]) : UInt32(symbol[8..]);
                        if ((symbol[4] & 0xef) == 1 && symbol[5] == 0 && value == 0) { Add(name, decorated: true); }
                    }
                }

                position += (int)size;
            }

            Require(position == commands.Length, "Mach-O load commands do not consume their declared table.");
            return Result("mach-o", architecture);
        }
    }
}

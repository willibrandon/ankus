namespace Ankus.Build;

internal static partial class NativeBindingRecordChecks
{
    private sealed partial class Writer
    {
        /// <summary>
        /// Reads independently prepared native bytes through real fields, including const and volatile members.
        /// </summary>
        private void Bitfields()
        {
            _source.AppendLine("const char *ankus_native_record_check(void)\n{");
            for (int index = 0; index < _graph.Declarations.Count; index++)
            {
                NativeRecordDeclaration declaration = _graph.Declarations[index];
                NativeRecordField[] fields = [.. declaration.Fields.Where(static field => field.BitWidth > 0 && field.Name.Length != 0)];
                if (fields.Length == 0) { continue; }

                string anchor = _anchors[index];
                _source.AppendLine("    {");
                _source.Append("        void *allocation = malloc(sizeof(").Append(anchor).Append(") + _Alignof(").Append(anchor).AppendLine(") - 1);");
                _source.AppendLine("        if (allocation == NULL) return \"Native record verification allocation failed\";");
                _source.Append("        uintptr_t alignment = _Alignof(").Append(anchor).AppendLine(");");
                _source.AppendLine("        unsigned char *bytes = (unsigned char *)(((uintptr_t)allocation + alignment - 1) & ~(alignment - 1));");
                _source.Append("        const ").Append(anchor).Append(" *value = (const ").Append(anchor).AppendLine(" *)bytes;");
                foreach (NativeRecordField field in fields) { Bitfield(index, anchor, field); }

                _source.AppendLine("        free(allocation);\n    }");
            }

            _source.AppendLine("    return NULL;\n}");
        }

        /// <summary>
        /// Walks every expected bit and checks zero/all-ones storage to detect shifts, width changes and signedness.
        /// </summary>
        private void Bitfield(int declaration, string anchor, NativeRecordField field)
        {
            int width = field.BitWidth!.Value;
            NativeRecordType integer = Canonical(field.Type);
            if (integer.Kind == "enum") { integer = Canonical(_graph.Declarations[integer.Declaration!.Value].EnumUnderlying!.Value); }

            bool signed = Signed(integer);
            if (width > 128) { throw new FormatException("Native bitfield verification supports at most 128 value bits."); }

            bool extended = width > 64 || integer.Size > 8;
            string wide = extended ? "unsigned __int128" : "uint64_t";
            string name = _graph.Declarations[declaration].Name;
            string error = "{ free(allocation); return \"Native record bitfield changed: " + (name.Length == 0 ? "declaration " + Number(declaration) : name) + "." + field.Name + "\"; }";
            string actual = "value->" + field.Name;
            string mask = "(~(" + wide + ")0 >> " + Number((extended ? 128 : 64) - width) + ")";
            _source.Append("        memset(bytes, 0, sizeof(").Append(anchor).AppendLine("));");
            _source.Append("        if (").Append(actual).Append(" != 0) ").AppendLine(error);
            _source.Append("        memset(bytes, 255, sizeof(").Append(anchor).AppendLine("));");
            _source.Append("        if ((").Append(wide).Append(')').Append(actual).Append(" != ")
                .Append(signed ? "~(" + wide + ")0" : mask).Append(") ").AppendLine(error);
            _source.Append("        if (((long double)").Append(actual).Append(" < 0.0L) != ").Append(signed ? '1' : '0').Append(") ").AppendLine(error);
            if (!signed)
            {
                // All-ones values of different widths differ by approximately a factor of two, even beyond uint64_t.
                _source.Append("        if ((long double)").Append(actual).Append(" != (long double)").Append(mask).Append(") ").AppendLine(error);
            }

            _source.Append("        for (unsigned int bit = 0; bit < ").Append(Number(width)).AppendLine("; ++bit)\n        {");
            _source.Append("            memset(bytes, 0, sizeof(").Append(anchor).AppendLine("));");
            _source.Append("            size_t physical = ").Append(Number(field.OffsetBits)).AppendLine(" + bit;");
            _source.Append("            bytes[physical / 8] = (unsigned char)(1u << ").Append(_graph.Target.IsLittleEndian ? "(physical % 8)" : "(7 - physical % 8)").AppendLine(");");
            string logical = _graph.Target.IsLittleEndian ? "bit" : Number(width - 1) + " - bit";
            _source.Append("            unsigned int logical = ").Append(logical).AppendLine(";");
            _source.Append("            ").Append(wide).Append(" expected = (").Append(wide).AppendLine(")1 << logical;");
            if (signed)
            {
                _source.Append("            if (logical == ").Append(Number(width - 1)).Append(") expected |= ~").Append(mask).AppendLine(";");
            }

            _source.Append("            if ((").Append(wide).Append(')').Append(actual).Append(" != expected) ").AppendLine(error);
            string negative = signed ? "(logical == " + Number(width - 1) + ")" : "0";
            _source.Append("            if (((long double)").Append(actual).Append(" < 0.0L) != ").Append(negative).Append(") ").AppendLine(error);
            _source.AppendLine("        }");
        }

        /// <summary>
        /// Resolves the measured integer model without relying on host C# integer widths or signedness.
        /// </summary>
        private bool Signed(NativeRecordType integer) => integer.Name switch
        {
            "char" => _graph.Target.Numeric.CharIsSigned,
            "wchar_t" => _graph.Target.Numeric.WCharIsSigned,
            "signed char" or "short" or "int" or "long" or "long long" or "__int128" => true,
            "_Bool" or "bool" or "unsigned char" or "unsigned short" or "unsigned int" or "unsigned long" or "unsigned long long" or "unsigned __int128" => false,
            _ => throw new FormatException("Native representation verification requires an integer type."),
        };
    }
}

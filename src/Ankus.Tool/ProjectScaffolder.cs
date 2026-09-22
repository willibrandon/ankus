using System.Text;
using System.Text.RegularExpressions;

namespace Ankus.Tool;

/// <summary>
/// Creates an independently restorable extension solution from the tool's bundled source templates.
/// </summary>
internal static partial class ProjectScaffolder
{
    private static readonly HashSet<string> s_keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        "__arglist", "__makeref", "__reftype", "__refvalue",
    };

    internal static async Task<string> CreateAsync(string name, string? output, string? extension, CancellationToken token)
    {
        ValidateName(name);
        extension ??= ToExtensionName(name);
        if (extension.Length is < 1 or > 63 || !(char.IsAsciiLetterLower(extension[0]) || extension[0] == '_') ||
            !extension.All(static c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
        {
            throw new ArgumentException("The extension name must be 1–63 lowercase ASCII letters, digits, or underscores, beginning with a letter or underscore.");
        }

        string destination = Path.GetFullPath(output ?? name);
        if (Path.Exists(destination))
        {
            throw new IOException($"The destination already exists: {destination}. Choose a new directory.");
        }

        string templateRoot = Path.Combine(AppContext.BaseDirectory, "Templates", "Extension");
        string version = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ankus-tool.version"), token)).Trim();
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__PROJECT__"] = name,
            ["__NAMESPACE__"] = string.Join('.', name.Split('.').Select(static part => s_keywords.Contains(part) ? "@" + part : part)),
            ["__EXTENSION__"] = extension,
            ["__VERSION__"] = version,
        };
        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".ankus-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (string source in Directory.EnumerateFiles(templateRoot, "*.template", SearchOption.AllDirectories))
            {
                string relative = Replace(Path.GetRelativePath(templateRoot, source)[..^".template".Length], replacements);
                string target = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string text = Replace(await File.ReadAllTextAsync(source, token), replacements);
                await File.WriteAllTextAsync(target, text, new UTF8Encoding(false), token);
            }

            token.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static string Replace(string value, Dictionary<string, string> replacements)
        => TokenPattern().Replace(value, match => replacements[match.Value]);

    [GeneratedRegex("__(PROJECT|NAMESPACE|EXTENSION|VERSION)__", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Split('.').Any(static part =>
                part.Length == 0 || !(char.IsAsciiLetter(part[0]) || part[0] == '_') ||
                !part.All(static c => char.IsAsciiLetterOrDigit(c) || c == '_')))
        {
            throw new ArgumentException("The project name must use C# identifier segments separated by dots, with ASCII letters, digits, and underscores (up to 128 characters).");
        }

        string first = name.Split('.')[0].ToUpperInvariant();
        if (first is "CON" or "PRN" or "AUX" or "NUL" ||
            (first.Length == 4 && (first.StartsWith("COM", StringComparison.Ordinal) || first.StartsWith("LPT", StringComparison.Ordinal)) && first[3] is >= '1' and <= '9'))
        {
            throw new ArgumentException("The project name is reserved on Windows. Choose a portable name.");
        }
    }

    private static string ToExtensionName(string name)
    {
        var result = new StringBuilder();
        for (int index = 0; index < name.Length; index++)
        {
            char c = name[index];
            if (char.IsAsciiLetterUpper(c) && index > 0 && name[index - 1] != '.' && name[index - 1] != '_' &&
                (char.IsAsciiLetterLower(name[index - 1]) || char.IsAsciiDigit(name[index - 1]) ||
                 (index + 1 < name.Length && char.IsAsciiLetterLower(name[index + 1]))))
            {
                result.Append('_');
            }

            result.Append(c == '.' ? '_' : char.ToLowerInvariant(c));
        }

        return result.ToString();
    }
}

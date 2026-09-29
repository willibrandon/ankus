using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ankus.Tool;

internal static partial class FrameworkUpgrade
{
    private static void AddPackageReference(List<UpgradeReference> references, UpgradeDocument document, XElement item,
        XObject node, string package, bool guarded)
    {
        string condition = node is XElement element ? (string?)element.Attribute("Condition") ?? "" : "";
        Match existingGuard = ItemVersionGuard().Match(condition);
        if (existingGuard.Success && !existingGuard.Groups["package"].Value.Equals(package, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? newCondition = null;
        XElement? guardedItem = null;
        if (guarded && !existingGuard.Success)
        {
            newCondition = (condition.Length == 0 ? "" : "(" + condition + ") And ") + "'%(Identity)' == '" + package + "'";
            string metadata = node is XAttribute attribute ? attribute.Name.LocalName : ((XElement)node).Name.LocalName;
            XElement? existing = item.Elements().LastOrDefault(child => child.Name.LocalName == metadata &&
                string.Equals((string?)child.Attribute("Condition"), newCondition, StringComparison.Ordinal));
            if (existing is not null)
            {
                node = existing;
            }
            else
            {
                guardedItem = item;
            }
        }

        if (!references.Any(reference => ReferenceEquals(reference.Node, node) && reference.Package.Equals(package, StringComparison.OrdinalIgnoreCase)))
        {
            string value = node is XAttribute attribute ? attribute.Value : ((XElement)node).Value;
            references.Add(new UpgradeReference(document, node, package, value, false, Item: guardedItem, Condition: newCondition));
        }
    }

    [GeneratedRegex(@"^(?:\((?<condition>[\s\S]*)\) And )?'%\(Identity\)' == '(?<package>[^']+)'$", RegexOptions.CultureInvariant)]
    private static partial Regex ItemVersionGuard();
}

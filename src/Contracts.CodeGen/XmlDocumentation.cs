using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace OpenDispatch.Contracts.CodeGen;

/// <summary>
/// The XML documentation compiled alongside an assembly, read back so the generated
/// TypeScript can carry the same explanations the C# does.
/// </summary>
/// <remarks>
/// <para>
/// The clients see this package and nothing else of ours, so reasoning written down here and
/// nowhere they can reach it is reasoning they do not have. Only <c>summary</c> crosses over:
/// the <c>remarks</c> sections are arguments aimed at whoever maintains the backend.
/// </para>
/// <para>
/// A missing file is an error rather than a shrug. Degrading to comment-free output would make
/// the generated file depend on how the assembly happened to be built, and step 23 diffs that
/// file to detect drift — one machine silently emitting less than another is exactly the false
/// alarm that teaches people to ignore the check.
/// </para>
/// </remarks>
public sealed class XmlDocumentation
{
    private readonly Dictionary<string, XElement> _members;

    private XmlDocumentation(Dictionary<string, XElement> members) => _members = members;

    /// <summary>Loads the documentation file sitting beside <paramref name="assembly"/>.</summary>
    /// <exception cref="FileNotFoundException">
    /// The assembly was built without <c>GenerateDocumentationFile</c>, or its XML was not
    /// copied next to it.
    /// </exception>
    public static XmlDocumentation ForAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var path = Path.ChangeExtension(assembly.Location, ".xml");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No XML documentation beside {Path.GetFileName(assembly.Location)}. The generated "
                    + "TypeScript carries the C# summaries, so generating without them would quietly "
                    + "produce a different file.",
                path);
        }

        var members = XDocument.Load(path)
            .Descendants("member")
            .Where(member => member.Attribute("name") is not null)
            .ToDictionary(member => member.Attribute("name")!.Value, member => member, StringComparer.Ordinal);

        return new XmlDocumentation(members);
    }

    /// <summary>The summary on a type, as one line of plain text.</summary>
    public string? Summary(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Flatten(Element($"T:{FullName(type)}", "summary"));
    }

    /// <summary>The summary on a field — an enum member, or a constant.</summary>
    public string? Summary(FieldInfo field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return Flatten(Element($"F:{FullName(field.DeclaringType!)}.{field.Name}", "summary"));
    }

    /// <summary>
    /// The summary on a property, falling back to the <c>param</c> of the same name on the
    /// declaring type.
    /// </summary>
    /// <remarks>
    /// The fallback is the whole point: the wire types are positional records, and the compiler
    /// files documentation for a positional member under the type's <c>param</c> rather than
    /// under the property it generates.
    /// </remarks>
    public string? Summary(PropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);

        var declaring = FullName(property.DeclaringType!);

        if (Element($"P:{declaring}.{property.Name}", "summary") is { } own)
        {
            return Flatten(own);
        }

        var parameter = _members.TryGetValue($"T:{declaring}", out var type)
            ? type.Elements("param")
                .FirstOrDefault(element =>
                    string.Equals(element.Attribute("name")?.Value, property.Name, StringComparison.Ordinal))
            : null;

        return Flatten(parameter);
    }

    private static string FullName(Type type) => type.FullName ?? type.Name;

    private XElement? Element(string id, string name) =>
        _members.TryGetValue(id, out var member) ? member.Element(name) : null;

    /// <summary>
    /// Turns documentation XML into one line of prose: references become the thing they name,
    /// code spans become backticks, and the newlines and indentation the compiler preserved
    /// are collapsed away.
    /// </summary>
    private static string? Flatten(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var text = new StringBuilder();

        foreach (var node in element.Nodes())
        {
            Append(text, node);
        }

        var words = text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return words.Length == 0 ? null : string.Join(' ', words);
    }

    private static void Append(StringBuilder text, XNode node)
    {
        switch (node)
        {
            case XText content:
                text.Append(content.Value);
                break;

            case XElement { Name.LocalName: "see" or "seealso" } reference:
                text.Append(Reference(reference));
                break;

            case XElement { Name.LocalName: "paramref" or "typeparamref" } reference:
                text.Append(Quote(Camel(reference.Attribute("name")?.Value)));
                break;

            case XElement { Name.LocalName: "c" or "code" } span:
                text.Append(Quote(span.Value));
                break;

            case XElement element:
                foreach (var child in element.Nodes())
                {
                    Append(text, child);
                }

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// What a <c>see</c> should read as on the other side. A property is a field on the
    /// generated interface, so it reads as that field; a constant or an enum member keeps its
    /// container, because <c>JobStatus.EnRoute</c> says more than <c>EnRoute</c>.
    /// </summary>
    private static string Reference(XElement element)
    {
        if (element.Attribute("langword")?.Value is { Length: > 0 } keyword)
        {
            return Quote(keyword);
        }

        if (element.Attribute("cref")?.Value is not { Length: > 2 } cref)
        {
            return string.Empty;
        }

        var kind = cref[0];
        var segments = cref[(cref.IndexOf(':', StringComparison.Ordinal) + 1)..].Split('.');

        return kind switch
        {
            'T' => Quote(segments[^1]),
            'P' => Quote(Camel(segments[^1])),
            _ => Quote(string.Join('.', segments.TakeLast(2))),
        };
    }

    private static string Quote(string value) => $"`{value}`";

    private static string Camel(string? name) =>
        string.IsNullOrEmpty(name)
            ? string.Empty
            : $"{char.ToLower(name[0], CultureInfo.InvariantCulture)}{name[1..]}";
}

using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDispatch.Contracts.CodeGen;

/// <summary>
/// Turns the <c>Contracts</c> assembly into the TypeScript half of
/// <c>@opendispatch/contracts</c> — the shapes no OpenAPI document can describe.
/// </summary>
/// <remarks>
/// <para>
/// Three rules, applied mechanically rather than type by type, so a new wire type needs no
/// change here: an enum becomes a frozen object plus a union of its values, a static class of
/// constants becomes a frozen object, and everything else becomes an interface.
/// </para>
/// <para>
/// It refuses rather than guesses. A property whose type has no mapping, or an enum that is
/// not encoded as strings, stops generation with a message naming the offender — because the
/// alternative is emitting <c>any</c> or a numeric enum, which compiles on both sides and is
/// wrong only at runtime, in a client repository, against a server that thinks it agreed.
/// </para>
/// <para>
/// The output is a pure function of the assembly and its XML documentation: types sorted by
/// namespace then name, members in declaration order, no timestamps, no machine names, and
/// <c>\n</c> line endings everywhere. Step 23 regenerates this file and diffs it, so anything
/// that varies between two runs is a false alarm waiting to happen.
/// </para>
/// </remarks>
public sealed class TypeScriptEmitter
{
    private static readonly Dictionary<Type, string> Primitives = new()
    {
        [typeof(string)] = "string",
        [typeof(Guid)] = "string",
        [typeof(DateTimeOffset)] = "string",
        [typeof(DateTime)] = "string",
        [typeof(TimeSpan)] = "string",
        [typeof(bool)] = "boolean",
        [typeof(byte)] = "number",
        [typeof(short)] = "number",
        [typeof(int)] = "number",
        [typeof(long)] = "number",
        [typeof(float)] = "number",
        [typeof(double)] = "number",
        [typeof(decimal)] = "number",
        [typeof(JsonElement)] = "unknown",
    };

    private static readonly HashSet<Type> Sequences =
    [
        typeof(IReadOnlyList<>),
        typeof(IReadOnlyCollection<>),
        typeof(IEnumerable<>),
        typeof(IList<>),
        typeof(ICollection<>),
        typeof(List<>),
    ];

    private readonly HashSet<Type> _declared;
    private readonly XmlDocumentation _documentation;
    private readonly NullabilityInfoContext _nullability = new();
    private readonly StringBuilder _text = new();

    private TypeScriptEmitter(IEnumerable<Type> declared, XmlDocumentation documentation)
    {
        _declared = [.. declared];
        _documentation = documentation;
    }

    /// <summary>Generates the TypeScript source for every public type in <paramref name="contracts"/>.</summary>
    /// <exception cref="NotSupportedException">
    /// A type or member has no defensible TypeScript equivalent, which is a decision for a
    /// person rather than a default for a generator.
    /// </exception>
    public static string Emit(Assembly contracts, XmlDocumentation documentation)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        return Emit(contracts.GetExportedTypes(), documentation);
    }

    /// <summary>Generates the TypeScript source for exactly <paramref name="types"/>.</summary>
    /// <remarks>
    /// The set is what a property may refer to. A type outside it has no name on the other
    /// side, so referring to one is an error rather than a dangling identifier in a file that
    /// looks generated correctly.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// A type or member has no defensible TypeScript equivalent, which is a decision for a
    /// person rather than a default for a generator.
    /// </exception>
    public static string Emit(IReadOnlyCollection<Type> types, XmlDocumentation documentation)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(documentation);

        return new TypeScriptEmitter(types, documentation).Write(types);
    }

    private string Write(IReadOnlyCollection<Type> types)
    {
        Header();

        var namespaces = types
            .GroupBy(type => type.Namespace ?? string.Empty)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in namespaces)
        {
            Banner(group.Key);

            foreach (var type in group.OrderBy(type => type.Name, StringComparer.Ordinal))
            {
                Declaration(type);
            }
        }

        return $"{_text.ToString().TrimEnd('\n')}\n";
    }

    private void Header() => _text.Append(
        """
        /**
         * @opendispatch/contracts — the wire shapes an OpenAPI document cannot describe:
         * SignalR board events, the offline-sync payloads, and the enums both halves share.
         *
         * Generated from the backend's `src/Contracts` by `make gen-contracts`. Do not edit —
         * change the C# and regenerate, which is what keeps a backend change a client compile
         * error rather than a runtime surprise.
         *
         * A C# `long` arrives as `number`: version stamps and cursors stay far inside the range
         * a double holds exactly. A `Guid` and a `DateTimeOffset` both arrive as `string`, the
         * latter in ISO 8601 with its offset.
         */


        """.ReplaceLineEndings("\n"));

    private void Banner(string name) => _text.Append(TypeScriptText.Banner(name));

    private void Declaration(Type type)
    {
        if (type.IsEnum)
        {
            Enumeration(type);
        }
        else if (type is { IsAbstract: true, IsSealed: true })
        {
            Constants(type);
        }
        else
        {
            Interface(type);
        }

        _text.Append('\n');
    }

    /// <summary>
    /// An enum becomes a frozen object and a union of its values rather than a TypeScript
    /// <c>enum</c>: a real <c>enum</c> is a runtime construct that type-stripping toolchains
    /// reject outright, and this pattern gives a client both the values to compare against and
    /// a type to annotate with.
    /// </summary>
    private void Enumeration(Type type)
    {
        RequireStringEncoding(type);

        Documentation(_documentation.Summary(type), string.Empty);
        _text.Append("export const ").Append(type.Name).Append(" = {\n");

        foreach (var member in Literals(type))
        {
            Documentation(_documentation.Summary(member), "  ");
            _text.Append("  ").Append(member.Name).Append(": '").Append(member.Name).Append("',\n");
        }

        _text.Append("} as const;\n\n")
            .Append("export type ").Append(type.Name)
            .Append(" = (typeof ").Append(type.Name).Append(")[keyof typeof ").Append(type.Name).Append("];\n");
    }

    /// <summary>A static class of constants becomes the same frozen object, minus the union.</summary>
    private void Constants(Type type)
    {
        Documentation(_documentation.Summary(type), string.Empty);
        _text.Append("export const ").Append(type.Name).Append(" = {\n");

        foreach (var constant in Literals(type))
        {
            var value = constant.GetRawConstantValue() as string
                ?? throw new NotSupportedException(
                    $"{type.Name}.{constant.Name} is not a string constant, and a generator that "
                        + "guessed at what else a shared constant might be would be guessing about "
                        + "the wire.");

            Documentation(_documentation.Summary(constant), "  ");
            _text.Append("  ").Append(constant.Name).Append(": '").Append(value).Append("',\n");
        }

        _text.Append("} as const;\n");
    }

    private void Interface(Type type)
    {
        Documentation(_documentation.Summary(type), string.Empty);
        _text.Append("export interface ").Append(type.Name).Append(" {\n");

        foreach (var property in InDeclarationOrder(type.GetProperties(BindingFlags.Public | BindingFlags.Instance)))
        {
            Documentation(_documentation.Summary(property), "  ");
            _text.Append("  readonly ")
                .Append(JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                .Append(": ")
                .Append(TypeOf(property))
                .Append(";\n");
        }

        _text.Append("}\n");
    }

    /// <summary>
    /// The constants a type declares, in the order they were written — enum members, or the
    /// strings of a static class. Anything that is not a compile-time constant is not part of
    /// the wire vocabulary.
    /// </summary>
    private static IEnumerable<FieldInfo> Literals(Type type) =>
        InDeclarationOrder(type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.IsLiteral));

    /// <summary>
    /// Reflection makes no promise about the order it hands members back, and a file whose
    /// fields shuffle between runs cannot be diffed for drift.
    /// </summary>
    private static IEnumerable<T> InDeclarationOrder<T>(IEnumerable<T> members)
        where T : MemberInfo =>
        members.OrderBy(member => member.MetadataToken);

    private string TypeOf(PropertyInfo property)
    {
        var info = _nullability.Create(property);

        return Map(info, property);
    }

    private string Map(NullabilityInfo info, PropertyInfo property)
    {
        var mapped = MapCore(info, property);

        // `unknown` already admits null — it is the top type — so unioning the two says nothing
        // and reads as a mistake. Whether an opaque payload may be absent is a fact the
        // documentation carries; the type system has nowhere to put it.
        if (mapped == "unknown" || info.ReadState != NullabilityState.Nullable)
        {
            return mapped;
        }

        return $"{mapped} | null";
    }

    private string MapCore(NullabilityInfo info, PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(info.Type) ?? info.Type;

        if (type.IsEnum)
        {
            return type.Name;
        }

        if (Primitives.TryGetValue(type, out var primitive))
        {
            return primitive;
        }

        // Another shape in this same file, so it is referred to by name — no import to
        // resolve, no declaration order to get right.
        if (_declared.Contains(type))
        {
            return type.Name;
        }

        if (info.ElementType is { } element)
        {
            return $"readonly {Map(element, property)}[]";
        }

        if (type.IsGenericType
            && Sequences.Contains(type.GetGenericTypeDefinition())
            && info.GenericTypeArguments is [var item])
        {
            return $"readonly {Map(item, property)}[]";
        }

        throw new NotSupportedException(
            $"{property.DeclaringType?.Name}.{property.Name} is a {type.Name}, which has no "
                + "TypeScript equivalent here. Add one deliberately rather than letting the "
                + "clients receive `any`.");
    }

    /// <summary>
    /// An enum reaches the clients as the name of its member, which is only true while it
    /// carries a converter that says so. Without this check, removing the attribute would put
    /// numbers on the wire and leave the generated union comparing against strings — a break
    /// that compiles on both sides.
    /// </summary>
    private static void RequireStringEncoding(Type type)
    {
        var converter = type.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType;

        var strings = converter == typeof(JsonStringEnumConverter)
            || (converter is { IsGenericType: true }
                && converter.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>));

        if (!strings)
        {
            throw new NotSupportedException(
                $"{type.Name} has no JsonStringEnumConverter, so it goes on the wire as numbers "
                    + "while this generator would emit strings. Add the converter, or decide "
                    + "deliberately that the clients should match on integers.");
        }
    }

    private void Documentation(string? summary, string indent) =>
        _text.Append(TypeScriptText.Documentation(summary, indent));
}

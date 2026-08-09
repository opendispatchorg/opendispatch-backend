using System.Text;

namespace OpenDispatch.Contracts.CodeGen;

/// <summary>
/// The shared look of the generated file: how wide it is, how a section is announced, and how
/// a documentation comment wraps.
/// </summary>
/// <remarks>
/// Here rather than inside one emitter because two of them write into the same file — the wire
/// shapes and the job transition table — and a file whose two halves disagree about margins
/// reads like two files that were stapled together.
/// </remarks>
internal static class TypeScriptText
{
    /// <summary>Where the file wraps. Wide enough for prose, narrow enough to read.</summary>
    public const int Columns = 96;

    /// <summary>A ruled heading announcing where the following declarations came from.</summary>
    public static string Banner(string name)
    {
        var rule = new string('-', Columns - 3);

        return $"// {rule}\n// {name}\n// {rule}\n\n";
    }

    /// <summary>
    /// One summary as a TSDoc comment, on a single line where it fits and wrapped where it does
    /// not. Empty when there is nothing to say, so a caller can concatenate it unconditionally.
    /// </summary>
    public static string Documentation(string? summary, string indent = "")
    {
        if (summary is null)
        {
            return string.Empty;
        }

        var lines = Wrap(summary, Columns - indent.Length - 3);

        if (lines.Count == 1)
        {
            return $"{indent}/** {lines[0]} */\n";
        }

        var text = new StringBuilder($"{indent}/**\n");

        foreach (var line in lines)
        {
            text.Append(indent).Append(" * ").Append(line).Append('\n');
        }

        return text.Append(indent).Append(" */\n").ToString();
    }

    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        return lines;
    }
}

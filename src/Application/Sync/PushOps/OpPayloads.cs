using System.Text.Json;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Sync.PushOps;

/// <summary>
/// Reads an operation's payload, which arrived as whatever a phone put in it.
/// </summary>
/// <remarks>
/// <para>
/// Everything here answers "can this be read at all" and nothing answers "is this allowed". A
/// quantity of minus three parses perfectly and is refused by the domain, which is where that rule
/// lives; a quantity of <c>"two"</c> never becomes a number and is refused here. Keeping the line
/// there means these readers cannot drift from the guards on <c>JobLine</c>, because they do not
/// restate them.
/// </para>
/// <para>
/// Missing and wrong-typed are the same answer on purpose. A device that sends
/// <c>{"quantity": "2"}</c> and one that sends no quantity at all have both failed to say how
/// many, and a technician reading the conflict is no better off for knowing which.
/// </para>
/// </remarks>
internal static class OpPayloads
{
    /// <summary>Reads a status change, or explains what it could not read.</summary>
    internal static Read<StatusChange> ReadStatusChange(JsonElement payload)
    {
        if (!TryReadString(payload, "status", out var name)
            || !Enum.TryParse<JobStatus>(name, ignoreCase: true, out var status)
            || !Enum.IsDefined(status))
        {
            return Read<StatusChange>.Failed("it does not name a status a job can be in.");
        }

        // Optional, and only meaningful for a completion. Present-but-unreadable is still an
        // error: a device that meant to say when the work finished must not have it ignored.
        DateTimeOffset? completedAt = null;

        if (payload.TryGetProperty("completedAt", out var when) && when.ValueKind is not JsonValueKind.Null)
        {
            if (!when.TryGetDateTimeOffset(out var finished))
            {
                return Read<StatusChange>.Failed("its completion time is not an instant.");
            }

            completedAt = finished;
        }

        return Read<StatusChange>.Ok(new StatusChange(status, completedAt));
    }

    /// <summary>Reads a note, or explains what it could not read.</summary>
    internal static Read<string> ReadNote(JsonElement payload) =>
        TryReadString(payload, "text", out var text)
            ? Read<string>.Ok(text)
            : Read<string>.Failed("it does not carry any text.");

    /// <summary>Reads a recorded line, or explains what it could not read.</summary>
    internal static Read<RecordedLine> ReadLine(JsonElement payload)
    {
        if (!TryReadString(payload, "kind", out var kindName)
            || !Enum.TryParse<LineItemKind>(kindName, ignoreCase: true, out var kind)
            || !Enum.IsDefined(kind))
        {
            return Read<RecordedLine>.Failed("it does not say whether it is labour or a part.");
        }

        if (!TryReadString(payload, "description", out var description))
        {
            return Read<RecordedLine>.Failed("it does not say what it is for.");
        }

        if (!TryReadQuantity(payload, out var quantity))
        {
            return Read<RecordedLine>.Failed("it does not say how many.");
        }

        if (!TryReadCents(payload, out var cents))
        {
            return Read<RecordedLine>.Failed("it does not say what one costs, in cents.");
        }

        return Read<RecordedLine>.Ok(new RecordedLine(kind, description, quantity, new Money(cents)));
    }

    private static bool TryReadString(JsonElement payload, string property, out string value)
    {
        value = string.Empty;

        if (payload.ValueKind is not JsonValueKind.Object
            || !payload.TryGetProperty(property, out var element)
            || element.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;

        return value.Length > 0;
    }

    private static bool TryReadQuantity(JsonElement payload, out decimal value)
    {
        value = 0m;

        return TryReadNumber(payload, "quantity", out var element) && element.TryGetDecimal(out value);
    }

    private static bool TryReadCents(JsonElement payload, out long value)
    {
        value = 0L;

        return TryReadNumber(payload, "unitPriceCents", out var element) && element.TryGetInt64(out value);
    }

    private static bool TryReadNumber(JsonElement payload, string property, out JsonElement element)
    {
        element = default;

        return payload.ValueKind is JsonValueKind.Object
            && payload.TryGetProperty(property, out element)
            && element.ValueKind is JsonValueKind.Number;
    }
}

/// <summary>
/// What reading a payload produced: the value, or the sentence explaining why there is not one.
/// </summary>
/// <typeparam name="TValue">What the payload should have contained.</typeparam>
/// <remarks>
/// Not a <c>Result</c>, deliberately. A <c>Result</c> carries an error code and a category that
/// belong to the application's failure vocabulary, and "the fourth operation in a batch had no
/// quantity in it" is not one of those — it is half a sentence the handler puts inside the one
/// conflict it does report.
/// </remarks>
internal readonly record struct Read<TValue>(TValue? Value, string? Problem)
{
    /// <summary>Whether the payload could be read.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A payload that said what it needed to.</summary>
    public static Read<TValue> Ok(TValue value) => new(value, null);

    /// <summary>A payload that did not, and why.</summary>
    public static Read<TValue> Failed(string problem) => new(default, problem);
}

/// <summary>A status change, read from a payload.</summary>
/// <param name="Status">Where the device says the job should be.</param>
/// <param name="CompletedAt">When the work finished, if it said.</param>
internal sealed record StatusChange(JobStatus Status, DateTimeOffset? CompletedAt);

/// <summary>A line of work, read from a payload.</summary>
/// <param name="Kind">Labour or a part.</param>
/// <param name="Description">What it was.</param>
/// <param name="Quantity">How many.</param>
/// <param name="UnitPrice">What one costs.</param>
internal sealed record RecordedLine(LineItemKind Kind, string Description, decimal Quantity, Money UnitPrice);

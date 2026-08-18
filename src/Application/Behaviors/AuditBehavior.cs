using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auditing;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Behaviors;

/// <summary>
/// Writes down who did what, for every command that succeeded.
/// </summary>
/// <typeparam name="TRequest">The command. Queries do not reach this behavior.</typeparam>
/// <typeparam name="TResponse">Its result type.</typeparam>
/// <remarks>
/// <para>
/// A behavior rather than a line in each handler, because "every write is attributable" is exactly
/// the kind of rule that a handler eventually forgets — and the forgetting is invisible. Every write
/// in this system is a command through this pipeline, so this is the one place that can say so for
/// all of them, including the ones nobody has written yet.
/// </para>
/// <para>
/// <strong>Innermost, below the transaction, and that is the whole point.</strong> The entry is
/// staged after the handler succeeds and is written by the same save and the same commit as the work
/// it describes: an audit row cannot survive a command that rolled back, and committed work cannot
/// exist without its row. Anywhere above <c>TransactionBehavior</c> would make them two writes that
/// can disagree.
/// </para>
/// <para>
/// <strong>Failures are not recorded here.</strong> A refused command changed nothing, and a trail
/// that logged attempts would fill with validation noise; what a refusal produces is a log line and
/// a metric, which is the <c>LoggingBehavior</c>'s business. The one thing worth knowing that this
/// cannot see — repeated failed sign-ins — is not a command at all.
/// </para>
/// <para>
/// <strong>It records identities, never values.</strong> See <see cref="AuditEntry"/>: a trail
/// holding names and phone numbers would be a second copy of the personal data an erasure has to
/// remove.
/// </para>
/// </remarks>
internal sealed class AuditBehavior<TRequest, TResponse>(
    IAuditLog audit,
    ITenantContext tenant,
    ICallerContext caller,
    IClock clock)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase
    where TResponse : Result
{
    /// <summary>The identity-bearing properties of each command type met so far.</summary>
    /// <remarks>
    /// Reflection once per type rather than per request. What counts as an identity is a struct
    /// with a single <c>Value</c> of type <see cref="Guid"/> — which is every strongly-typed id in
    /// this system and nothing else, because that is what those types are.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Identities = new();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response;
        }

        audit.Record(AuditEntry.For(
            tenant.OrgId,
            caller.UserId,
            caller.Username,
            Named(typeof(TRequest)),
            Targets(request),
            clock.UtcNow));

        return response;
    }

    /// <summary>
    /// What to call this act: the command's type name without its suffix — <c>CancelJob</c> rather
    /// than <c>CancelJobCommand</c>, because the suffix is a C# convention rather than a fact about
    /// the business.
    /// </summary>
    private static string Named(Type request) =>
        request.Name.EndsWith("Command", StringComparison.Ordinal)
            ? request.Name[..^"Command".Length]
            : request.Name;

    /// <summary>The identities the command named, as JSON.</summary>
    private static string Targets(TRequest request)
    {
        var properties = Identities.GetOrAdd(typeof(TRequest), static type =>
            [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(IsIdentity)]);

        var targets = new Dictionary<string, Guid>(properties.Length, StringComparer.Ordinal);

        foreach (var property in properties)
        {
            if (property.GetValue(request) is { } id && Value(id) is { } value)
            {
                targets[property.Name] = value;
            }
        }

        return JsonSerializer.Serialize(targets);
    }

    private static bool IsIdentity(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        return type.IsValueType && type.GetProperty("Value")?.PropertyType == typeof(Guid);
    }

    private static Guid? Value(object id) => id.GetType().GetProperty("Value")?.GetValue(id) as Guid?;
}

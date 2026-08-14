using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Behaviors;
using OpenDispatch.Application.Observability;

namespace OpenDispatch.Application;

/// <summary>
/// Registration for the application layer: the mediator every feature slice is dispatched
/// through, the validators that guard them, and the pipeline they all ride on.
/// </summary>
/// <remarks>
/// The composition root is the Api (Document 2 §2) and it calls this, exactly as it calls
/// <c>AddPersistence</c> — but the order the behaviors run in is stated here, next to the
/// behaviors it orders. Splitting the two would mean a reader of either half could not tell
/// what the pipeline does, and a fourth behavior added to this project could be registered in
/// the wrong place from another one.
/// </remarks>
public static class ApplicationRegistration
{
    /// <summary>
    /// Registers MediatR over this assembly, the FluentValidation validators in it, and the
    /// three pipeline behaviors in the order they wrap a handler.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(mediator =>
        {
            mediator.RegisterServicesFromAssemblyContaining(typeof(ApplicationRegistration));

            // Outermost first. The order is a set of decisions, not a preference:
            //
            //   Logging      sees everything, including the failures the behaviors below
            //                produce and the time their work costs. Nothing it wraps can hide
            //                from it.
            //   Validation   is next so a malformed request is refused before anything else
            //                happens — in particular before a transaction is opened for work
            //                that was never going to be kept.
            //   Concurrency  is next, and its position is forced: the lost race it reports is
            //                thrown by the save inside Transaction below, and that transaction
            //                rolls back as the exception passes out through it — so this is the
            //                first place the failure can be turned into a Result with nothing
            //                half-written behind it.
            //   Transaction  is innermost, so the transaction spans the handler and nothing
            //                else. Validators do not run inside it, and neither does logging.
            //
            // A behavior added later belongs above Transaction unless it genuinely needs to be
            // inside the transaction, in which case it belongs to the handler instead.
            mediator.AddOpenBehavior(typeof(LoggingBehavior<,>));
            mediator.AddOpenBehavior(typeof(ValidationBehavior<,>));
            mediator.AddOpenBehavior(typeof(ConcurrencyBehavior<,>));
            mediator.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        // Internal types included: a validator is an implementation detail of its slice and has
        // no business being public, and a validator that silently is not registered is a rule
        // that silently does not apply.
        services.AddValidatorsFromAssemblyContaining(
            typeof(ApplicationRegistration),
            includeInternalTypes: true);

        // The instruments the handlers record on (Document 3, step 54). AddMetrics is called here
        // rather than left to the host: a web host registers IMeterFactory for itself, but the
        // integration harness composes this pipeline over a bare ServiceCollection, and a handler
        // that cannot be resolved outside a web host is one whose measurements only exist in
        // production. Both calls are idempotent, so a host that also asks for metrics gets one
        // factory and one set of instruments.
        //
        // Singletons: an instrument is created once and recorded on from every request.
        services.AddMetrics();
        services.AddSingleton<SchedulingMetrics>();
        services.AddSingleton<SyncMetrics>();

        return services;
    }
}

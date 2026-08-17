using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Behaviors;

/// <summary>
/// Records that a request ran, how long it took, and how it ended.
/// </summary>
/// <typeparam name="TRequest">The command or query.</typeparam>
/// <typeparam name="TResponse">Its result type.</typeparam>
/// <remarks>
/// <para>
/// Outermost of the three, so it sees everything the pipeline does — including a request
/// validation refused, which is a failure a handler never hears about and therefore one no
/// handler could log. Its timing covers the whole pipeline for the same reason: what a caller
/// waited for includes the validators and the commit.
/// </para>
/// <para>
/// It logs the request's <em>type</em> and never its contents. A command carries customer
/// names, addresses and phone numbers, and a log is the easiest place in a system to leak them
/// from. The identifiers a support question actually needs come from the correlation id on the
/// surrounding request scope (step 54).
/// </para>
/// <para>
/// An exception is logged and rethrown, never handled. Turning a bug into a value here would
/// hide it from the transaction behavior below, which needs it to roll back, and from step 46's
/// handler, which owns what a client is told about it.
/// </para>
/// </remarks>
internal sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();

        PipelineLog.Handling(logger, name);

        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (response.Error is { } error)
            {
                PipelineLog.Failed(logger, name, elapsed, error.Code, error.Category, error.Message);
            }
            else
            {
                PipelineLog.Succeeded(logger, name, elapsed);
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away, or the host is shutting down. Neither is this command failing,
            // and a stack trace at error level for every phone that drives into a tunnel is how an
            // error log becomes something nobody reads. The edge answers 499; this says how far it
            // had got, which is the part worth knowing.
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            PipelineLog.Cancelled(logger, name, elapsed);

            throw;
        }
        catch (Exception exception)
        {
            PipelineLog.Threw(logger, exception, name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}

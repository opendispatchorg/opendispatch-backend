using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Behaviors;

/// <summary>
/// The four things <see cref="LoggingBehavior{TRequest, TResponse}"/> has to say, as
/// source-generated log methods.
/// </summary>
/// <remarks>
/// Separate from the behavior because the behavior is generic and these are not: one generated
/// method serves every closed pair rather than one set per request type. Generated rather than
/// hand-written <c>logger.LogInformation(...)</c> calls so the message template is checked at
/// compile time, the level is tested before any argument is touched, and nothing boxes on a
/// path that runs for every request in the system.
/// </remarks>
internal static partial class PipelineLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {Request}")]
    internal static partial void Handling(ILogger logger, string request);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Request} succeeded in {ElapsedMs}ms")]
    internal static partial void Succeeded(ILogger logger, string request, double elapsedMs);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Request} failed after {ElapsedMs}ms with {ErrorCode} ({ErrorCategory}): {ErrorMessage}")]
    internal static partial void Failed(
        ILogger logger,
        string request,
        double elapsedMs,
        string errorCode,
        ErrorCategory errorCategory,
        string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Request} threw after {ElapsedMs}ms")]
    internal static partial void Threw(ILogger logger, Exception exception, string request, double elapsedMs);

    /// <remarks>
    /// Information, and no exception object: a caller that hung up is not a fault. What is worth
    /// keeping is how long the work had been running when they gave up, which is the number that
    /// says whether they were impatient or whether this path got slow.
    /// </remarks>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Request} was cancelled after {ElapsedMs}ms")]
    internal static partial void Cancelled(ILogger logger, string request, double elapsedMs);
}

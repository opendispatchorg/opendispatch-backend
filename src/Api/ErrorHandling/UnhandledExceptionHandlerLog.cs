namespace OpenDispatch.Api.ErrorHandling;

/// <summary>The one thing <see cref="UnhandledExceptionHandler"/> has to say, source-generated.</summary>
/// <remarks>
/// Generated rather than a hand-written <c>logger.LogError(...)</c> call, the same reason
/// <c>Application.Behaviors.PipelineLog</c> is: the message template is checked at compile time
/// and nothing boxes on a path that — unlike that one — is meant to run rarely, but should cost
/// nothing to have wired up when it does not.
/// </remarks>
internal static partial class UnhandledExceptionHandlerLog
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unhandled exception on {Method} {Path} (correlation {CorrelationId})")]
    internal static partial void Unhandled(
        ILogger logger, Exception exception, string method, string path, string correlationId);

    /// <summary>
    /// A request this server could not read. Warning rather than error, and without the exception
    /// object: nothing here is this server's fault, nothing is actionable in a stack trace through
    /// framework parameter binding, and a caller looping on a malformed request must not be able to
    /// fill an error log with dumps.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Malformed request on {Method} {Path} answered {StatusCode}: {Reason} "
            + "(correlation {CorrelationId})")]
    internal static partial void Malformed(
        ILogger logger, string method, string path, int statusCode, string reason, string correlationId);
}

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
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Method} {Path}")]
    internal static partial void Unhandled(ILogger logger, Exception exception, string method, string path);
}

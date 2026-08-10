using Microsoft.Extensions.Logging;

namespace OpenDispatch.Application.Tests.Pipeline;

/// <summary>One line the pipeline logged.</summary>
/// <param name="Level">How severe it said it was.</param>
/// <param name="Message">The rendered message.</param>
/// <param name="Exception">The exception it carried, if any.</param>
internal sealed record RecordedLog(LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// Keeps every log line so a test can ask what the pipeline saw.
/// </summary>
/// <remarks>
/// Used for one thing: proving that a failure produced by a behavior is still logged, which can
/// only be true if logging wraps that behavior. The wording of the lines is not under test —
/// asserting on log text is how you get a suite that breaks when someone improves a message.
/// </remarks>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<RecordedLog> _entries = [];

    /// <summary>What was logged, oldest first.</summary>
    public IReadOnlyList<RecordedLog> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Recorder(_entries);

    public void Dispose()
    {
        // Nothing to release: the entries outlive the provider so a test can read them.
    }

    private sealed class Recorder(List<RecordedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Add(new RecordedLog(logLevel, formatter(state, exception), exception));
    }
}

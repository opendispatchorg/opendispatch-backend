using Microsoft.Extensions.Logging;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// Counts the commands EF sends to the database.
/// </summary>
/// <remarks>
/// <para>
/// A read model's whole justification is that it asks one question instead of loading a graph of
/// aggregates and throwing most of it away, and the failure mode is a query per row rather than a
/// wrong answer — nothing about the result would look different. So the only way to hold the claim
/// is to count round trips and require that the number does not grow with the data.
/// </para>
/// <para>
/// EF logs every command it executes under a category of its own, which is cheaper to hook than an
/// interceptor and does not change how the context is built.
/// </para>
/// </remarks>
internal sealed class RoundTripCounter : ILoggerProvider
{
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    private int _executed;

    /// <summary>How many commands have been sent since the counter was created.</summary>
    public int Executed => Volatile.Read(ref _executed);

    public ILogger CreateLogger(string categoryName) =>
        categoryName == CommandCategory ? new Counting(this) : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class Counting(RoundTripCounter counter) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // The one event that means "a command went to the server". Connections opening and
            // closing are logged in the same category and are not round trips of their own.
            if (eventId.Id == 20101)
            {
                Interlocked.Increment(ref counter._executed);
            }
        }
    }

    private sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}

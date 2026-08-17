using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.ErrorHandling;

/// <summary>
/// A caller who hangs up is not a server error.
/// </summary>
/// <remarks>
/// <para>
/// A dispatcher closing a tab, a phone driving into a tunnel mid-sync, a browser cancelling a slow
/// export and asking again: all of them abort a request, all of them raise
/// <see cref="OperationCanceledException"/> out of whatever was running, and all of them used to be
/// logged as unhandled exceptions and answered with a 500 written into a socket nobody was holding.
/// A fleet of vans produces those all day, and an error log full of them is an error log nobody
/// reads.
/// </para>
/// <para>
/// Tested against the handler rather than through a request, because the case is a request with no
/// caller left on it — proving it over HTTP would mean proving the test harness can hang up at
/// exactly the right moment, which tests the harness.
/// </para>
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class DisconnectTests
{
    [Fact]
    public async Task AnAbortedRequestIsNotAnError()
    {
        var context = new DefaultHttpContext();
        var abort = new CancellationTokenSource();
        await abort.CancelAsync();
        context.Features.Set<IHttpRequestLifetimeFeature>(new Aborted(abort.Token));

        var logs = new Recording();
        var handled = await Handler(logs).TryHandleAsync(
            context, new OperationCanceledException(abort.Token), CancellationToken.None);

        Assert.True(handled);

        // 499, nginx's "client closed request": for the log and for a proxy in front, since there
        // is nothing left on the wire to answer.
        Assert.Equal(499, context.Response.StatusCode);

        // And nothing at error level. That is the whole point of the change.
        Assert.DoesNotContain(logs.Levels, level => level >= LogLevel.Warning);
        Assert.Contains(LogLevel.Information, logs.Levels);
    }

    /// <summary>
    /// The other half, which must keep working: a genuine failure during a request that also
    /// happens to have been aborted is still a failure.
    /// </summary>
    [Fact]
    public async Task AFailureIsStillAFailureEvenIfTheCallerHasGone()
    {
        var context = new DefaultHttpContext();
        var abort = new CancellationTokenSource();
        await abort.CancelAsync();
        context.Features.Set<IHttpRequestLifetimeFeature>(new Aborted(abort.Token));

        var logs = new Recording();
        await Handler(logs).TryHandleAsync(
            context, new InvalidOperationException("the database fell over"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains(LogLevel.Error, logs.Levels);
    }

    /// <summary>
    /// And the trap in the other direction: a handler that cancelled its own work, on a request
    /// nobody hung up on, is a bug rather than a disconnect.
    /// </summary>
    [Fact]
    public async Task CancellationOnARequestNobodyAbortedIsStillAnError()
    {
        var context = new DefaultHttpContext();
        var logs = new Recording();

        await Handler(logs).TryHandleAsync(
            context, new OperationCanceledException(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains(LogLevel.Error, logs.Levels);
    }

    private static UnhandledExceptionHandler Handler(Recording logs) => new(
        new ServiceCollection().AddLogging().AddOptions().AddProblemDetails().BuildServiceProvider()
            .GetRequiredService<IProblemDetailsService>(),
        new Logger(logs));

    /// <summary>A request lifetime whose token is already cancelled, as an aborted one is.</summary>
    private sealed class Aborted(CancellationToken token) : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; } = token;

        public void Abort()
        {
        }
    }

    /// <summary>What was logged, by level — which is the fact these tests are about.</summary>
    private sealed class Recording
    {
        public List<LogLevel> Levels { get; } = [];
    }

    private sealed class Logger(Recording recording) : ILogger<UnhandledExceptionHandler>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => recording.Levels.Add(logLevel);
    }
}

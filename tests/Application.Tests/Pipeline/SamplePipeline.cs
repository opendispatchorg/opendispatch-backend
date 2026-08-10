using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Tests.Pipeline;

/// <summary>
/// The real pipeline, over a sample handler and a unit of work that writes nothing.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddApplication</c> is called rather than reproduced, so these tests fail if the behaviors
/// are registered in the wrong order — which is the decision this step makes and the one a
/// behavior tested on its own could never contradict. Only the sample request family and the
/// fakes are added on top.
/// </para>
/// <para>
/// Scopes are validated, so a behavior that took a singleton dependency on something scoped
/// would fail here rather than in production at the second request.
/// </para>
/// </remarks>
internal sealed class SamplePipeline : IAsyncDisposable
{
    private readonly PipelineJournal _journal = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;

    public SamplePipeline()
    {
        _services = new ServiceCollection()
            .AddSingleton(_journal)
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(_logs))
            .AddScoped<IUnitOfWork, RecordingUnitOfWork>()
            .AddApplication()
            .AddTransient<IRequestHandler<SampleCommand, Result<string>>, SampleCommandHandler>()
            .AddTransient<IRequestHandler<SampleQuery, Result<string>>, SampleQueryHandler>()
            .AddScoped<IValidator<SampleCommand>, SampleCommandValidator>()
            .BuildServiceProvider(validateScopes: true);

        _scope = _services.CreateScope();
    }

    /// <summary>What the pipeline did, in order.</summary>
    public PipelineJournal Journal => _journal;

    /// <summary>What the pipeline logged.</summary>
    public IReadOnlyList<RecordedLog> Logs => _logs.Entries;

    /// <summary>Sends a request through the whole pipeline, from one scope, as a request would.</summary>
    public ISender Sender => _scope.ServiceProvider.GetRequiredService<ISender>();

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _services.DisposeAsync();
    }
}

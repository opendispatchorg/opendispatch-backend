using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;

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
    private readonly Fakes.FakeAuditLog _audit = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;

    /// <param name="transientFailures">
    /// How many attempts the unit of work should fail transiently before letting one through — the
    /// database blip a retry exists for, without a database to unplug. Zero for every test that is
    /// not about the retry.
    /// </param>
    public SamplePipeline(int transientFailures = 0)
    {
        _services = new ServiceCollection()
            .AddSingleton(_journal)
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(_logs))
            .AddSingleton<ITenantContext>(new Fakes.FixedTenant(OrgId.New()))
            .AddSingleton<IClock>(new Fakes.FixedClock())
            .AddSingleton(_audit)
            .AddSingleton<IAuditLog>(_audit)
            .AddSingleton<ICallerContext>(new Fakes.FixedCaller(UserId.New(), "dana@vance.example"))
            .AddScoped<IUnitOfWork>(provider =>
                new RecordingUnitOfWork(provider.GetRequiredService<PipelineJournal>())
                {
                    TransientFailures = transientFailures,
                })
            .AddApplication()
            .AddTransient<IRequestHandler<SampleCommand, Result<string>>, SampleCommandHandler>()
            .AddTransient<IRequestHandler<SampleQuery, Result<string>>, SampleQueryHandler>()
            .AddScoped<IValidator<SampleCommand>, SampleCommandValidator>()
            .BuildServiceProvider(validateScopes: true);

        _scope = _services.CreateScope();
    }

    /// <summary>What the pipeline did, in order.</summary>
    public PipelineJournal Journal => _journal;

    /// <summary>What the pipeline wrote down about what was done.</summary>
    public Fakes.FakeAuditLog Audit => _audit;

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

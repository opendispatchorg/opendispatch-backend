using System.ComponentModel.DataAnnotations;

namespace OpenDispatch.Api.Configuration;

/// <summary>
/// How the outbox sweep behaves, bound from the <c>Outbox</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Every value has a working default, so a deployment that configures nothing gets a sweep every
/// ten seconds over messages older than thirty. What an operator gets from these is a way to slow it
/// down on a busy database — or, in an incident, to stop it (<c>Outbox:Enabled=false</c>) while a
/// poison message is dealt with, which is the only reason a deployment should ever turn it off.
/// </para>
/// <para>
/// The grace period must stay longer than a request takes: the ordinary path publishes and deletes
/// its rows a moment after the commit, so sweeping sooner would deliver everything twice for no
/// reason. Subscribers tolerate that, but it is waste rather than a feature.
/// </para>
/// </remarks>
public sealed class OutboxDeliveryOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Whether the sweep runs. Off is an incident measure, not a configuration.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Seconds between sweeps.</summary>
    [Range(1, 3600)]
    public int IntervalSeconds { get; init; } = 10;

    /// <summary>How old an undelivered message must be before a sweep takes it.</summary>
    [Range(1, 3600)]
    public int GraceSeconds { get; init; } = 30;

    /// <summary>How many messages one sweep claims.</summary>
    [Range(1, 10_000)]
    public int BatchSize { get; init; } = 50;
}

/// <summary>Registration for <see cref="OutboxDeliveryOptions"/>.</summary>
public static class OutboxDeliveryOptionsRegistration
{
    /// <summary>Binds and validates <see cref="OutboxDeliveryOptions"/>, on start like the rest.</summary>
    public static IServiceCollection AddOutboxOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<OutboxDeliveryOptions>()
            .Bind(configuration.GetSection(OutboxDeliveryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}

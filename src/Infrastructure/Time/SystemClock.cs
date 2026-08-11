using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Time;

/// <summary>
/// <see cref="IClock"/> over the machine's clock.
/// </summary>
/// <remarks>
/// The whole adapter, and it arrives with its first caller rather than with the port: step 35's
/// status change has to tell the domain when work finished, and when nobody has said otherwise the
/// answer is now. Reading <see cref="DateTimeOffset.UtcNow"/> in the handler instead would make
/// that handler untestable without waiting.
/// </remarks>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Registration for the clock.</summary>
public static class TimeRegistration
{
    /// <summary>
    /// Registers the machine clock behind <see cref="IClock"/>.
    /// </summary>
    /// <remarks>
    /// Separate from <c>AddPersistence</c> because a clock is not persistence, and a singleton
    /// because it holds nothing. It is called by the composition root and by the integration
    /// harness that stands in for it.
    /// </remarks>
    /// <param name="services">The host's service collection.</param>
    public static IServiceCollection AddSystemClock(this IServiceCollection services) =>
        services.AddSingleton<IClock, SystemClock>();
}

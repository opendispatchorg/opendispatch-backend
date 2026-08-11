using FluentValidation;

namespace OpenDispatch.Application.Validation;

/// <summary>
/// The rules that make a pair of numbers a place on the earth.
/// </summary>
/// <remarks>
/// <para>
/// A command carries a coordinate as two doubles rather than as a <c>GeoPoint</c>, because a value
/// object that refuses an impossible value by throwing would do so at the edge, before any
/// validator ran — turning a mistyped longitude into an unhandled exception instead of a rejected
/// field. These rules are what stands in for that: they mirror the bounds inside <c>GeoPoint</c>
/// so the caller is told, and the value object still refuses so the scheduler can trust every
/// point it is handed.
/// </para>
/// <para>
/// Shared because every aggregate that sits somewhere states it the same way — a service location
/// and a technician's home base are the same two numbers with the same meaning, and a copy of
/// these five lines per slice is five chances for one of them to omit the finiteness check.
/// </para>
/// </remarks>
internal static class CoordinateRules
{
    /// <summary>Refuses anything that is not a latitude.</summary>
    internal static IRuleBuilderOptions<T, double> IsLatitude<T>(this IRuleBuilderInitial<T, double> rule) =>
        rule
            // Stop at the first failure, so a NaN is reported as not being a number rather than
            // also as being outside a range it cannot meaningfully be compared to.
            .Cascade(CascadeMode.Stop)
            .Must(double.IsFinite).WithMessage("A latitude must be a number.")
            .InclusiveBetween(-90d, 90d);

    /// <summary>Refuses anything that is not a longitude.</summary>
    internal static IRuleBuilderOptions<T, double> IsLongitude<T>(this IRuleBuilderInitial<T, double> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .Must(double.IsFinite).WithMessage("A longitude must be a number.")
            .InclusiveBetween(-180d, 180d);
}

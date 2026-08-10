using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Persistence.Conversions;

/// <summary>
/// Maps a <see cref="TimeWindow"/> to its two instants.
/// </summary>
/// <remarks>
/// <para>
/// The odd one out in this folder: a window is genuinely two values, so it is a complex type
/// rather than a conversion, and the composite index step 27 puts on
/// <c>jobs(org_id, status, window_start)</c> needs the opening instant to be a column of its own.
/// </para>
/// <para>
/// It is an extension method rather than a line in
/// <c>ConfigureConventions</c> because a convention-level registration cannot declare a complex
/// type's members, and these members have to be declared. <see cref="TimeWindow"/>'s properties
/// are read-only — deliberately, so a <c>with</c> expression cannot produce a window that ends
/// before it starts — which means EF's property discovery skips them, and skipping them leaves it
/// unable to bind the constructor that is the only way to build the struct. Naming them here puts
/// them in the model, and EF then constructs each window through the constructor that validates
/// it.
/// </para>
/// <para>
/// Forgetting to call this is not a silent failure: an undeclared <see cref="TimeWindow"/>
/// property is one EF cannot map, and the model build says so.
/// </para>
/// </remarks>
public static class TimeWindowMapping
{
    /// <summary>Maps a window property to a pair of <c>timestamptz</c> columns.</summary>
    /// <param name="builder">The aggregate being configured.</param>
    /// <param name="window">The window property to map.</param>
    public static EntityTypeBuilder<TEntity> HasTimeWindow<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TimeWindow>> window)
        where TEntity : class =>
        builder.ComplexProperty(window, mapped =>
        {
            mapped.Property(w => w.Start);
            mapped.Property(w => w.End);
        });
}

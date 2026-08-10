using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Domain.Customers;

namespace OpenDispatch.Infrastructure.Persistence.Conversions;

/// <summary>
/// Maps a <see cref="ContactInfo"/> to its two optional columns.
/// </summary>
/// <remarks>
/// The same shape as <see cref="TimeWindowMapping"/>, and for the same reason: the value object's
/// properties are read-only, so EF's property discovery skips them and the constructor it would
/// otherwise bind has nothing to bind to. Naming both members puts them in the model, and EF then
/// builds each value through the constructor that normalises blanks to null.
/// </remarks>
public static class ContactInfoMapping
{
    /// <summary>Maps a contact property to a nullable email and phone column.</summary>
    /// <param name="builder">The aggregate being configured.</param>
    /// <param name="contact">The contact property to map.</param>
    public static EntityTypeBuilder<TEntity> HasContactInfo<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, ContactInfo>> contact)
        where TEntity : class =>
        builder.ComplexProperty(contact, mapped =>
        {
            mapped.Property(c => c.Email);
            mapped.Property(c => c.Phone);
        });
}

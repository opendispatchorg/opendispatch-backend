using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Application.Auth;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// Users: who may sign in.
/// </summary>
/// <remarks>
/// <para>
/// The second configuration in this folder whose type is not a domain aggregate, for the same
/// reason as the op log's: <see cref="AuthUser"/> is Application's (a login is not a business
/// concept — see <c>UserRole</c>), but this is where this database's tables are described, and a
/// table configured somewhere else is a table a reader has to be told about.
/// </para>
/// <para>
/// <strong>The username is unique across the deployment, not per tenant.</strong> It has to be:
/// a caller types a username and a password and nothing else, so if two organizations could both
/// hold "admin@example" there would be no way to tell which one is signing in. Uniqueness is a
/// unique index rather than a check in the store, so two <c>create-user</c> runs racing cannot both
/// win.
/// </para>
/// <para>
/// It is stored already normalized (see <c>AuthUsername</c>) rather than compared with a
/// case-insensitive collation, so the index that enforces uniqueness is the same index the lookup
/// uses — a <c>citext</c> column or a <c>lower(...)</c> expression index would work equally well and
/// would put the rule in the schema instead of in one small class either side of it.
/// </para>
/// </remarks>
internal sealed class AuthUserConfiguration : IEntityTypeConfiguration<AuthUser>
{
    /// <summary>
    /// How long a username may be: an email address's full length, since that is what every login
    /// in this system is. The same number <c>TextLimits.Email</c> holds for the validators, which
    /// is Application-internal and so cannot be named from here.
    /// </summary>
    private const int UsernameLength = 320;

    /// <summary>Room for the longest role name, and nothing like enough for prose.</summary>
    private const int RoleLength = 40;

    public void Configure(EntityTypeBuilder<AuthUser> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);

        builder.HasIndex(user => user.Username).IsUnique();

        // Scoped like every other tenant-owned table, even though the login path deliberately
        // reads past the filter: an administrator listing their organization's users later should
        // not have to remember the scope, and the index is what makes that cheap.
        builder.HasIndex(user => user.OrgId);

        builder.Property(user => user.Username).HasMaxLength(UsernameLength);

        // Stored as the name of the role rather than its ordinal. Roles are read by people — in a
        // psql session, in a support conversation, in the row `create-user` prints back — and an
        // enum's ordinal is a number whose meaning lives in a C# file. Renumbering the enum also
        // stops being a silent privilege change.
        builder.Property(user => user.Role).HasConversion<string>().HasMaxLength(RoleLength);
    }
}

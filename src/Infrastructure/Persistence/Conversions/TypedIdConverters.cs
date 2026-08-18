using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenDispatch.Application.Auth;
using OpenDispatch.Application.Sync;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Infrastructure.Persistence.Conversions;

/*
 * One converter per strongly-typed id, kept in one file because each is a single expression
 * and eight files of two lines would hide the fact that this is a list — of every identity in
 * the system, and of exactly which ones the database knows how to store.
 *
 * They are registered as model-wide conventions in AppDbContext.ConfigureConventions, not per
 * property, so a JobId is a uuid wherever it appears and no per-aggregate configuration has to
 * remember a converter. A new typed id costs one class here and one line there; forgetting is
 * not silent, because an unconverted id fails the model build.
 *
 * A single generic converter over an IStronglyTypedId interface was the alternative. It would
 * have meant a Domain interface added for the benefit of the persistence layer, and traded
 * eight declarations here for eight annotations there — no smaller, and paid for in the one
 * project that is supposed to answer to nothing.
 *
 * Two of them are not Domain ids: SyncOpId identifies an entry in the sync op log and UserId a
 * row in the user store, both protocol or access bookkeeping rather than business concepts, and
 * both therefore living with their ports in Application. They are stored the same way as the
 * rest, so they are declared with the rest.
 */

internal sealed class AssignmentIdConverter()
    : ValueConverter<AssignmentId, Guid>(id => id.Value, value => AssignmentId.From(value))
{
}

internal sealed class AttachmentIdConverter()
    : ValueConverter<AttachmentId, Guid>(id => id.Value, value => AttachmentId.From(value))
{
}

internal sealed class CustomerIdConverter()
    : ValueConverter<CustomerId, Guid>(id => id.Value, value => CustomerId.From(value))
{
}

internal sealed class InvoiceIdConverter()
    : ValueConverter<InvoiceId, Guid>(id => id.Value, value => InvoiceId.From(value))
{
}

internal sealed class JobIdConverter()
    : ValueConverter<JobId, Guid>(id => id.Value, value => JobId.From(value))
{
}

internal sealed class LineItemIdConverter()
    : ValueConverter<LineItemId, Guid>(id => id.Value, value => LineItemId.From(value))
{
}

internal sealed class JobLineIdConverter()
    : ValueConverter<JobLineId, Guid>(id => id.Value, value => JobLineId.From(value))
{
}

internal sealed class OrgIdConverter()
    : ValueConverter<OrgId, Guid>(id => id.Value, value => OrgId.From(value))
{
}

internal sealed class ServiceLocationIdConverter()
    : ValueConverter<ServiceLocationId, Guid>(id => id.Value, value => ServiceLocationId.From(value))
{
}

internal sealed class SyncOpIdConverter()
    : ValueConverter<SyncOpId, Guid>(id => id.Value, value => SyncOpId.From(value))
{
}

internal sealed class TechnicianIdConverter()
    : ValueConverter<TechnicianId, Guid>(id => id.Value, value => TechnicianId.From(value))
{
}

internal sealed class UserIdConverter()
    : ValueConverter<UserId, Guid>(id => id.Value, value => UserId.From(value))
{
}

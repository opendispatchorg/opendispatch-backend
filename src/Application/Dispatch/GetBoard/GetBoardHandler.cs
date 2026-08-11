using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Dispatch.GetBoard;

/// <summary>
/// Asks the read model for the day.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the thinnest handler in the application. Everything expensive about the board is a
/// question of how one query is written, which is Infrastructure's business; everything that would
/// make this handler interesting — loading aggregates, joining them in memory, deciding what a
/// stop is late against — is a thing the board is specifically designed not to do.
/// </para>
/// <para>
/// A query, so no transaction: the pipeline gives one only to commands, and a board poll that
/// opened one would buy a round trip for nothing.
/// </para>
/// </remarks>
internal sealed class GetBoardHandler(IDispatchBoardReadModel board)
    : IRequestHandler<GetBoardQuery, Result<DispatchBoard>>
{
    public async Task<Result<DispatchBoard>> Handle(GetBoardQuery query, CancellationToken cancellationToken)
    {
        var day = new TimeWindow(query.From.ToUniversalTime(), query.To.ToUniversalTime());

        return Result.Success(await board.GetAsync(day, cancellationToken).ConfigureAwait(false));
    }
}

using MediatR;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Application.Technicians.GetTechnician;
using OpenDispatch.Application.Technicians.ListTechnicians;
using OpenDispatch.Application.Technicians.SetShift;
using OpenDispatch.Application.Technicians.SetSkills;
using OpenDispatch.Application.Technicians.UpdateTechnician;
using OpenDispatch.Contracts.Technicians;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Api.Technicians;

/// <summary>The crew — CRUD plus skills and shift (Document 3, step 47).</summary>
/// <remarks>
/// Reading the crew is <see cref="AuthPolicies.AdminOrDispatcher"/> — a dispatcher has to see who
/// is available to book a job onto them — but changing who is on the crew, and what they are
/// qualified for, is <see cref="AuthPolicies.AdminOnly"/>: staffing is Document 1's admin persona,
/// not the dispatcher's.
/// </remarks>
public static class TechnicianEndpoints
{
    public static IEndpointRouteBuilder MapTechnicianEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var technicians = endpoints.MapGroup("/technicians").WithTags("Technicians");

        technicians.MapGet("/", ListAsync)
            .RequireAuthorization(AuthPolicies.AdminOrDispatcher)
            .WithName("ListTechnicians");
        technicians.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(AuthPolicies.AdminOrDispatcher)
            .WithName("GetTechnician");

        technicians.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .WithName("CreateTechnician");
        technicians.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .WithName("UpdateTechnician");
        technicians.MapPut("/{id:guid}/skills", SetSkillsAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .WithName("SetTechnicianSkills");
        technicians.MapPut("/{id:guid}/shift", SetShiftAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .WithName("SetTechnicianShift");

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateTechnicianRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateTechnicianCommand(
            request.Name, request.Skills, request.ShiftStart, request.ShiftEnd, request.Latitude, request.Longitude);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(id => Results.Created(
            $"/technicians/{id.Value}",
            new TechnicianResponse(
                id.Value, request.Name, request.Skills, request.ShiftStart, request.ShiftEnd,
                request.Latitude, request.Longitude)));
    }

    private static async Task<IResult> ListAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListTechniciansQuery(), cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(crew => Results.Ok(crew.Select(ToResponse)));
    }

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender
            .Send(new GetTechnicianQuery(TechnicianId.From(id)), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(technician => Results.Ok(ToResponse(technician)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateTechnicianRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateTechnicianCommand(TechnicianId.From(id), request.Name, request.Latitude, request.Longitude);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult();
    }

    private static async Task<IResult> SetSkillsAsync(
        Guid id,
        SetSkillsRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender
            .Send(new SetSkillsCommand(TechnicianId.From(id), request.Skills), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult();
    }

    private static async Task<IResult> SetShiftAsync(
        Guid id,
        SetShiftRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender
            .Send(new SetShiftCommand(TechnicianId.From(id), request.Start, request.End), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult();
    }

    private static TechnicianResponse ToResponse(TechnicianSummary technician) => new(
        technician.Id.Value,
        technician.Name,
        technician.Skills,
        technician.ShiftStart,
        technician.ShiftEnd,
        technician.Latitude,
        technician.Longitude);
}

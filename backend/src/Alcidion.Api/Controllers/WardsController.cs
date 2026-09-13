using Alcidion.Admissions.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

/// <param name="FreeBeds">Allocatable right now, so the admit form can say where a patient will fit.</param>
public sealed record WardDto(Guid Id, string Code, string Name, string WardType, int Beds, int OccupiedBeds, int EffectiveCapacity, int FreeBeds)
{
    public static WardDto From(WardSummary w) =>
        new(w.Id, w.Code, w.Name, w.WardType, w.Beds, w.OccupiedBeds, w.EffectiveCapacity, w.FreeBeds);
}

/// <summary>
/// The wards a patient can be admitted to. Occupancy counts only - no patient identifiers or
/// demographics leave this read model, the same rule the hospital occupancy view follows.
/// </summary>
[Route("api/wards")]
[Authorize]
public sealed class WardsController(IWardDirectory wards) : ApiController
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WardDto>>> List(CancellationToken ct) =>
        Ok((await wards.ListAsync(ct)).Select(WardDto.From).ToList());
}

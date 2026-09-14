using Alcidion.Hospital;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

[Authorize]
[Route("api/hospital-occupancy")]
public sealed class HospitalOccupancyController(IHospitalOccupancyReader reader) : ApiController
{
    [HttpGet]
    [EndpointName("GetHospitalOccupancy")]
    [EndpointSummary("Get hospital occupancy")]
    [EndpointDescription("Returns the hospital's beds and patient placements at the requested instant, or now if at is omitted. Requires hospital storage; returns 503 when unavailable.")]
    [ProducesResponseType<HospitalSnapshot>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HospitalSnapshot>> Get([FromQuery] DateTimeOffset? at, CancellationToken ct) =>
        (await reader.ReadAsync(at, ct)).Match<ActionResult<HospitalSnapshot>>(snapshot => Ok(snapshot), error => FromError(error));
}

using Alcidion.Hospital;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

[Authorize]
[Route("api/hospital-occupancy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HospitalOccupancyController(IHospitalOccupancyReader reader) : ApiController
{
    [HttpGet]
    public async Task<ActionResult<HospitalSnapshot>> Get([FromQuery] DateTimeOffset? at, CancellationToken ct) =>
        (await reader.ReadAsync(at, ct)).Match<ActionResult<HospitalSnapshot>>(snapshot => Ok(snapshot), error => FromError(error));
}

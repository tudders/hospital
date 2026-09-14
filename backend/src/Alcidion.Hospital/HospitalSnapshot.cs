using Alcidion.Shared;

namespace Alcidion.Hospital;

// Patient placement is included for authorised operational views. Demographics stay in the
// Patients context; this read model carries only the identity needed to locate a bed.
public sealed record HospitalBed(
    Guid Id, string Code, int Number,
    Guid HospitalId, string HospitalName,
    Guid FloorId, string FloorName, int FloorNumber,
    Guid WardId, string WardName,
    Guid RoomId, string RoomName, int RoomNumber,
    string Status, Guid? PatientId, string? PatientName);

public sealed record HospitalSnapshot(
    DateTimeOffset AsOf, DateTimeOffset CapturedAt, string Source, IReadOnlyList<HospitalBed> Beds);

/// <summary>
/// Every bed in the hospital as of a moment, with what is in it. A read model in its own right:
/// it crosses the physical hierarchy, occupancy and patient identity, which no single one of the
/// writing contexts owns, so it reads those tables and writes none of them.
/// </summary>
public interface IHospitalOccupancyReader
{
    Task<Result<HospitalSnapshot>> ReadAsync(DateTimeOffset? at, CancellationToken ct);
}

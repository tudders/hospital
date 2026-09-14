using Alcidion.Shared;

namespace Alcidion.Hospital;

/// <summary>A bed's location and occupancy, including the occupying patient's ID and name when present.</summary>
public sealed record HospitalBed(
    Guid Id, string Code, int Number,
    Guid HospitalId, string HospitalName,
    Guid FloorId, string FloorName, int FloorNumber,
    Guid WardId, string WardName,
    Guid RoomId, string RoomName, int RoomNumber,
    string Status, Guid? PatientId, string? PatientName);

/// <summary>Hospital beds and patient placements at a point in time.</summary>
/// <param name="AsOf">The requested instant for which occupancy was calculated.</param>
/// <param name="CapturedAt">When this snapshot was read from storage.</param>
/// <param name="Source">The source of the occupancy data.</param>
/// <param name="Beds">Beds in the hospital hierarchy, with their occupancy and patient placement.</param>
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

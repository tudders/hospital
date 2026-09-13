namespace Alcidion.Api.Hospital;

// Occupancy only: no patient identifiers or demographics leave this read model.
public sealed record HospitalBed(
    Guid Id, string Code, int Number,
    Guid HospitalId, string HospitalName,
    Guid FloorId, string FloorName, int FloorNumber,
    Guid WardId, string WardName,
    Guid RoomId, string RoomName, int RoomNumber,
    string Status);

public sealed record HospitalSnapshot(
    DateTimeOffset AsOf, DateTimeOffset CapturedAt, string Source, IReadOnlyList<HospitalBed> Beds);

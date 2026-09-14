namespace Alcidion.Api.Hospital;

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

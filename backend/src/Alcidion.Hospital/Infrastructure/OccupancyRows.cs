namespace Alcidion.Hospital.Infrastructure;

/// <summary>
/// One row of the occupancy snapshot: a bed, where it is in the building, what state it is in, and
/// who is in it. Keyless and backed by no table - it is the shape of a query, not of storage - so
/// the context can materialise the snapshot without anything here being writable or trackable.
/// <para>
/// Every property is mapped to the column the query names it. That mapping is what replaced sixteen
/// positional <c>reader.GetGuid(n)</c> calls, where inserting a column silently shifted every field
/// after it; <c>OccupancyReaderTests</c> checks the two still agree.
/// </para>
/// </summary>
internal sealed class BedSnapshotRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public int Number { get; set; }
    public Guid HospitalId { get; set; }
    public string HospitalName { get; set; } = "";
    public Guid FloorId { get; set; }
    public string FloorName { get; set; } = "";
    public int FloorNumber { get; set; }
    public Guid WardId { get; set; }
    public string WardName { get; set; } = "";
    public Guid RoomId { get; set; }
    public string RoomName { get; set; } = "";
    public int RoomNumber { get; set; }
    public string Status { get; set; } = "";
    public Guid? PatientId { get; set; }
    public string? PatientName { get; set; }
}

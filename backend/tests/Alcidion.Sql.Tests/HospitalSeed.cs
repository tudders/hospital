namespace Alcidion.Sql.Tests;

/// <summary>A ward the test owns outright, with its own hospital, floor and room above it.</summary>
public sealed record SeededWard(
    Guid HospitalId, Guid FloorId, Guid WardId, Guid RoomId,
    string Code, string Name, string WardType, IReadOnlyList<Guid> Beds);

/// <summary>
/// Builds hospitals a test can have to itself. Bed allocation is ward-scoped and the schema keeps
/// one open admission per patient, so tests that shared the seeded hospital would decide each
/// other's outcomes; every test seeds its own ward and its own patients instead, and the whole
/// suite still runs against one database.
/// </summary>
public sealed class HospitalSeed(SqlServerFixture sql)
{
    private int _next;

    /// <summary>Short, unique and stable within a run - the schema's codes are all unique.</summary>
    private string NextTag() => $"T{Guid.NewGuid():N}"[..9] + Interlocked.Increment(ref _next);

    /// <summary>
    /// A ward with <paramref name="beds"/> beds in one room. <paramref name="staffedLimit"/> adds a
    /// capacity period, which caps admissions below the bed count - the limit the beds alone cannot
    /// express. <paramref name="availableFrom"/> backdates the beds so a snapshot taken in the past
    /// still sees them in service.
    /// </summary>
    public async Task<SeededWard> WardAsync(int beds, int? staffedLimit = null,
        DateTimeOffset? availableFrom = null, string wardType = "general")
    {
        var tag = NextTag();
        var hospital = Guid.NewGuid();
        var floor = Guid.NewGuid();
        var ward = Guid.NewGuid();
        var room = Guid.NewGuid();
        var from = availableFrom ?? DateTimeOffset.UtcNow.AddYears(-1);
        var name = $"Ward {tag}";

        await sql.ExecuteAsync("""
            INSERT INTO dbo.hospitals (id, code, name) VALUES (@hospital, @tag, @hospitalName);
            INSERT INTO dbo.floors (id, hospital_id, floor_number, code, name)
                VALUES (@floor, @hospital, 1, @floorCode, @floorName);
            INSERT INTO dbo.wards (id, code, name, ward_type, floor_id)
                VALUES (@ward, @wardCode, @wardName, @wardType, @floor);
            INSERT INTO dbo.rooms (id, floor_id, ward_id, room_number, code, name)
                VALUES (@room, @floor, @ward, 1, @roomCode, @roomName);
            """,
            ("@hospital", hospital), ("@tag", tag), ("@hospitalName", $"Hospital {tag}"),
            ("@floor", floor), ("@floorCode", $"{tag}-F"), ("@floorName", $"Floor {tag}"),
            ("@ward", ward), ("@wardCode", $"{tag}-W"), ("@wardName", name), ("@wardType", wardType),
            ("@room", room), ("@roomCode", $"{tag}-R"), ("@roomName", $"Room {tag}"));

        var ids = new List<Guid>();
        for (var i = 1; i <= beds; i++)
        {
            var bed = Guid.NewGuid();
            ids.Add(bed);
            await sql.ExecuteAsync("""
                INSERT INTO dbo.beds (id, ward_id, code, bed_type, available_from, room_id, bed_number)
                VALUES (@id, @ward, @code, N'standard', @from, @room, @number);
                """,
                ("@id", bed), ("@ward", ward), ("@code", $"{tag}-B{i:00}"),
                ("@from", from), ("@room", room), ("@number", i));
        }

        if (staffedLimit is { } limit)
            await sql.ExecuteAsync("""
                INSERT INTO dbo.ward_capacity_periods (id, ward_id, starts_at, ends_at, staffed_bed_limit)
                VALUES (@id, @ward, @from, NULL, @limit);
                """,
                ("@id", Guid.NewGuid()), ("@ward", ward), ("@from", from), ("@limit", limit));

        return new SeededWard(hospital, floor, ward, room, $"{tag}-W", name, wardType, ids);
    }

    /// <summary>A patient with a unique MRN, which the schema requires to be trimmed and upper case.</summary>
    public async Task<Guid> PatientAsync(string given = "Ada", string family = "Lovelace")
    {
        var id = Guid.NewGuid();
        await sql.ExecuteAsync("""
            INSERT INTO dbo.patients (id, mrn, given_name, family_name, date_of_birth, gender, registered_at)
            VALUES (@id, @mrn, @given, @family, '1990-01-01', N'unknown', SYSDATETIMEOFFSET());
            """,
            ("@id", id), ("@mrn", $"MRN{NextTag().ToUpperInvariant()}"), ("@given", given), ("@family", family));
        return id;
    }

    /// <summary>Blocks a bed from <paramref name="from"/>, the way cleaning or maintenance does.</summary>
    public Task BlockBedAsync(Guid bedId, DateTimeOffset from, DateTimeOffset? until = null) =>
        sql.ExecuteAsync("""
            INSERT INTO dbo.bed_blocks (id, bed_id, starts_at, ends_at, reason)
            VALUES (@id, @bed, @from, @until, N'maintenance');
            """,
            ("@id", Guid.NewGuid()), ("@bed", bedId), ("@from", from), ("@until", until));

    /// <summary>
    /// An admission that has asked for a bed and not been given one. The repository never writes
    /// this state - allocation is part of admitting - but the schema allows it and the read path
    /// has to describe it, so the test has to be able to create it.
    /// </summary>
    public async Task<Guid> AwaitingBedAsync(Guid patientId, Guid wardId, DateTimeOffset requestedAt)
    {
        var admission = Guid.NewGuid();
        await sql.ExecuteAsync("""
            INSERT INTO dbo.admissions (id, patient_id, requested_at) VALUES (@id, @patient, @at);
            INSERT INTO dbo.bed_requests (id, admission_id, target_ward_id, requested_at)
                VALUES (@request, @id, @ward, @at);
            """,
            ("@id", admission), ("@patient", patientId), ("@at", requestedAt),
            ("@request", Guid.NewGuid()), ("@ward", wardId));
        return admission;
    }
}

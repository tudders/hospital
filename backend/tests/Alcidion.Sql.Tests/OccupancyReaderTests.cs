using Alcidion.Admissions.Domain;
using Alcidion.Admissions.Infrastructure;
using Alcidion.Hospital;
using Alcidion.Hospital.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alcidion.Sql.Tests;

/// <summary>
/// The occupancy snapshot against the real schema. The load-bearing test here is the last one: the
/// reader materialises sixteen columns by name, where it used to read them by ordinal, and that
/// test reads the same query positionally and requires the two to agree row for row. A column
/// mapped to the wrong name - two ids of the same type swapped, a name pointing at the floor
/// instead of the ward - is invisible everywhere else and fails there.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class OccupancyReaderTests(SqlServerFixture sql)
{
    private readonly HospitalSeed _seed = new(sql);
    private static readonly DateTimeOffset AsOf = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private IHospitalOccupancyReader Reader() => new EfHospitalOccupancyReader(
        new OccupancyDbContext(new DbContextOptionsBuilder<OccupancyDbContext>()
            .UseSqlServer(sql.ConnectionString).Options),
        TimeProvider.System,
        NullLogger<EfHospitalOccupancyReader>.Instance);

    private EfAdmissionRepository Admissions() => new(
        new AdmissionsDbContext(new DbContextOptionsBuilder<AdmissionsDbContext>()
            .UseSqlServer(sql.ConnectionString).Options),
        NullLogger<EfAdmissionRepository>.Instance);

    private async Task<IReadOnlyList<HospitalBed>> ReadAsync(DateTimeOffset at)
    {
        var result = await Reader().ReadAsync(at, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!.Beds;
    }

    [SqlFact]
    public async Task Every_bed_is_reported_with_its_place_in_the_building()
    {
        var ward = await _seed.WardAsync(beds: 3);

        var beds = (await ReadAsync(AsOf)).Where(b => b.WardId == ward.WardId).ToList();

        Assert.Equal(3, beds.Count);
        Assert.Equal([1, 2, 3], beds.Select(b => b.Number));
        Assert.All(beds, b =>
        {
            Assert.Equal(ward.HospitalId, b.HospitalId);
            Assert.Equal(ward.FloorId, b.FloorId);
            Assert.Equal(ward.RoomId, b.RoomId);
            Assert.Equal(ward.Name, b.WardName);
            Assert.Equal(1, b.FloorNumber);
            Assert.Equal(1, b.RoomNumber);
        });
    }

    [SqlFact]
    public async Task A_bed_reports_available_occupied_blocked_or_out_of_service()
    {
        var ward = await _seed.WardAsync(beds: 5);
        await Admissions().TryAddActiveAsync(Admission.Admit(await _seed.PatientAsync(), ward.Code, AsOf.AddHours(-2)));
        await _seed.BlockBedAsync(ward.Beds[1], AsOf.AddHours(-1));
        await NotInServiceUntil(ward.Beds[2], AsOf.AddDays(30));
        await RetiredAt(ward.Beds[3], AsOf.AddHours(-1));

        var beds = (await ReadAsync(AsOf)).Where(b => b.WardId == ward.WardId).ToDictionary(b => b.Id, b => b.Status);

        // The allocator takes beds in code order, so the admission is in the first one.
        Assert.Equal("occupied", beds[ward.Beds[0]]);
        Assert.Equal("blocked", beds[ward.Beds[1]]);
        Assert.Equal("inactive", beds[ward.Beds[2]]);
        Assert.Equal("inactive", beds[ward.Beds[3]]);
        Assert.Equal("available", beds[ward.Beds[4]]);
    }

    [SqlFact]
    public async Task An_occupied_bed_names_the_patient_in_it()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var patient = await _seed.PatientAsync("Grace", "Hopper");
        await Admissions().TryAddActiveAsync(Admission.Admit(patient, ward.Code, AsOf.AddHours(-2)));

        var bed = Assert.Single(await ReadAsync(AsOf), b => b.WardId == ward.WardId);

        Assert.Equal(patient, bed.PatientId);
        Assert.Equal("Grace Hopper", bed.PatientName);
    }

    [SqlFact]
    public async Task An_empty_bed_names_nobody()
    {
        var ward = await _seed.WardAsync(beds: 1);

        var bed = Assert.Single(await ReadAsync(AsOf), b => b.WardId == ward.WardId);

        Assert.Null(bed.PatientId);
        Assert.Null(bed.PatientName);
    }

    [SqlFact]
    public async Task A_snapshot_of_an_earlier_moment_sees_the_hospital_as_it_was()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var admitted = Assert.IsType<AdmitResult.Admitted>(await Admissions().TryAddActiveAsync(
            Admission.Admit(await _seed.PatientAsync(), ward.Code, AsOf.AddHours(-2)))).Admission;
        admitted.Discharge(AsOf.AddHours(-1));
        await Admissions().UpdateAsync(admitted);

        var before = Assert.Single(await ReadAsync(AsOf.AddHours(-3)), b => b.WardId == ward.WardId);
        var during = Assert.Single(await ReadAsync(AsOf.AddHours(-90 / 60.0)), b => b.WardId == ward.WardId);
        var after = Assert.Single(await ReadAsync(AsOf), b => b.WardId == ward.WardId);

        Assert.Equal("available", before.Status);
        Assert.Equal("occupied", during.Status);
        Assert.Equal("available", after.Status);   // the stay ended, so the bed is free again
    }

    [SqlFact]
    public async Task A_stay_that_ends_exactly_at_the_snapshot_no_longer_counts()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var admitted = Assert.IsType<AdmitResult.Admitted>(await Admissions().TryAddActiveAsync(
            Admission.Admit(await _seed.PatientAsync(), ward.Code, AsOf.AddHours(-2)))).Admission;
        admitted.Discharge(AsOf);
        await Admissions().UpdateAsync(admitted);

        // Half-open intervals: [start, end). A transfer at T is counted in one bed, not two.
        Assert.Equal("available", Assert.Single(await ReadAsync(AsOf), b => b.WardId == ward.WardId).Status);
    }

    [SqlFact]
    public async Task A_cancelled_admission_leaves_the_bed_occupied_by_nobody()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var admitted = Assert.IsType<AdmitResult.Admitted>(await Admissions().TryAddActiveAsync(
            Admission.Admit(await _seed.PatientAsync(), ward.Code, AsOf.AddHours(-2)))).Admission;
        // The repository never writes this, but the schema allows it and the query filters for it.
        await sql.ExecuteAsync(
            "UPDATE dbo.admissions SET admitted_at = NULL, cancelled_at = @at WHERE id = @id",
            ("@at", AsOf.AddHours(-1)), ("@id", admitted.Id));

        var bed = Assert.Single(await ReadAsync(AsOf), b => b.WardId == ward.WardId);

        Assert.Equal("occupied", bed.Status);   // the stay is still open; the status is physical
        Assert.Null(bed.PatientId);             // but a cancelled episode places nobody
    }

    [SqlFact]
    public async Task A_ward_with_no_floor_has_no_place_in_the_building_and_is_left_out()
    {
        var ward = await _seed.WardAsync(beds: 1);
        await sql.ExecuteAsync("UPDATE dbo.wards SET floor_id = NULL WHERE id = @id", ("@id", ward.WardId));

        Assert.DoesNotContain(await ReadAsync(AsOf), b => b.WardId == ward.WardId);
    }

    [SqlFact]
    public async Task A_snapshot_of_the_future_is_a_bad_request_not_an_empty_hospital()
    {
        var result = await Reader().ReadAsync(DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Error!.Code);
    }

    [SqlFact]
    public async Task With_no_database_configured_the_view_is_unavailable_but_still_rejects_a_bad_time()
    {
        var reader = new UnavailableHospitalOccupancyReader(TimeProvider.System);

        var future = await reader.ReadAsync(DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);
        var now = await reader.ReadAsync(null, CancellationToken.None);

        Assert.Equal("validation", future.Error!.Code);
        Assert.Equal("hospital_unavailable", now.Error!.Code);
    }

    // --- The one that matters ----------------------------------------------

    [SqlFact]
    public async Task Every_column_lands_in_the_field_the_ordinal_read_put_it_in()
    {
        // Every shape the CASE and the OUTER APPLY have to distinguish, in one hospital. The
        // admissions come first: a bed cannot be blocked and occupied over the same interval, and
        // the allocator takes beds in code order, so blocking one before admitting would decide
        // which bed each patient got.
        var ward = await _seed.WardAsync(beds: 6);
        await Admissions().TryAddActiveAsync(
            Admission.Admit(await _seed.PatientAsync("Ada", "Byron"), ward.Code, AsOf.AddHours(-3)));
        var moved = Assert.IsType<AdmitResult.Admitted>(await Admissions().TryAddActiveAsync(
            Admission.Admit(await _seed.PatientAsync("Grace", "Hopper"), ward.Code, AsOf.AddHours(-3)))).Admission;
        var elsewhere = await _seed.WardAsync(beds: 1);
        // Leaves a closed stay behind on bed 2, which the OUTER APPLY has to not report as anyone.
        await Admissions().TransferAsync(moved.Id, elsewhere.Code, AsOf.AddHours(-2));
        await _seed.BlockBedAsync(ward.Beds[2], AsOf.AddHours(-2));
        await _seed.BlockBedAsync(ward.Beds[3], AsOf.AddHours(-5), AsOf.AddHours(-4));   // a block that has ended
        await NotInServiceUntil(ward.Beds[4], AsOf.AddDays(10));
        await RetiredAt(ward.Beds[5], AsOf.AddHours(-1));

        var actual = await ReadAsync(AsOf);
        var expected = await ByOrdinalAsync(AsOf);

        // Not just the seeded ward: this compares every bed in the database, including the hospital
        // the tracked seed migrations build and whatever the rest of the suite has left behind.
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected, actual);
    }

    private Task NotInServiceUntil(Guid bedId, DateTimeOffset from) =>
        sql.ExecuteAsync("UPDATE dbo.beds SET available_from = @from WHERE id = @id", ("@from", from), ("@id", bedId));

    private Task RetiredAt(Guid bedId, DateTimeOffset at) =>
        sql.ExecuteAsync("UPDATE dbo.beds SET retired_at = @at WHERE id = @id", ("@at", at), ("@id", bedId));

    /// <summary>
    /// The snapshot read the way it used to be read: by column position, with no model in between.
    /// Independent of every <c>HasColumnName</c> in the context, which is the point - it is the only
    /// check that the names the reader maps and the columns the query returns still line up.
    /// </summary>
    private async Task<List<HospitalBed>> ByOrdinalAsync(DateTimeOffset at)
    {
        await using var connection = sql.Connect();
        await connection.OpenAsync();
        await using var command = new SqlCommand(ByPosition, connection);
        command.Parameters.Add("@at", System.Data.SqlDbType.DateTimeOffset).Value = at;
        var beds = new List<HospitalBed>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            beds.Add(new HospitalBed(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5), reader.GetString(6), reader.GetInt32(7),
                reader.GetGuid(8), reader.GetString(9), reader.GetGuid(10), reader.GetString(11), reader.GetInt32(12),
                reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetGuid(14),
                reader.IsDBNull(15) ? null : reader.GetString(15)));
        return beds;
    }

    private const string ByPosition = """
        SELECT b.id, b.code, b.bed_number, h.id, h.name,
               f.id, f.name, f.floor_number, w.id, w.name,
               r.id, r.name, r.room_number,
               CASE
                 WHEN b.available_from > @at OR b.retired_at <= @at THEN 'inactive'
                 WHEN EXISTS (SELECT 1 FROM dbo.bed_stays s WHERE s.bed_id = b.id
                     AND s.started_at <= @at AND (s.ended_at IS NULL OR s.ended_at > @at)) THEN 'occupied'
                 WHEN EXISTS (SELECT 1 FROM dbo.bed_blocks x WHERE x.bed_id = b.id
                     AND x.starts_at <= @at AND (x.ends_at IS NULL OR x.ends_at > @at)) THEN 'blocked'
                 ELSE 'available'
               END AS status,
               placement.patient_id, placement.patient_name
        FROM dbo.hospitals h
        JOIN dbo.floors f ON f.hospital_id = h.id
        JOIN dbo.wards w ON w.floor_id = f.id
        JOIN dbo.rooms r ON r.ward_id = w.id AND r.floor_id = f.id
        JOIN dbo.beds b ON b.room_id = r.id AND b.ward_id = w.id
        OUTER APPLY (
            SELECT TOP (1) a.patient_id,
                   CONCAT(p.given_name, ' ', p.family_name) AS patient_name
            FROM dbo.bed_stays s
            JOIN dbo.admissions a ON a.id = s.admission_id
            JOIN dbo.patients p ON p.id = a.patient_id
            WHERE s.bed_id = b.id
              AND s.started_at <= @at AND (s.ended_at IS NULL OR s.ended_at > @at)
              AND a.cancelled_at IS NULL
            ORDER BY s.started_at DESC
        ) placement
        ORDER BY h.code, f.floor_number, w.code, r.room_number, b.bed_number
        """;
}

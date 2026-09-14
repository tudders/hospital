using System.Data;
using Alcidion.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alcidion.Hospital.Infrastructure;

/// <summary>
/// Reads every bed in the hospital as of a moment. Stays and blocks use half-open intervals
/// [start, end), so a transfer at T is counted once. These are physical availability states, not a
/// promise of staffed or clinically suitable capacity.
/// </summary>
public sealed class EfHospitalOccupancyReader(
    OccupancyDbContext db, TimeProvider time, ILogger<EfHospitalOccupancyReader> logger) : IHospitalOccupancyReader
{
    /// <summary>
    /// Long enough for a serializable scan over every bed in the hospital, short enough that a
    /// blocked read gives the caller an answer rather than holding the request open.
    /// </summary>
    private const int QueryTimeoutSeconds = 20;

    public async Task<Result<HospitalSnapshot>> ReadAsync(DateTimeOffset? at, CancellationToken ct)
    {
        var window = OccupancyReads.Resolve(at, time);
        if (window.Error is { } rejected) return Result<HospitalSnapshot>.Fail(rejected);
        var asOf = window.Value;

        try
        {
            db.Database.SetCommandTimeout(QueryTimeoutSeconds);
            // Keep all hierarchy and interval reads consistent, even during a concurrent transfer.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var rows = await Snapshot(asOf).ToListAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<HospitalSnapshot>.Ok(new(asOf, time.GetUtcNow(), "sql", [.. rows.Select(Bed)]));
        }
        catch (SqlException ex)
        {
            // SQL error messages can contain server/login names. Only log the numeric code.
            logger.LogWarning("Hospital occupancy SQL read failed with code {SqlErrorNumber}", ex.Number);
            return OccupancyReads.Unavailable;
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Hospital occupancy connection configuration is invalid");
            return OccupancyReads.Unavailable;
        }
    }

    private static HospitalBed Bed(BedSnapshotRow row) => new(
        row.Id, row.Code, row.Number,
        row.HospitalId, row.HospitalName,
        row.FloorId, row.FloorName, row.FloorNumber,
        row.WardId, row.WardName,
        row.RoomId, row.RoomName, row.RoomNumber,
        row.Status, row.PatientId, row.PatientName);

    /// <summary>
    /// The hospital, floor by floor, with whoever is in each bed.
    /// <para>
    /// Written out rather than composed in LINQ, for the reason the plans give. The placement is an
    /// <c>OUTER APPLY (SELECT TOP (1) ...)</c>, which seeks <c>ix_bed_stays_bed_history</c> once per
    /// bed and stays that cheap however long the hospital runs. EF Core rewrites every correlated
    /// <c>FirstOrDefault</c>/<c>Take(1)</c> into a <c>ROW_NUMBER</c> over the whole open-stay set
    /// joined to the whole admissions table and spooled back per bed: measured on the seeded
    /// hospital (720 beds, 486 stays) that was 41 ms of server CPU against 10 ms, and unlike the
    /// apply its cost grows with stay history rather than with the number of beds. No index fixes
    /// it - <c>started_at &lt;= @at AND (ended_at IS NULL OR ended_at &gt; @at)</c> cannot seek - and
    /// no LINQ formulation avoids the rewrite, so the query stays as SQL.
    /// </para>
    /// <para>
    /// What the migration to EF did buy here is everything around the query: no hand-managed
    /// connection, transaction or reader, the same connection string as every other context, and
    /// sixteen columns materialised by name instead of by ordinal.
    /// </para>
    /// </summary>
    private IQueryable<BedSnapshotRow> Snapshot(DateTimeOffset asOf) => db.BedSnapshot.FromSql($"""
        SELECT b.id, b.code, b.bed_number,
               h.id AS hospital_id, h.name AS hospital_name,
               f.id AS floor_id, f.name AS floor_name, f.floor_number,
               w.id AS ward_id, w.name AS ward_name,
               r.id AS room_id, r.name AS room_name, r.room_number,
               CASE
                 WHEN b.available_from > {asOf} OR b.retired_at <= {asOf} THEN 'inactive'
                 WHEN EXISTS (SELECT 1 FROM dbo.bed_stays s WHERE s.bed_id = b.id
                     AND s.started_at <= {asOf} AND (s.ended_at IS NULL OR s.ended_at > {asOf})) THEN 'occupied'
                 WHEN EXISTS (SELECT 1 FROM dbo.bed_blocks x WHERE x.bed_id = b.id
                     AND x.starts_at <= {asOf} AND (x.ends_at IS NULL OR x.ends_at > {asOf})) THEN 'blocked'
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
              AND s.started_at <= {asOf} AND (s.ended_at IS NULL OR s.ended_at > {asOf})
              AND a.cancelled_at IS NULL
            ORDER BY s.started_at DESC
        ) placement
        ORDER BY h.code, f.floor_number, w.code, r.room_number, b.bed_number
        """);
}

/// <summary>Shared by both readers, so a missing database cannot change what a bad time means.</summary>
internal static class OccupancyReads
{
    internal static Result<HospitalSnapshot> Unavailable => Result<HospitalSnapshot>.Fail(
        new Error("hospital_unavailable", "The hospital database could not be read. Check the server connection and retry."));

    /// <summary>
    /// The moment to read the hospital at. A time in the future is a bad request whether or not
    /// there is a database behind this, so it is rejected before anything is opened.
    /// </summary>
    internal static Result<DateTimeOffset> Resolve(DateTimeOffset? at, TimeProvider time)
    {
        var asOf = (at ?? time.GetUtcNow()).ToUniversalTime();
        return asOf > time.GetUtcNow()
            ? Result<DateTimeOffset>.Fail(Error.Validation("Choose a time in the past or use the current snapshot."))
            : Result<DateTimeOffset>.Ok(asOf);
    }
}

/// <summary>
/// What the occupancy view is when the API runs with no database configured. The snapshot is a
/// read of real beds or it is nothing - there is no in-memory hospital to stand in for one - so
/// this reports the read as unavailable, and still rejects an impossible time as a bad request.
/// </summary>
public sealed class UnavailableHospitalOccupancyReader(TimeProvider time) : IHospitalOccupancyReader
{
    public Task<Result<HospitalSnapshot>> ReadAsync(DateTimeOffset? at, CancellationToken ct) =>
        Task.FromResult(OccupancyReads.Resolve(at, time).Error is { } rejected
            ? Result<HospitalSnapshot>.Fail(rejected)
            : OccupancyReads.Unavailable);
}

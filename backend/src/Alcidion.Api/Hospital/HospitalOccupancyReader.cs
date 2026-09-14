using System.Data;
using Alcidion.Shared;
using Microsoft.Data.SqlClient;

namespace Alcidion.Api.Hospital;

public sealed class HospitalOccupancyReader(IConfiguration configuration, IWebHostEnvironment environment,
    TimeProvider time, ILogger<HospitalOccupancyReader> logger)
{
    public async Task<Result<HospitalSnapshot>> ReadAsync(DateTimeOffset? at, CancellationToken ct)
    {
        var asOf = (at ?? time.GetUtcNow()).ToUniversalTime();
        if (asOf > time.GetUtcNow())
            return Result<HospitalSnapshot>.Fail(Error.Validation("Choose a time in the past or use the current snapshot."));

        var connectionString = GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return Unavailable();

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            // Keep all hierarchy and interval reads consistent, even during a concurrent transfer.
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await using var command = new SqlCommand(Query, connection, transaction) { CommandTimeout = 20 };
            command.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = asOf;
            var beds = new List<HospitalBed>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    beds.Add(new HospitalBed(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2),
                        reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5), reader.GetString(6), reader.GetInt32(7),
                        reader.GetGuid(8), reader.GetString(9), reader.GetGuid(10), reader.GetString(11), reader.GetInt32(12),
                        reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetGuid(14),
                        reader.IsDBNull(15) ? null : reader.GetString(15)));
            }
            await transaction.CommitAsync(ct);
            return Result<HospitalSnapshot>.Ok(new(asOf, time.GetUtcNow(), "sql", beds));
        }
        catch (SqlException ex)
        {
            // SQL error messages can contain server/login names. Only log the numeric code.
            logger.LogWarning("Hospital occupancy SQL read failed with code {SqlErrorNumber}", ex.Number);
            return Unavailable();
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Hospital occupancy connection configuration is invalid");
            return Unavailable();
        }
    }

    private static Result<HospitalSnapshot> Unavailable() => Result<HospitalSnapshot>.Fail(
        new Error("hospital_unavailable", "The hospital database could not be read. Check the server connection and retry."));

    private string? GetConnectionString() => HospitalConnection.Resolve(configuration, environment);

    // Stays and blocks use half-open intervals [start, end). A transfer at T is counted once.
    // These are physical availability states, not a promise of staffed/clinically suitable capacity.
    internal const string Query = """
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

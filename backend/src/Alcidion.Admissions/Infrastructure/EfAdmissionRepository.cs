using Alcidion.Admissions.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alcidion.Admissions.Infrastructure;

/// <summary>
/// SQL Server implementation of <see cref="IAdmissionRepository"/>.
/// <para>
/// The schema has no ward column on an admission on purpose: where a patient is, is the bed they
/// occupy. So admitting allocates a bed and opens a stay, discharging closes it, and reading an
/// admission's ward follows its stays. That keeps one answer to "where is this patient", which the
/// hospital occupancy view and this list both read.
/// </para>
/// </summary>
public sealed class EfAdmissionRepository(AdmissionsDbContext db, ILogger<EfAdmissionRepository> logger) : IAdmissionRepository
{
    /// <summary>Shown for an admission whose bed request has not been fulfilled yet.</summary>
    private const string AwaitingBed = "Awaiting bed";

    // 2601 = duplicate key in a unique index, 2627 = unique constraint violation. Here it is
    // ux_admissions_one_open_per_patient: the patient was admitted while this insert was in flight.
    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    public async Task<Admission?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await ToDomainAsync(Open().Where(a => a.Id == id), ct)).FirstOrDefault();

    public async Task<Admission?> GetActiveForPatientAsync(Guid patientId, CancellationToken ct = default) =>
        (await ToDomainAsync(Open().Where(a => a.PatientId == patientId && a.DischargedAt == null), ct)).FirstOrDefault();

    /// <summary>
    /// Admitted episodes, newest first. An admission still waiting for its first bed is not one the
    /// Admitted/Discharged pair can describe, so it stays out of this list until it is admitted.
    /// </summary>
    public async Task<IReadOnlyList<Admission>> ListAsync(CancellationToken ct = default) =>
        await ToDomainAsync(Open().Where(a => a.AdmittedAt != null).OrderByDescending(a => a.AdmittedAt), ct);

    public async Task<TransferResult> TransferAsync(Guid admissionId, string wardText, DateTimeOffset now, CancellationToken ct = default)
    {
        var ward = await ResolveWardAsync(wardText, ct);
        if (ward is null) return new TransferResult.UnknownWard(wardText);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.admissions
            SET concurrency_version = concurrency_version + 1
            WHERE id = {admissionId} AND admitted_at IS NOT NULL AND discharged_at IS NULL AND cancelled_at IS NULL
            """, ct);
        if (locked == 0)
        {
            await transaction.RollbackAsync(ct);
            return new TransferResult.NotFound();
        }

        var requestId = Guid.NewGuid();
        var stayId = Guid.NewGuid();
        db.BedRequests.Add(new BedRequestRow
        {
            Id = requestId, AdmissionId = admissionId, TargetWardId = ward.Id,
            RequestedAt = now, FulfilledAt = now,
        });
        await db.SaveChangesAsync(ct);

        if (await AllocateBedAsync(stayId, admissionId, requestId, ward.Id, now, ct) == 0)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new TransferResult.NoBedAvailable(ward.Name, await DescribeShortageAsync(ward.Id, ct));
        }

        var closed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.bed_stays
            SET ended_at = {now}, end_reason = N'transfer', concurrency_version = concurrency_version + 1
            WHERE admission_id = {admissionId} AND ended_at IS NULL AND started_at <= {now}
            """, ct);
        if (closed == 0)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new TransferResult.NotFound();
        }

        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return new TransferResult.Transferred(await GetByIdAsync(admissionId, ct)
            ?? throw new InvalidOperationException($"Admission {admissionId} disappeared after transfer."));
    }

    /// <summary>Cancelled episodes never became an admission, so nothing outside this class sees them.</summary>
    private IQueryable<AdmissionRow> Open() => db.Admissions.AsNoTracking().Where(a => a.CancelledAt == null);

    private async Task<List<Admission>> ToDomainAsync(IQueryable<AdmissionRow> source, CancellationToken ct)
    {
        var rows = await source.Select(a => new
        {
            a.Id,
            a.PatientId,
            a.RequestedAt,
            a.AdmittedAt,
            a.DischargedAt,
            // The open stay if there is one, otherwise the most recent closed stay: a discharged
            // admission should still say which ward the patient left.
            StayWard = (from s in db.BedStays
                        from b in db.Beds.Where(b => b.Id == s.BedId)
                        from w in db.Wards.Where(w => w.Id == b.WardId)
                        where s.AdmissionId == a.Id
                        orderby s.EndedAt == null ? 0 : 1, s.StartedAt descending
                        select w.Name).FirstOrDefault(),
            RequestedWard = (from r in db.BedRequests
                             from w in db.Wards.Where(w => w.Id == r.TargetWardId)
                             where r.AdmissionId == a.Id && r.CancelledAt == null
                             orderby r.RequestedAt descending
                             select w.Name).FirstOrDefault(),
        }).ToListAsync(ct);

        return rows.Select(r => Admission.Rehydrate(
            r.Id, r.PatientId,
            r.StayWard ?? r.RequestedWard ?? AwaitingBed,
            r.AdmittedAt ?? r.RequestedAt,
            r.DischargedAt)).ToList();
    }

    public async Task<AdmitResult> TryAddActiveAsync(Admission admission, CancellationToken ct = default)
    {
        var ward = await ResolveWardAsync(admission.Ward, ct);
        if (ward is null) return new AdmitResult.UnknownWard(admission.Ward);

        var now = admission.AdmittedAt;
        var requestId = Guid.NewGuid();
        var stayId = Guid.NewGuid();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.Admissions.Add(new AdmissionRow
        {
            Id = admission.Id,
            PatientId = admission.PatientId,
            RequestedAt = now,
            AdmittedAt = now,
        });
        db.BedRequests.Add(new BedRequestRow
        {
            Id = requestId,
            AdmissionId = admission.Id,
            TargetWardId = ward.Id,
            RequestedAt = now,
            FulfilledAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var existing = await GetActiveForPatientAsync(admission.PatientId, ct);
            // The open admission is gone again already (discharged between the insert and this
            // read), so report the collision rather than inventing a ward it is not in.
            return existing is null
                ? new AdmitResult.NoBedAvailable(ward.Name, "the patient's admission changed while this request was in flight")
                : new AdmitResult.AlreadyActive(existing);
        }

        var allocated = await AllocateBedAsync(stayId, admission.Id, requestId, ward.Id, now, ct);
        if (allocated == 0)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new AdmitResult.NoBedAvailable(ward.Name, await DescribeShortageAsync(ward.Id, ct));
        }

        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        logger.LogInformation("Allocated a bed in ward {WardCode} for admission {AdmissionId}", ward.Code, admission.Id);

        // Re-read so the caller gets the ward the bed is actually in, not the text that was typed.
        return new AdmitResult.Admitted(await GetByIdAsync(admission.Id, ct) ?? admission);
    }

    public async Task<bool> UpdateAsync(Admission admission, CancellationToken ct = default)
    {
        if (admission.DischargedAt is not { } dischargedAt) return true; // nothing else is mutable yet

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // The predicate is the concurrency check: a second discharge matches no rows.
        var closed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.admissions
            SET discharged_at = {dischargedAt}, concurrency_version = concurrency_version + 1
            WHERE id = {admission.Id}
              AND admitted_at IS NOT NULL
              AND discharged_at IS NULL
              AND cancelled_at IS NULL
            """, ct);

        if (closed == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        // Releasing the bed is part of discharging, not a follow-up someone might forget: the bed
        // has to be allocatable again the moment the admission is closed.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.bed_stays
            SET ended_at = {dischargedAt}, end_reason = N'discharge', concurrency_version = concurrency_version + 1
            WHERE admission_id = {admission.Id} AND ended_at IS NULL AND started_at <= {dischargedAt}
            """, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.bed_requests
            SET cancelled_at = {dischargedAt}, concurrency_version = concurrency_version + 1
            WHERE admission_id = {admission.Id} AND fulfilled_at IS NULL AND cancelled_at IS NULL
            """, ct);

        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Claims one bed in the ward, as a single statement so the choice and the claim cannot be
    /// separated. UPDLOCK holds the chosen bed for this transaction and READPAST steps over a bed
    /// another allocation is already taking, instead of queueing behind it. Returns rows inserted:
    /// 0 means nothing was free within the ward's staffed limit.
    /// </summary>
    private Task<int> AllocateBedAsync(Guid stayId, Guid admissionId, Guid requestId, Guid wardId, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.bed_stays (id, admission_id, bed_id, bed_request_id, started_at, concurrency_version)
            SELECT {stayId}, {admissionId}, free.id, {requestId}, {now}, 0
            FROM (
                SELECT TOP (1) b.id
                FROM dbo.beds b WITH (UPDLOCK, ROWLOCK, READPAST)
                WHERE b.ward_id = {wardId}
                  AND b.available_from <= {now}
                  AND (b.retired_at IS NULL OR b.retired_at > {now})
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.bed_stays s
                      WHERE s.bed_id = b.id AND s.started_at <= {now} AND (s.ended_at IS NULL OR s.ended_at > {now}))
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.bed_blocks x
                      WHERE x.bed_id = b.id AND x.starts_at <= {now} AND (x.ends_at IS NULL OR x.ends_at > {now}))
                ORDER BY b.code
            ) AS free
            WHERE (
                SELECT COUNT(*)
                FROM dbo.bed_stays s2
                JOIN dbo.beds b2 ON b2.id = s2.bed_id
                WHERE b2.ward_id = {wardId} AND s2.started_at <= {now} AND (s2.ended_at IS NULL OR s2.ended_at > {now})
            ) < COALESCE((
                SELECT TOP (1) cp.staffed_bed_limit
                FROM dbo.ward_capacity_periods cp
                WHERE cp.ward_id = {wardId} AND cp.starts_at <= {now} AND (cp.ends_at IS NULL OR cp.ends_at > {now})
                ORDER BY cp.starts_at DESC
            ), 2147483647)
            """, ct);

    /// <summary>
    /// Why the allocation found nothing. Free beds and staffed capacity fail the same way but mean
    /// different things - a ward can be half empty and still have no one rostered to staff another bed.
    /// </summary>
    private async Task<string> DescribeShortageAsync(Guid wardId, CancellationToken ct)
    {
        var ward = (await WardCapacityAsync(ct, wardId)).SingleOrDefault();
        if (ward is null) return "the ward has no beds";
        return ward.OccupiedBeds >= ward.EffectiveCapacity
            ? $"{ward.OccupiedBeds} of {ward.EffectiveCapacity} staffed beds are in use"
            : "every bed is occupied, blocked or out of service";
    }

    /// <summary>Ward-by-ward capacity as of now. Shared by the shortage message and the ward directory.</summary>
    internal async Task<IReadOnlyList<WardSummary>> WardCapacityAsync(CancellationToken ct, Guid? onlyWardId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var query = db.Wards.AsNoTracking();
        if (onlyWardId is { } id) query = query.Where(w => w.Id == id);

        var rows = await query.Select(w => new
        {
            w.Id,
            w.Code,
            w.Name,
            w.WardType,
            Beds = db.Beds.Count(b => b.WardId == w.Id),
            // Usable: in service now, and not closed for cleaning or maintenance.
            Usable = db.Beds.Count(b => b.WardId == w.Id
                && b.AvailableFrom <= now
                && (b.RetiredAt == null || b.RetiredAt > now)
                && !db.BedBlocks.Any(x => x.BedId == b.Id && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now))),
            Occupied = db.BedStays.Count(s => s.StartedAt <= now && (s.EndedAt == null || s.EndedAt > now)
                && db.Beds.Any(b => b.Id == s.BedId && b.WardId == w.Id)),
            StaffedLimit = db.WardCapacityPeriods
                .Where(c => c.WardId == w.Id && c.StartsAt <= now && (c.EndsAt == null || c.EndsAt > now))
                .OrderByDescending(c => c.StartsAt)
                .Select(c => (int?)c.StaffedBedLimit)
                .FirstOrDefault(),
        }).ToListAsync(ct);

        return rows.Select(r =>
        {
            // Effective capacity is the lower of the beds that exist and the beds anyone is
            // rostered to look after. Both limits bind; the tighter one decides.
            var effective = Math.Min(r.Usable, r.StaffedLimit ?? r.Usable);
            return new WardSummary(r.Id, r.Code, r.Name, r.WardType, r.Beds, r.Occupied, effective,
                Math.Max(0, effective - r.Occupied));
        })
        .OrderBy(w => w.Code, StringComparer.Ordinal)
        .ToList();
    }

    /// <summary>
    /// Matches what was asked for against a ward's code, name or type, in that order of confidence.
    /// "F01-W03", "Intensive Care Unit" and "ICU" all name the same place; where a type matches
    /// several wards the one with the most room wins, so the request is not sent to a full ward.
    /// </summary>
    private async Task<WardRow?> ResolveWardAsync(string wardText, CancellationToken ct)
    {
        var wanted = wardText.Trim();
        if (wanted.Length == 0) return null;

        var candidates = await db.Wards.AsNoTracking()
            .Where(w => w.Code == wanted || w.Name == wanted || w.WardType == wanted)
            .ToListAsync(ct);
        if (candidates.Count is 0) return null;
        if (candidates.Count is 1) return candidates[0];

        var byConfidence = candidates
            .OrderBy(w => Same(w.Code, wanted) ? 0 : Same(w.Name, wanted) ? 1 : 2)
            .ToList();
        // Only a ward-type match can be ambiguous; a code or a name identifies exactly one ward.
        if (!Same(byConfidence[0].WardType, wanted)) return byConfidence[0];

        var free = (await WardCapacityAsync(ct)).ToDictionary(w => w.Id, w => w.FreeBeds);
        return byConfidence.OrderByDescending(w => free.GetValueOrDefault(w.Id)).First();

        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

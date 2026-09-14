using Alcidion.Patients.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Alcidion.Patients.Infrastructure;

/// <summary>
/// SQL Server implementation of <see cref="IPatientRepository"/>. The in-memory repository's MRN
/// index is a stand-in for <c>ux_patients_mrn</c>; here the index does the job for real, and a
/// duplicate surfaces as a unique-violation on the insert rather than a check that raced.
/// </summary>
public sealed class EfPatientRepository(PatientsDbContext db) : IPatientRepository
{
    // 2601 = duplicate key in a unique index, 2627 = unique constraint violation. Both shapes are
    // matched because the two writes reach the server differently: SaveChanges wraps the provider
    // exception, ExecuteUpdate runs its own command and lets it through.
    private static bool IsDuplicateKey(Exception ex) => ex switch
    {
        SqlException { Number: 2601 or 2627 } => true,
        DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => true,
        _ => false,
    };

    public async Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct))?.ToDomain();

    public async Task<Patient?> GetByMrnAsync(string mrn, CancellationToken ct = default)
    {
        // Compared against the same normalized form the aggregate stores, so " mrn-1 " finds MRN-1.
        var normalized = Patient.NormalizeMrn(mrn);
        return (await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Mrn == normalized, ct))?.ToDomain();
    }

    /// <summary>Newest first: the register form is at the top of the page, and so is what it just added.</summary>
    public async Task<IReadOnlyList<Patient>> ListAsync(CancellationToken ct = default)
    {
        // Materialize first: the mapping back to the aggregate is C#, not something to translate.
        var rows = await db.Patients.AsNoTracking()
            .OrderByDescending(p => p.RegisteredAt).ThenBy(p => p.Mrn)
            .ToListAsync(ct);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<Patient>> SearchAsync(string query, CancellationToken ct = default)
    {
        var wanted = query.Trim();
        var rows = await db.Patients.AsNoTracking()
            .Where(p => p.Mrn.Contains(wanted) || p.GivenName.Contains(wanted) || p.FamilyName.Contains(wanted))
            .OrderBy(p => p.FamilyName).ThenBy(p => p.GivenName).ThenBy(p => p.Mrn)
            .Take(50)
            .ToListAsync(ct);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<bool> TryAddAsync(Patient patient, CancellationToken ct = default)
    {
        db.Patients.Add(PatientRow.From(patient));
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // The MRN was taken between this request starting and its insert landing.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>
    /// One conditional UPDATE decides everything: whether the patient is still at the version the
    /// correction was taken against, and - through <c>ux_patients_mrn</c> - whether the corrected MRN
    /// is free. Reading either first and writing after would be the check-then-write this repository
    /// exists to avoid.
    /// </summary>
    public async Task<CorrectionResult> TryCorrectAsync(Patient corrected, long expectedVersion, CancellationToken ct = default)
    {
        int updated;
        try
        {
            updated = await db.Patients
                .Where(p => p.Id == corrected.Id && p.ConcurrencyVersion == expectedVersion)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.Mrn, corrected.Mrn)
                    .SetProperty(p => p.GivenName, corrected.GivenName)
                    .SetProperty(p => p.FamilyName, corrected.FamilyName)
                    .SetProperty(p => p.DateOfBirth, corrected.DateOfBirth)
                    .SetProperty(p => p.ConcurrencyVersion, p => p.ConcurrencyVersion + 1), ct);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            return new CorrectionResult.MrnTaken(corrected.Mrn);
        }

        if (updated == 1)
        {
            corrected.Committed();
            return new CorrectionResult.Corrected(corrected);
        }

        // Nothing matched. Which of the two reasons it was is read back rather than assumed: a
        // caller that is simply behind deserves the version to retry at, and one whose patient is
        // gone deserves to be told that instead.
        var current = await db.Patients.AsNoTracking()
            .Where(p => p.Id == corrected.Id)
            .Select(p => (long?)p.ConcurrencyVersion)
            .FirstOrDefaultAsync(ct);

        return current is { } version
            ? new CorrectionResult.VersionMismatch(version)
            : new CorrectionResult.NotFound();
    }
}

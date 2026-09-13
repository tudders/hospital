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
    // 2601 = duplicate key in a unique index, 2627 = unique constraint violation.
    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

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
}

using Alcidion.Admissions.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alcidion.Admissions.Infrastructure;

/// <summary>
/// Admissions' read model of patients, read from the patient rows rather than copied into a second
/// table. The PatientRegistered event still marks the seam - Admissions never calls Patients - but
/// once both contexts persist to one database there is nothing for the handler to carry across it,
/// and a copy would only add a way for the two to disagree.
/// </summary>
public sealed class EfKnownPatients(AdmissionsDbContext db) : IKnownPatients
{
    public async Task<KnownPatient?> FindAsync(Guid patientId, CancellationToken ct = default) =>
        await db.KnownPatients.AsNoTracking()
            .Where(p => p.Id == patientId)
            .Select(p => new KnownPatient(p.Id, p.Mrn, p.GivenName + " " + p.FamilyName))
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Nothing to do: the row this projects is written by the Patients context in the same
    /// database, and was committed before the event that triggers this was published.
    /// </summary>
    public Task UpsertAsync(KnownPatient patient, CancellationToken ct = default) => Task.CompletedTask;
}

using Alcidion.Patients.Domain;
using Alcidion.Patients.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alcidion.Sql.Tests;

/// <summary>
/// The patient repository against the real schema. Corrections are the reason this file exists:
/// <c>ux_patients_mrn</c> and <c>concurrency_version</c> are what make a correction safe, and neither
/// can be stood in for. Every assertion reads the table back with plain SQL rather than through the
/// context that wrote it, so a mapping that agrees with itself and with nothing else cannot pass.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PatientRepositoryTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A repository on its own context, as the request scope gives it. Each call gets a fresh one so
    /// a test cannot pass on state the change tracker happened to be holding.
    /// </summary>
    private EfPatientRepository Repository() => new(
        new PatientsDbContext(new DbContextOptionsBuilder<PatientsDbContext>()
            .UseSqlServer(sql.ConnectionString).Options));

    private static Patient NewPatient(string? mrn = null) =>
        Patient.Register(mrn ?? $"MRN{Guid.NewGuid():N}"[..16], "Ada", "Lovelace", new DateOnly(1990, 1, 1), Now);

    private async Task<Patient> StoredAsync(string? mrn = null)
    {
        var patient = NewPatient(mrn);
        Assert.True(await Repository().TryAddAsync(patient));
        return patient;
    }

    [SqlFact]
    public async Task A_registered_patient_lands_at_version_zero()
    {
        // Migration 004's column default. The aggregate says a fresh patient is version 0; if the
        // row disagreed, the first correction would be taken against a version nothing ever held.
        var patient = await StoredAsync();

        Assert.Equal(0L, await VersionAsync(patient.Id));
        Assert.Equal(0, (await Repository().GetByIdAsync(patient.Id))!.Version);
    }

    [SqlFact]
    public async Task A_correction_writes_the_named_columns_and_bumps_the_version()
    {
        var patient = await StoredAsync();
        var corrected = (await Repository().GetByIdAsync(patient.Id))!;
        corrected.Correct(null, "Augusta", "King-Noel", null, Now);

        var result = await Repository().TryCorrectAsync(corrected, expectedVersion: 0);

        Assert.IsType<CorrectionResult.Corrected>(result);
        var row = Assert.Single(await sql.RowsAsync(
            "SELECT mrn, given_name, family_name, date_of_birth, concurrency_version FROM dbo.patients WHERE id = @id",
            ("@id", patient.Id)));
        Assert.Equal(patient.Mrn, row[0]);
        Assert.Equal("Augusta", row[1]);
        Assert.Equal("King-Noel", row[2]);
        Assert.Equal(new DateTime(1990, 1, 1), row[3]);
        Assert.Equal(1L, row[4]);
        Assert.Equal(1, ((CorrectionResult.Corrected)result).Patient.Version);
    }

    /// <summary>
    /// The reason the column exists. Two clerks read the same patient and each fix a different
    /// field; without the version in the WHERE clause the second write lands on top of the first and
    /// the earlier correction is gone with nothing to say it happened.
    /// </summary>
    [SqlFact]
    public async Task A_correction_taken_against_a_stale_version_loses_rather_than_overwriting()
    {
        var patient = await StoredAsync();

        var first = (await Repository().GetByIdAsync(patient.Id))!;
        var second = (await Repository().GetByIdAsync(patient.Id))!;
        Assert.Equal(0, second.Version);

        first.Correct(null, "Augusta", null, null, Now);
        Assert.IsType<CorrectionResult.Corrected>(await Repository().TryCorrectAsync(first, expectedVersion: 0));

        second.Correct(null, null, "King-Noel", null, Now);
        var result = await Repository().TryCorrectAsync(second, expectedVersion: 0);

        var mismatch = Assert.IsType<CorrectionResult.VersionMismatch>(result);
        Assert.Equal(1, mismatch.CurrentVersion);

        // The loser wrote nothing at all - not its own field, and not the winner's back to what it was.
        var row = Assert.Single(await sql.RowsAsync(
            "SELECT given_name, family_name, concurrency_version FROM dbo.patients WHERE id = @id",
            ("@id", patient.Id)));
        Assert.Equal("Augusta", row[0]);
        Assert.Equal("Lovelace", row[1]);
        Assert.Equal(1L, row[2]);
    }

    [SqlFact]
    public async Task Correcting_an_mrn_onto_one_another_patient_holds_is_refused_by_the_index()
    {
        var taken = await StoredAsync();
        var patient = await StoredAsync();

        var corrected = (await Repository().GetByIdAsync(patient.Id))!;
        corrected.Correct(taken.Mrn, null, null, null, Now);

        var result = await Repository().TryCorrectAsync(corrected, expectedVersion: 0);

        // ux_patients_mrn decides this, not a read the correction did first: a check followed by an
        // update would let two callers both pass it and one of them violate the index anyway.
        Assert.Equal(taken.Mrn, Assert.IsType<CorrectionResult.MrnTaken>(result).Mrn);
        Assert.Equal(patient.Mrn, await sql.ScalarAsync<string>(
            "SELECT mrn FROM dbo.patients WHERE id = @id", ("@id", patient.Id)));
        Assert.Equal(0L, await VersionAsync(patient.Id));
    }

    [SqlFact]
    public async Task A_corrected_mrn_frees_the_old_one_for_the_patient_it_belonged_to()
    {
        var patient = await StoredAsync(mrn: $"TYPO{Guid.NewGuid():N}"[..16]);
        var typo = patient.Mrn;

        var corrected = (await Repository().GetByIdAsync(patient.Id))!;
        corrected.Correct($"RIGHT{Guid.NewGuid():N}"[..16], null, null, null, Now);
        Assert.IsType<CorrectionResult.Corrected>(await Repository().TryCorrectAsync(corrected, expectedVersion: 0));

        // The typo was never anyone's real MRN, so the patient it was meant for must still be able
        // to be registered under it.
        Assert.True(await Repository().TryAddAsync(NewPatient(typo)));
    }

    [SqlFact]
    public async Task Correcting_a_patient_that_is_gone_is_not_a_version_mismatch()
    {
        var absent = NewPatient();
        absent.Correct(null, "Augusta", null, null, Now);

        // Both reach the repository as "the UPDATE matched nothing". Telling them apart is the
        // difference between a caller who should re-read and one who should stop trying.
        Assert.IsType<CorrectionResult.NotFound>(await Repository().TryCorrectAsync(absent, expectedVersion: 0));
    }

    [SqlFact]
    public async Task A_corrected_mrn_still_satisfies_the_schemas_own_normalisation_check()
    {
        // ck_patients_mrn requires the stored MRN to equal UPPER(LTRIM(RTRIM(mrn))). The aggregate
        // normalises on the way in; a correction that skipped that would be refused by the database
        // rather than by a rule anyone can read.
        var patient = await StoredAsync();
        var corrected = (await Repository().GetByIdAsync(patient.Id))!;
        corrected.Correct($"  fix{Guid.NewGuid():N}  "[..16].Trim(), null, null, null, Now);

        Assert.IsType<CorrectionResult.Corrected>(await Repository().TryCorrectAsync(corrected, expectedVersion: 0));
        Assert.Equal(corrected.Mrn, await sql.ScalarAsync<string>(
            "SELECT mrn FROM dbo.patients WHERE id = @id", ("@id", patient.Id)));
    }

    private Task<long> VersionAsync(Guid id) => sql.ScalarAsync<long>(
        "SELECT concurrency_version FROM dbo.patients WHERE id = @id", ("@id", id))!;
}

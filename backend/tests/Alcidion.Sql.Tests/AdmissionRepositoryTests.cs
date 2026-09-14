using Alcidion.Admissions.Domain;
using Alcidion.Admissions.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alcidion.Sql.Tests;

/// <summary>
/// The repository against the real schema. Every assertion reads the tables back with plain SQL
/// rather than through the context that wrote them, so a mapping that agrees with itself and with
/// nothing else cannot pass: these tests fail when the rows are wrong, not when the LINQ changes.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AdmissionRepositoryTests(SqlServerFixture sql)
{
    private readonly HospitalSeed _seed = new(sql);
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A repository on its own context, as the request scope gives it. Each call gets a fresh one so
    /// a test cannot pass on state the change tracker happened to be holding.
    /// </summary>
    private EfAdmissionRepository Repository() => new(
        new AdmissionsDbContext(new DbContextOptionsBuilder<AdmissionsDbContext>()
            .UseSqlServer(sql.ConnectionString).Options),
        NullLogger<EfAdmissionRepository>.Instance);

    // --- Admitting ---------------------------------------------------------

    [SqlFact]
    public async Task Admitting_claims_a_bed_in_the_ward_and_opens_a_stay()
    {
        var ward = await _seed.WardAsync(beds: 2);
        var patient = await _seed.PatientAsync();

        var result = await Repository().TryAddActiveAsync(Admission.Admit(patient, ward.Code, Now));

        var admitted = Assert.IsType<AdmitResult.Admitted>(result);
        Assert.Equal(ward.Name, admitted.Admission.Ward);
        Assert.Equal(AdmissionStatus.Admitted, admitted.Admission.Status);

        var stay = await sql.RowsAsync("""
            SELECT s.bed_id, s.ended_at, s.end_reason, r.fulfilled_at
            FROM dbo.bed_stays s
            JOIN dbo.bed_requests r ON r.id = s.bed_request_id
            WHERE s.admission_id = @id
            """, ("@id", admitted.Admission.Id));

        var row = Assert.Single(stay);
        Assert.Contains((Guid)row[0]!, ward.Beds);
        Assert.Null(row[1]);                 // the stay is open
        Assert.Null(row[2]);
        Assert.NotNull(row[3]);              // the request it fulfilled is marked fulfilled
    }

    [SqlFact]
    public async Task A_ward_can_be_named_by_its_code_its_name_or_its_type()
    {
        var ward = await _seed.WardAsync(beds: 3, wardType: "type" + Guid.NewGuid().ToString("N")[..8]);

        foreach (var text in new[] { ward.Code, ward.Name, ward.WardType })
        {
            var patient = await _seed.PatientAsync();
            var result = await Repository().TryAddActiveAsync(Admission.Admit(patient, text, Now));
            Assert.Equal(ward.Name, Assert.IsType<AdmitResult.Admitted>(result).Admission.Ward);
        }
    }

    [SqlFact]
    public async Task Ward_text_that_matches_nothing_is_reported_not_invented()
    {
        var patient = await _seed.PatientAsync();

        var result = await Repository().TryAddActiveAsync(Admission.Admit(patient, "Ward Nineteen", Now));

        Assert.Equal("Ward Nineteen", Assert.IsType<AdmitResult.UnknownWard>(result).Ward);
        Assert.Equal(0, await sql.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.admissions WHERE patient_id = @p", ("@p", patient)));
    }

    [SqlFact]
    public async Task A_patient_who_is_already_admitted_is_not_admitted_twice()
    {
        var ward = await _seed.WardAsync(beds: 4);
        var patient = await _seed.PatientAsync();
        await Repository().TryAddActiveAsync(Admission.Admit(patient, ward.Code, Now));

        var again = await Repository().TryAddActiveAsync(Admission.Admit(patient, ward.Code, Now));

        var existing = Assert.IsType<AdmitResult.AlreadyActive>(again).Existing;
        Assert.Equal(ward.Name, existing.Ward);
        Assert.Equal(1, await sql.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.admissions WHERE patient_id = @p", ("@p", patient)));
    }

    [SqlFact]
    public async Task A_ward_whose_beds_are_all_taken_refuses_the_admission()
    {
        var ward = await _seed.WardAsync(beds: 1);
        await Repository().TryAddActiveAsync(Admission.Admit(await _seed.PatientAsync(), ward.Code, Now));
        var second = await _seed.PatientAsync();

        var result = await Repository().TryAddActiveAsync(Admission.Admit(second, ward.Code, Now));

        var refused = Assert.IsType<AdmitResult.NoBedAvailable>(result);
        Assert.Equal(ward.Name, refused.Ward);
        // The whole attempt is rolled back, so the refused patient holds no admission row.
        Assert.Equal(0, await sql.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.admissions WHERE patient_id = @p", ("@p", second)));
    }

    [SqlFact]
    public async Task A_half_empty_ward_with_no_one_rostered_is_full()
    {
        var ward = await _seed.WardAsync(beds: 4, staffedLimit: 1);
        await Repository().TryAddActiveAsync(Admission.Admit(await _seed.PatientAsync(), ward.Code, Now));

        var result = await Repository().TryAddActiveAsync(
            Admission.Admit(await _seed.PatientAsync(), ward.Code, Now));

        var refused = Assert.IsType<AdmitResult.NoBedAvailable>(result);
        Assert.Contains("staffed beds are in use", refused.Reason);
    }

    [SqlFact]
    public async Task A_blocked_bed_is_not_allocated()
    {
        var ward = await _seed.WardAsync(beds: 2);
        await _seed.BlockBedAsync(ward.Beds[0], Now.AddHours(-1));
        await _seed.BlockBedAsync(ward.Beds[1], Now.AddHours(-1));

        var result = await Repository().TryAddActiveAsync(
            Admission.Admit(await _seed.PatientAsync(), ward.Code, Now));

        Assert.Equal(ward.Name, Assert.IsType<AdmitResult.NoBedAvailable>(result).Ward);
    }

    [SqlFact]
    public async Task A_bed_that_is_not_in_service_yet_is_not_allocated()
    {
        var ward = await _seed.WardAsync(beds: 1, availableFrom: Now.AddDays(7));

        var result = await Repository().TryAddActiveAsync(
            Admission.Admit(await _seed.PatientAsync(), ward.Code, Now));

        Assert.IsType<AdmitResult.NoBedAvailable>(result);
    }

    // --- Transferring ------------------------------------------------------

    [SqlFact]
    public async Task Transferring_opens_a_stay_in_the_new_ward_and_closes_the_old_one()
    {
        var from = await _seed.WardAsync(beds: 1);
        var to = await _seed.WardAsync(beds: 1);
        var patient = await _seed.PatientAsync();
        var admitted = await AdmitAsync(patient, from.Code, Now);

        var result = await Repository().TransferAsync(admitted.Id, to.Code, Now.AddHours(2));

        Assert.Equal(to.Name, Assert.IsType<TransferResult.Transferred>(result).Admission.Ward);

        var stays = await sql.RowsAsync("""
            SELECT b.ward_id, s.ended_at, s.end_reason
            FROM dbo.bed_stays s JOIN dbo.beds b ON b.id = s.bed_id
            WHERE s.admission_id = @id ORDER BY s.started_at
            """, ("@id", admitted.Id));

        Assert.Equal(2, stays.Count);
        Assert.Equal(from.WardId, stays[0][0]);
        Assert.NotNull(stays[0][1]);
        Assert.Equal("transfer", stays[0][2]);
        Assert.Equal(to.WardId, stays[1][0]);
        Assert.Null(stays[1][1]);            // the new stay is open
    }

    [SqlFact]
    public async Task Transferring_bumps_the_admission_concurrency_version()
    {
        var from = await _seed.WardAsync(beds: 1);
        var to = await _seed.WardAsync(beds: 1);
        var admitted = await AdmitAsync(await _seed.PatientAsync(), from.Code, Now);

        await Repository().TransferAsync(admitted.Id, to.Code, Now.AddHours(2));

        Assert.Equal(1L, await sql.ScalarAsync<long>(
            "SELECT concurrency_version FROM dbo.admissions WHERE id = @id", ("@id", admitted.Id)));
    }

    [SqlFact]
    public async Task Transferring_an_admission_that_is_not_open_is_not_found()
    {
        var ward = await _seed.WardAsync(beds: 1);

        var result = await Repository().TransferAsync(Guid.NewGuid(), ward.Code, Now);

        Assert.IsType<TransferResult.NotFound>(result);
    }

    [SqlFact]
    public async Task Transferring_to_an_unknown_ward_leaves_the_patient_where_they_are()
    {
        var from = await _seed.WardAsync(beds: 1);
        var admitted = await AdmitAsync(await _seed.PatientAsync(), from.Code, Now);

        var result = await Repository().TransferAsync(admitted.Id, "Nowhere Ward", Now.AddHours(1));

        Assert.IsType<TransferResult.UnknownWard>(result);
        Assert.Equal(from.Name, (await Repository().GetByIdAsync(admitted.Id))!.Ward);
    }

    [SqlFact]
    public async Task A_transfer_into_a_full_ward_is_rolled_back_completely()
    {
        var from = await _seed.WardAsync(beds: 1);
        var full = await _seed.WardAsync(beds: 1);
        await Repository().TryAddActiveAsync(Admission.Admit(await _seed.PatientAsync(), full.Code, Now));
        var admitted = await AdmitAsync(await _seed.PatientAsync(), from.Code, Now);

        var result = await Repository().TransferAsync(admitted.Id, full.Code, Now.AddHours(1));

        Assert.Equal(full.Name, Assert.IsType<TransferResult.NoBedAvailable>(result).Ward);
        // The lock bump, the new bed request and the closing of the old stay all go back.
        Assert.Equal(from.Name, (await Repository().GetByIdAsync(admitted.Id))!.Ward);
        Assert.Equal(0L, await sql.ScalarAsync<long>(
            "SELECT concurrency_version FROM dbo.admissions WHERE id = @id", ("@id", admitted.Id)));
        Assert.Equal(1, await sql.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.bed_requests WHERE admission_id = @id", ("@id", admitted.Id)));
        Assert.Equal(1, await sql.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.bed_stays WHERE admission_id = @id AND ended_at IS NULL",
            ("@id", admitted.Id)));
    }

    // --- Discharging -------------------------------------------------------

    [SqlFact]
    public async Task Discharging_closes_the_admission_releases_the_bed_and_cancels_what_is_pending()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var admitted = await AdmitAsync(await _seed.PatientAsync(), ward.Code, Now);
        // A request nobody fulfilled: the queue entry a discharge has to clear.
        await sql.ExecuteAsync("""
            INSERT INTO dbo.bed_requests (id, admission_id, target_ward_id, requested_at)
            VALUES (@id, @admission, @ward, @at);
            """, ("@id", Guid.NewGuid()), ("@admission", admitted.Id), ("@ward", ward.WardId), ("@at", Now));

        admitted.Discharge(Now.AddDays(1));
        Assert.True(await Repository().UpdateAsync(admitted));

        Assert.NotNull(await sql.ScalarAsync<DateTimeOffset?>(
            "SELECT discharged_at FROM dbo.admissions WHERE id = @id", ("@id", admitted.Id)));
        Assert.Equal(1L, await sql.ScalarAsync<long>(
            "SELECT concurrency_version FROM dbo.admissions WHERE id = @id", ("@id", admitted.Id)));
        Assert.Equal("discharge", await sql.ScalarAsync<string>(
            "SELECT end_reason FROM dbo.bed_stays WHERE admission_id = @id", ("@id", admitted.Id)));
        Assert.Equal(0, await sql.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.bed_stays WHERE admission_id = @id AND ended_at IS NULL",
            ("@id", admitted.Id)));
        Assert.Equal(1, await sql.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.bed_requests WHERE admission_id = @id AND cancelled_at IS NOT NULL",
            ("@id", admitted.Id)));
    }

    [SqlFact]
    public async Task The_released_bed_can_be_allocated_again_immediately()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var admitted = await AdmitAsync(await _seed.PatientAsync(), ward.Code, Now);
        admitted.Discharge(Now.AddHours(1));
        await Repository().UpdateAsync(admitted);

        var next = await Repository().TryAddActiveAsync(
            Admission.Admit(await _seed.PatientAsync(), ward.Code, Now.AddHours(2)));

        Assert.IsType<AdmitResult.Admitted>(next);
    }

    [SqlFact]
    public async Task Discharging_twice_reports_the_lost_race_rather_than_doing_nothing()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var first = await AdmitAsync(await _seed.PatientAsync(), ward.Code, Now);
        var stale = Admission.Rehydrate(first.Id, first.PatientId, ward.Name, Now, null);
        first.Discharge(Now.AddHours(1));
        stale.Discharge(Now.AddHours(1));

        Assert.True(await Repository().UpdateAsync(first));
        Assert.False(await Repository().UpdateAsync(stale));

        Assert.Equal(1L, await sql.ScalarAsync<long>(
            "SELECT concurrency_version FROM dbo.admissions WHERE id = @id", ("@id", first.Id)));
    }

    [SqlFact]
    public async Task An_admission_with_nothing_to_write_is_left_alone()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var admitted = await AdmitAsync(await _seed.PatientAsync(), ward.Code, Now);

        Assert.True(await Repository().UpdateAsync(admitted));

        Assert.Equal(0L, await sql.ScalarAsync<long>(
            "SELECT concurrency_version FROM dbo.admissions WHERE id = @id", ("@id", admitted.Id)));
    }

    // --- Reading -----------------------------------------------------------

    [SqlFact]
    public async Task A_discharged_admission_still_names_the_ward_it_left()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var admitted = await AdmitAsync(await _seed.PatientAsync(), ward.Code, Now);
        admitted.Discharge(Now.AddHours(6));
        await Repository().UpdateAsync(admitted);

        var stored = await Repository().GetByIdAsync(admitted.Id);

        Assert.Equal(ward.Name, stored!.Ward);
        Assert.Equal(AdmissionStatus.Discharged, stored.Status);
    }

    [SqlFact]
    public async Task An_admission_whose_bed_request_is_unfulfilled_names_the_ward_it_is_waiting_for()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var patient = await _seed.PatientAsync();
        var waiting = await _seed.AwaitingBedAsync(patient, ward.WardId, Now);

        var stored = await Repository().GetByIdAsync(waiting);

        // There is no stay yet, so the ward is the one that was asked for, not one anyone is in.
        Assert.Equal(ward.Name, stored!.Ward);
        // Admitted/Discharged cannot describe it, so it is not in the admitted list.
        Assert.DoesNotContain(await Repository().ListAsync(), a => a.Id == waiting);
        Assert.Equal(waiting, (await Repository().GetActiveForPatientAsync(patient))!.Id);
    }

    [SqlFact]
    public async Task An_admission_with_no_stay_and_no_open_request_reads_as_awaiting_a_bed()
    {
        var ward = await _seed.WardAsync(beds: 1);
        var patient = await _seed.PatientAsync();
        var waiting = await _seed.AwaitingBedAsync(patient, ward.WardId, Now);
        await sql.ExecuteAsync("UPDATE dbo.bed_requests SET cancelled_at = @at WHERE admission_id = @id",
            ("@at", Now), ("@id", waiting));

        Assert.Equal("Awaiting bed", (await Repository().GetByIdAsync(waiting))!.Ward);
    }

    [SqlFact]
    public async Task Ward_capacity_counts_beds_occupancy_and_the_staffed_limit()
    {
        var ward = await _seed.WardAsync(beds: 4, staffedLimit: 2);
        await Repository().TryAddActiveAsync(Admission.Admit(await _seed.PatientAsync(), ward.Code, Now));

        var wards = await new EfWardDirectory(Repository()).ListAsync();

        var summary = Assert.Single(wards, w => w.Id == ward.WardId);
        Assert.Equal(4, summary.Beds);
        Assert.Equal(1, summary.OccupiedBeds);
        Assert.Equal(2, summary.EffectiveCapacity);   // the staffed limit binds, not the four beds
        Assert.Equal(1, summary.FreeBeds);
    }

    private async Task<Admission> AdmitAsync(Guid patient, string ward, DateTimeOffset at) =>
        Assert.IsType<AdmitResult.Admitted>(
            await Repository().TryAddActiveAsync(Admission.Admit(patient, ward, at))).Admission;
}

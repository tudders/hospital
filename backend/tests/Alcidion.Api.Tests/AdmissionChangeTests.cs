using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Alcidion.Api.Tests;

/// <summary>
/// The conditional-change contract on an admission. A transfer and a discharge both rewrite where a
/// patient is, and both are reachable twice - a retried request, a replaying proxy, two clinicians
/// on the same board - so the version the change was decided against travels with it and the second
/// arrival is refused. These tests are the HTTP half of that; the SQL suite covers what the refusal
/// protects, which is a bed history that still describes the hospital.
/// </summary>
public class AdmissionChangeTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record Patient(Guid Id);
    private sealed record Admission(Guid Id, Guid PatientId, string Ward, string Status, long Version);

    [Fact]
    public async Task The_location_of_a_new_admission_resolves()
    {
        var client = await api.ClientAs("doctor");
        var admitted = await AdmitAsync(client, "ICU");

        var located = await client.GetAsync(admitted.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, located.StatusCode);
        Assert.Equal((await admitted.Content.ReadFromJsonAsync<Admission>())!.Id,
            (await located.Content.ReadFromJsonAsync<Admission>())!.Id);
        Assert.Equal(admitted.Version(), located.Version());
    }

    [Fact]
    public async Task A_change_that_names_no_version_is_refused()
    {
        var client = await api.ClientAs("doctor");
        var admission = await AdmittedAsync(client, "ICU");

        var res = await client.ChangeAdmissionAsync(admission.Id, new { status = "discharged" }, ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, res.StatusCode);
        Assert.Equal("Admitted", (await ReadAsync(client, admission.Id)).Status);
    }

    /// <summary>
    /// A weak tag is not a precondition a state transition can be taken on: it says the two
    /// representations are equivalent, not that they are the same version.
    /// </summary>
    [Fact]
    public async Task A_weak_version_is_not_a_version()
    {
        var client = await api.ClientAs("doctor");
        var admission = await AdmittedAsync(client, "ICU");

        var res = await client.ChangeAdmissionAsync(admission.Id, new { status = "discharged" },
            new EntityTagHeaderValue($"\"{admission.Version}\"", isWeak: true));

        Assert.Equal(HttpStatusCode.PreconditionRequired, res.StatusCode);
    }

    [Fact]
    public async Task A_change_taken_against_a_version_that_has_moved_is_refused()
    {
        var client = await api.ClientAs("doctor");
        var admitted = await AdmitAsync(client, "ICU");
        var admission = (await admitted.Content.ReadFromJsonAsync<Admission>())!;
        var stale = admitted.Version();

        var moved = await client.ChangeAdmissionAsync(admission.Id, new { ward = "Recovery" }, stale);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var late = await client.ChangeAdmissionAsync(admission.Id, new { status = "discharged" }, stale);

        Assert.Equal(HttpStatusCode.PreconditionFailed, late.StatusCode);
        Assert.Equal("Admitted", (await ReadAsync(client, admission.Id)).Status);
    }

    /// <summary>
    /// The defect this whole shape exists for: repeated, the old transfer closed the stay it had
    /// just opened, claimed a second bed and answered 200 over the result.
    /// </summary>
    [Fact]
    public async Task A_replayed_transfer_moves_the_patient_once()
    {
        var client = await api.ClientAs("doctor");
        var admitted = await AdmitAsync(client, "ICU");
        var admission = (await admitted.Content.ReadFromJsonAsync<Admission>())!;
        var taken = admitted.Version();

        var first = await client.ChangeAdmissionAsync(admission.Id, new { ward = "Recovery" }, taken);
        var replay = await client.ChangeAdmissionAsync(admission.Id, new { ward = "Recovery" }, taken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, replay.StatusCode);

        var current = await ReadAsync(client, admission.Id);
        Assert.Equal("Recovery", current.Ward);
        Assert.Equal(admission.Version + 1, current.Version);
    }

    [Fact]
    public async Task A_change_advances_the_version_it_publishes()
    {
        var client = await api.ClientAs("doctor");
        var admitted = await AdmitAsync(client, "ICU");
        var admission = (await admitted.Content.ReadFromJsonAsync<Admission>())!;

        var discharged = await client.ChangeAdmissionAsync(admission.Id, new { status = "discharged" }, admitted.Version());

        Assert.Equal(HttpStatusCode.OK, discharged.StatusCode);
        Assert.Equal("Discharged", (await discharged.Content.ReadFromJsonAsync<Admission>())!.Status);
        Assert.NotEqual(admitted.Version(), discharged.Version());
    }

    /// <summary>
    /// One body describes one transition. The schema settles it, so the action never has to decide
    /// which of two instructions a caller meant.
    /// </summary>
    [Fact]
    public async Task A_change_that_asks_for_two_things_at_once_is_rejected()
    {
        var client = await api.ClientAs("doctor");
        var admitted = await AdmitAsync(client, "ICU");
        var admission = (await admitted.Content.ReadFromJsonAsync<Admission>())!;

        var res = await client.ChangeAdmissionAsync(admission.Id,
            new { ward = "Recovery", status = "discharged" }, admitted.Version());

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Admitted", (await ReadAsync(client, admission.Id)).Status);
    }

    [Fact]
    public async Task A_discharge_is_audited_as_a_discharge_not_as_the_endpoint_that_served_it()
    {
        var client = await api.ClientAs("doctor");
        var admitted = await AdmitAsync(client, "ICU");
        var admission = (await admitted.Content.ReadFromJsonAsync<Admission>())!;

        await client.ChangeAdmissionAsync(admission.Id, new { status = "discharged" }, admitted.Version());

        Assert.Contains(api.Logs.Snapshot(), l => l.Message.Contains("AUDIT patient.discharge"));
    }

    private async Task<HttpResponseMessage> AdmitAsync(HttpClient client, string ward)
    {
        var mrn = $"MRN-{Guid.NewGuid():N}"[..12];
        var registered = await client.PostAsJsonAsync("/api/patients",
            new { mrn, givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });
        registered.EnsureSuccessStatusCode();
        var patient = (await registered.Content.ReadFromJsonAsync<Patient>())!;

        var admitted = await client.PostAsJsonAsync("/api/admissions", new { patientId = patient.Id, ward });
        admitted.EnsureSuccessStatusCode();
        return admitted;
    }

    private async Task<Admission> AdmittedAsync(HttpClient client, string ward) =>
        (await (await AdmitAsync(client, ward)).Content.ReadFromJsonAsync<Admission>())!;

    private static async Task<Admission> ReadAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Admission>($"/api/admissions/{id}"))!;
}

/// <summary>
/// Sending a conditional change by hand. <c>PatchAsJsonAsync</c> has nowhere to put the header the
/// request is conditional on, and the header is the point.
/// </summary>
internal static class ConditionalRequests
{
    internal static Task<HttpResponseMessage> ChangeAdmissionAsync(
        this HttpClient client, Guid id, object change, EntityTagHeaderValue? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/admissions/{id}")
        {
            Content = JsonContent.Create(change),
        };
        if (ifMatch is not null) request.Headers.IfMatch.Add(ifMatch);
        return client.SendAsync(request);
    }

    internal static Task<HttpResponseMessage> CorrectPatientAsync(
        this HttpClient client, Guid id, object correction, EntityTagHeaderValue? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/patients/{id}")
        {
            Content = JsonContent.Create(correction),
        };
        if (ifMatch is not null) request.Headers.IfMatch.Add(ifMatch);
        return client.SendAsync(request);
    }

    /// <summary>The version a response published, as the next change to it has to quote it back.</summary>
    internal static EntityTagHeaderValue Version(this HttpResponseMessage response) =>
        response.Headers.ETag ?? throw new InvalidOperationException(
            $"{response.RequestMessage?.RequestUri} answered {(int)response.StatusCode} with no ETag, so nothing can be changed against it.");
}

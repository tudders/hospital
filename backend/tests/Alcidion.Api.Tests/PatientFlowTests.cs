using System.Net;
using System.Net.Http.Json;

namespace Alcidion.Api.Tests;

public class PatientFlowTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record Patient(Guid Id, string Mrn, string GivenName, string FamilyName);
    private sealed record Admission(Guid Id, Guid PatientId, string Ward, string Status);

    [Fact]
    public async Task Register_then_admit_then_discharge()
    {
        var client = await api.ClientAs("nurse");
        var mrn = $"MRN-{Guid.NewGuid():N}"[..12];

        var reg = await client.PostAsJsonAsync("/api/patients", new { mrn, givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
        var patient = (await reg.Content.ReadFromJsonAsync<Patient>())!;
        Assert.Equal(mrn.ToUpperInvariant(), patient.Mrn);

        var adm = await client.PostAsJsonAsync("/api/admissions", new { patientId = patient.Id, ward = "ICU" });
        Assert.Equal(HttpStatusCode.Created, adm.StatusCode);
        var admission = (await adm.Content.ReadFromJsonAsync<Admission>())!;
        Assert.Equal("Admitted", admission.Status);

        var dup = await client.PostAsJsonAsync("/api/admissions", new { patientId = patient.Id, ward = "ICU" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        var dis = await client.ChangeAdmissionAsync(admission.Id, new { status = "discharged" }, adm.Version());
        Assert.Equal(HttpStatusCode.OK, dis.StatusCode);
        Assert.Equal("Discharged", (await dis.Content.ReadFromJsonAsync<Admission>())!.Status);
    }

    [Fact]
    public async Task Admit_unknown_patient_is_404_problem_details()
    {
        var client = await api.ClientAs("doctor");

        var res = await client.PostAsJsonAsync("/api/admissions", new { patientId = Guid.NewGuid(), ward = "ICU" });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Duplicate_mrn_is_409()
    {
        var client = await api.ClientAs("nurse");
        var body = new { mrn = "DUP-1", givenName = "A", familyName = "B", dateOfBirth = "1990-01-01" };

        await client.PostAsJsonAsync("/api/patients", body);
        var res = await client.PostAsJsonAsync("/api/patients", body);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Whitespace_padded_duplicate_mrn_is_409()
    {
        // The stored MRN is trimmed and upper-cased, so the uniqueness check has to see the same
        // normalized value. Otherwise " review-1 " registers a second patient with MRN REVIEW-1.
        var client = await api.ClientAs("nurse");
        var first = await client.PostAsJsonAsync("/api/patients",
            new { mrn = "REVIEW-1", givenName = "A", familyName = "B", dateOfBirth = "1990-01-01" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/patients",
            new { mrn = "  review-1  ", givenName = "C", familyName = "D", dateOfBirth = "1990-01-01" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<Patient>>("/api/patients"))!, p => p.Mrn == "REVIEW-1");
    }

    [Fact]
    public async Task Patient_search_with_no_match_returns_no_records()
    {
        var client = await api.ClientAs("doctor");

        var results = await client.GetFromJsonAsync<List<Patient>>("/api/patients?search=does-not-exist");

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Telemetry_ingest_is_anonymous_and_returns_correlation_id()
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "sess-1");

        var res = await client.PostAsJsonAsync("/api/telemetry/events", new[]
        {
            new { name = "page_view", sessionId = "sess-1", at = DateTimeOffset.UtcNow, props = new { path = "/" } }
        });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Contains("sess-1", await res.Content.ReadAsStringAsync());
    }
}

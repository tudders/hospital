using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Alcidion.Api.Tests;

/// <summary>
/// The conditional-correction contract on a patient. Demographics are what two people correct at
/// once - a clerk fixing an MRN while a nurse fixes the spelling of a name - so the version the
/// correction was decided against travels with it and the loser is told rather than dropped. These
/// tests are the HTTP half of that; the SQL suite covers what the refusal protects, which is a
/// record that still says who the patient is.
/// </summary>
public class PatientCorrectionTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record Patient(Guid Id, string Mrn, string GivenName, string FamilyName, DateOnly DateOfBirth, long Version);

    [Fact]
    public async Task The_location_of_a_new_patient_resolves_and_carries_the_same_version()
    {
        var client = await api.ClientAs("nurse");
        var registered = await RegisterAsync(client);

        var located = await client.GetAsync(registered.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, located.StatusCode);
        Assert.Equal(registered.Version(), located.Version());
        Assert.Equal(0, (await located.Content.ReadFromJsonAsync<Patient>())!.Version);
    }

    [Fact]
    public async Task A_correction_that_names_no_version_is_refused()
    {
        var client = await api.ClientAs("nurse");
        var patient = await RegisteredAsync(client);

        var res = await client.CorrectPatientAsync(patient.Id, new { givenName = "Augusta" }, ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, res.StatusCode);
        Assert.Equal("Ada", (await ReadAsync(client, patient.Id)).GivenName);
    }

    /// <summary>
    /// A weak tag is not a precondition a correction can be taken on: it says the two
    /// representations are equivalent, not that they are the same version.
    /// </summary>
    [Fact]
    public async Task A_weak_version_is_not_a_version()
    {
        var client = await api.ClientAs("nurse");
        var patient = await RegisteredAsync(client);

        var res = await client.CorrectPatientAsync(patient.Id, new { givenName = "Augusta" },
            new EntityTagHeaderValue($"\"{patient.Version}\"", isWeak: true));

        Assert.Equal(HttpStatusCode.PreconditionRequired, res.StatusCode);
    }

    /// <summary>
    /// <c>If-Match: *</c> asserts only that the patient exists. That is not the question a
    /// correction has to be right about, so it is refused the same way no header at all is.
    /// </summary>
    [Fact]
    public async Task A_wildcard_is_not_a_version()
    {
        var client = await api.ClientAs("nurse");
        var patient = await RegisteredAsync(client);

        var res = await client.CorrectPatientAsync(patient.Id, new { givenName = "Augusta" }, EntityTagHeaderValue.Any);

        Assert.Equal(HttpStatusCode.PreconditionRequired, res.StatusCode);
    }

    [Fact]
    public async Task A_correction_changes_only_what_it_names_and_advances_the_version()
    {
        var client = await api.ClientAs("nurse");
        var patient = await RegisteredAsync(client);

        var res = await client.CorrectPatientAsync(patient.Id, new { familyName = "King-Noel" }, Tag(patient.Version));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var corrected = (await res.Content.ReadFromJsonAsync<Patient>())!;
        Assert.Equal("King-Noel", corrected.FamilyName);
        Assert.Equal("Ada", corrected.GivenName);
        Assert.Equal(patient.Mrn, corrected.Mrn);
        Assert.Equal(patient.DateOfBirth, corrected.DateOfBirth);
        Assert.Equal(patient.Version + 1, corrected.Version);
        Assert.Equal(Tag(corrected.Version), res.Version());
    }

    [Fact]
    public async Task A_replayed_correction_is_refused_at_the_version_it_quoted()
    {
        var client = await api.ClientAs("nurse");
        var patient = await RegisteredAsync(client);
        var correction = new { givenName = "Augusta" };

        var first = await client.CorrectPatientAsync(patient.Id, correction, Tag(patient.Version));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // The same request arriving twice - a retry after a dropped response, a replaying proxy.
        var replay = await client.CorrectPatientAsync(patient.Id, correction, Tag(patient.Version));

        Assert.Equal(HttpStatusCode.PreconditionFailed, replay.StatusCode);
        Assert.Equal(1, (await ReadAsync(client, patient.Id)).Version);
    }

    [Fact]
    public async Task Correcting_an_mrn_onto_one_another_patient_holds_is_a_conflict()
    {
        var client = await api.ClientAs("nurse");
        var taken = await RegisteredAsync(client);
        var patient = await RegisteredAsync(client);

        var res = await client.CorrectPatientAsync(patient.Id, new { mrn = taken.Mrn }, Tag(patient.Version));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(patient.Mrn, (await ReadAsync(client, patient.Id)).Mrn);
    }

    [Fact]
    public async Task A_correction_that_corrects_nothing_is_a_400()
    {
        var client = await api.ClientAs("nurse");
        var patient = await RegisteredAsync(client);

        // minProperties on the schema: an empty body reaches no decision about what it meant.
        var res = await client.CorrectPatientAsync(patient.Id, new { }, Tag(patient.Version));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_correction_naming_a_field_the_schema_does_not_carry_is_a_400()
    {
        var client = await api.ClientAs("nurse");
        var patient = await RegisteredAsync(client);

        // additionalProperties: false. registeredAt is not correctable - it is what ties the record
        // to when it was made - and a body that silently dropped it would read as though it were.
        var res = await client.CorrectPatientAsync(patient.Id,
            new { registeredAt = "2020-01-01T00:00:00Z" }, Tag(patient.Version));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_blank_name_is_a_400_naming_the_field()
    {
        var client = await api.ClientAs("nurse");
        var patient = await RegisteredAsync(client);

        var res = await client.CorrectPatientAsync(patient.Id, new { givenName = "   " }, Tag(patient.Version));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("givenName", (await res.FieldErrors()).Keys);
    }

    [Fact]
    public async Task Correcting_an_unknown_patient_is_a_404()
    {
        var client = await api.ClientAs("nurse");

        var res = await client.CorrectPatientAsync(Guid.NewGuid(), new { givenName = "Augusta" }, Tag(0));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_viewer_cannot_correct_a_patient()
    {
        var patient = await RegisteredAsync(await api.ClientAs("nurse"));
        var viewer = await api.ClientAs("viewer");

        var res = await viewer.CorrectPatientAsync(patient.Id, new { givenName = "Augusta" }, Tag(patient.Version));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task A_correction_is_audited_as_patient_correct()
    {
        var client = await api.ClientAs("doctor");
        var patient = await RegisteredAsync(client);

        await client.CorrectPatientAsync(patient.Id, new { givenName = "Augusta" }, Tag(patient.Version));

        Assert.Contains(api.Logs.Snapshot(), l => l.Message.Contains("AUDIT patient.correct by doctor -> 200"));
    }

    private static EntityTagHeaderValue Tag(long version) => new($"\"{version}\"");

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client)
    {
        var mrn = $"COR{Guid.NewGuid():N}"[..12];
        var res = await client.PostAsJsonAsync("/api/patients",
            new { mrn, givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });
        res.EnsureSuccessStatusCode();
        return res;
    }

    private static async Task<Patient> RegisteredAsync(HttpClient client) =>
        (await (await RegisterAsync(client)).Content.ReadFromJsonAsync<Patient>())!;

    private static async Task<Patient> ReadAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Patient>($"/api/patients/{id}"))!;
}

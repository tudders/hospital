using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Alcidion.Api.Tests;

/// <summary>
/// The edge contract for <c>POST /api/admissions</c>. Two of these are regressions for what the
/// C#-bound body did before it had a schema: an absent <c>patientId</c> bound <c>Guid.Empty</c> and
/// came back 404 naming the nil UUID, and a ward of any length at all was accepted.
/// </summary>
public class AdmitSchemaValidationTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private async Task<HttpResponseMessage> Post(object body) =>
        await (await api.ClientAs("doctor")).PostAsJsonAsync("/api/admissions", body);

    /// <summary>A patient to admit, registered fresh so the test owns it.</summary>
    private async Task<Guid> APatient()
    {
        var client = await api.ClientAs("doctor");
        var mrn = $"ADM{Guid.NewGuid():N}"[..12];
        var res = await client.PostAsJsonAsync("/api/patients",
            new { mrn, givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_missing_patient_id_is_a_400_naming_the_field_not_a_404()
    {
        // The published document always said patientId was required; before the schema only the
        // binder enforced it, and a missing Guid is not missing - it is Guid.Empty, which then read
        // back as "Patient '00000000-0000-0000-0000-000000000000' was not found".
        var res = await Post(new { ward = "ICU" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("patientId", (await res.FieldErrors()).Keys);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("11111111-1111-1111-1111")]   // too few groups
    [InlineData("")]
    public async Task A_patient_id_that_is_not_a_uuid_is_refused_at_the_edge(string patientId)
    {
        var res = await Post(new { patientId, ward = "ICU" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("patientId", (await res.FieldErrors()).Keys);
    }

    [Fact]
    public async Task A_field_is_told_what_is_wrong_with_it_once()
    {
        // Every failing subschema also reports that it did not match, which says nothing the
        // keyword message has not already said. patientId is where that noise showed: Corvus
        // generates it as an entity of its own and reports its whole-schema result with an empty
        // schema location, which the formatter used to read as a keyword and pass through.
        var res = await Post(new { patientId = "not-a-guid", ward = "ICU" });

        var messages = (await res.FieldErrors())["patientId"];
        Assert.DoesNotContain(messages, m => m.Contains("subschema", StringComparison.OrdinalIgnoreCase));
        Assert.Single(messages);
    }

    [Fact]
    public async Task A_missing_ward_names_the_field()
    {
        var res = await Post(new { patientId = await APatient() });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("The ward field is required.", (await res.FieldErrors())["ward"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_ward_is_refused_at_the_edge_with_a_field_to_blame(string ward)
    {
        // The aggregate has always refused this, but as an ArgumentException rendered into a
        // ProblemDetails with no 'errors' dictionary - so a form had nothing to highlight.
        var res = await Post(new { patientId = await APatient(), ward });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("ward", (await res.FieldErrors()).Keys);
    }

    [Fact]
    public async Task A_ward_longer_than_the_column_is_refused_at_the_edge()
    {
        // Previously 201 Created: nothing in the stack bounded this.
        var res = await Post(new { patientId = await APatient(), ward = new string('W', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("ward", (await res.FieldErrors()).Keys);
    }

    [Fact]
    public async Task A_whitespace_padded_ward_still_reaches_the_aggregate()
    {
        // The ward pattern tolerates padding deliberately, because Admission.Admit trims.
        var res = await Post(new { patientId = await APatient(), ward = "  ICU  " });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ICU", created.GetProperty("ward").GetString());
    }

    [Fact]
    public async Task An_unknown_patient_is_a_422_naming_patient_id()
    {
        // Whether the id names a real patient is a lookup, not a shape, so the schema must not have
        // quietly turned this into a 400. Nor is it a 404: the collection this POST addresses is
        // there, and 404 answering a POST reads as "no such endpoint".
        var res = await Post(new { patientId = Guid.NewGuid(), ward = "ICU" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType!.MediaType);
        Assert.Contains("patientId", (await res.FieldErrors()).Keys);
    }

    [Fact]
    public async Task A_valid_body_is_still_accepted()
    {
        var res = await Post(new { patientId = await APatient(), ward = "Ward 3B" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }
}

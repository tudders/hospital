using System.Net;
using System.Net.Http.Json;
using Alcidion.Api.Controllers;

namespace Alcidion.Api.Tests;

public class CacheControlTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("/api/patients")]
    [InlineData("/api/admissions")]
    [InlineData("/api/wards")]
    [InlineData("/api/auth/me")]
    public async Task Authenticated_reads_prevent_response_storage(string path)
    {
        using var client = await api.ClientAs("viewer");

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Patient_and_admission_details_prevent_response_storage(bool readAdmission)
    {
        using var client = await api.ClientAs("doctor");
        using var registered = await client.PostAsJsonAsync("/api/patients", new
        {
            mrn = $"CACHE-{Guid.NewGuid():N}"[..12],
            givenName = "Ada",
            familyName = "Lovelace",
            dateOfBirth = "1990-01-01"
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var path = registered.Headers.Location;
        if (readAdmission)
        {
            var patient = await registered.Content.ReadFromJsonAsync<PatientDto>();
            using var admitted = await client.PostAsJsonAsync("/api/admissions",
                new { patientId = patient!.Id, ward = "ICU" });
            Assert.Equal(HttpStatusCode.Created, admitted.StatusCode);
            path = admitted.Headers.Location;
        }

        Assert.NotNull(path);
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }
}

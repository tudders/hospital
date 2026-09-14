using Alcidion.Admissions.Domain;

namespace Alcidion.Admissions.Infrastructure;

/// <summary>Reads the wards, their beds and their staffing out of the hospital schema.</summary>
public sealed class EfWardDirectory(EfAdmissionRepository admissions) : IWardDirectory
{
    public Task<IReadOnlyList<WardSummary>> ListAsync(CancellationToken ct = default) =>
        admissions.WardCapacityAsync(ct);
}

/// <summary>
/// The wards a run with no database offers. Capacity is nominal: there are no beds to count, so a
/// ward here never refuses an admission, which is what the in-memory admission repository expects.
/// </summary>
public sealed class InMemoryWardDirectory : IWardDirectory
{
    private const int NominalBeds = 20;

    private static readonly IReadOnlyList<WardSummary> Wards =
    [
        Ward("ED", "Emergency Department", "emergency"),
        Ward("ICU", "Intensive Care Unit", "icu"),
        Ward("GEN", "General Medicine", "general"),
        Ward("MAT", "Maternity", "maternity"),
        Ward("PAED", "Paediatrics", "paediatrics"),
    ];

    public Task<IReadOnlyList<WardSummary>> ListAsync(CancellationToken ct = default) => Task.FromResult(Wards);

    /// <summary>A stable id per code, so a ward keeps the same identity across restarts of a demo run.</summary>
    private static WardSummary Ward(string code, string name, string type)
    {
        var id = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
        return new WardSummary(id, code, name, type, NominalBeds, 0, NominalBeds, NominalBeds);
    }
}

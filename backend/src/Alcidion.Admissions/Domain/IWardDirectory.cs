namespace Alcidion.Admissions.Domain;

/// <summary>A ward as the admit form needs to see it: what it is called, and whether it can take anyone.</summary>
/// <param name="EffectiveCapacity">The lower of usable physical beds and the staffed bed limit.</param>
/// <param name="FreeBeds">Allocatable now: effective capacity less occupied beds, never negative.</param>
public sealed record WardSummary(
    Guid Id, string Code, string Name, string WardType,
    int Beds, int OccupiedBeds, int EffectiveCapacity, int FreeBeds);

/// <summary>
/// The wards a patient can be admitted to. Read-only: wards are part of the hospital's physical
/// structure, which Admissions consumes rather than owns.
/// </summary>
public interface IWardDirectory
{
    Task<IReadOnlyList<WardSummary>> ListAsync(CancellationToken ct = default);
}

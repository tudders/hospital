namespace Alcidion.Admissions.Infrastructure;

/// <summary>
/// Rows of the existing hospital schema, as Admissions needs to see them. They are persistence
/// types, not aggregates: <see cref="Domain.Admission"/> has no setters for EF to write through,
/// and the columns below carry more of the schema than the aggregate models.
/// </summary>
internal sealed class AdmissionRow
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? AdmittedAt { get; set; }
    public DateTimeOffset? ExpectedDischargeAt { get; set; }
    public DateTimeOffset? DischargedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public int Priority { get; set; }
    public long ConcurrencyVersion { get; set; }
}

internal sealed class BedRequestRow
{
    public Guid Id { get; set; }
    public Guid AdmissionId { get; set; }
    public Guid? TreatmentOrderId { get; set; }
    public Guid? TargetWardId { get; set; }
    public string? RequiredBedType { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public long ConcurrencyVersion { get; set; }
}

internal sealed class BedStayRow
{
    public Guid Id { get; set; }
    public Guid AdmissionId { get; set; }
    public Guid BedId { get; set; }
    public Guid? BedRequestId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ExpectedEndAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public long ConcurrencyVersion { get; set; }
}

internal sealed class BedRow
{
    public Guid Id { get; set; }
    public Guid WardId { get; set; }
    public string Code { get; set; } = "";
    public string BedType { get; set; } = "";
    public DateTimeOffset AvailableFrom { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}

internal sealed class BedBlockRow
{
    public Guid Id { get; set; }
    public Guid BedId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string Reason { get; set; } = "";
}

internal sealed class WardRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string WardType { get; set; } = "";
}

internal sealed class WardCapacityPeriodRow
{
    public Guid Id { get; set; }
    public Guid WardId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public int StaffedBedLimit { get; set; }
}

/// <summary>
/// Admissions' read-only view of <c>dbo.patients</c>. Once both contexts share a database the
/// patient row is the read model, so the PatientRegistered handler has nothing left to copy - the
/// event still marks the seam, it just no longer carries the data across it.
/// </summary>
internal sealed class KnownPatientRow
{
    public Guid Id { get; set; }
    public string Mrn { get; set; } = "";
    public string GivenName { get; set; } = "";
    public string FamilyName { get; set; } = "";
}

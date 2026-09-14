namespace Alcidion.Admissions.Domain;

public enum AdmissionStatus { Admitted, Discharged }

/// <summary>Aggregate root for the Admissions bounded context.</summary>
public sealed class Admission
{
    public Guid Id { get; }
    public Guid PatientId { get; }
    public string Ward { get; private set; }
    public DateTimeOffset AdmittedAt { get; }
    public DateTimeOffset? DischargedAt { get; private set; }
    public AdmissionStatus Status => DischargedAt is null ? AdmissionStatus.Admitted : AdmissionStatus.Discharged;

    /// <summary>
    /// The stored version this instance was read at. Storage owns the number; the aggregate only
    /// carries it, so a write can be taken under the version it was decided under and lose to
    /// anything that moved the admission on in between. A transfer and a discharge each bump it.
    /// </summary>
    public long Version { get; private set; }

    private readonly Lock _gate = new();

    private Admission(Guid id, Guid patientId, string ward, DateTimeOffset admittedAt)
    {
        Id = id; PatientId = patientId; Ward = ward; AdmittedAt = admittedAt;
    }

    /// <summary>Static factory method: the only way to construct a valid Admission.</summary>
    public static Admission Admit(Guid patientId, string ward, DateTimeOffset now)
    {
        if (patientId == Guid.Empty) throw new ArgumentException("Patient is required.", nameof(patientId));
        if (string.IsNullOrWhiteSpace(ward)) throw new ArgumentException("Ward is required.", nameof(ward));
        return new Admission(Guid.NewGuid(), patientId, ward.Trim(), now);
    }

    /// <summary>
    /// Rebuilds an admission from storage. Invariants are not re-run: a stored row was validated by
    /// <see cref="Admit"/> on the way in, and the discharge time is history rather than a transition.
    /// </summary>
    public static Admission Rehydrate(Guid id, Guid patientId, string ward, DateTimeOffset admittedAt,
        DateTimeOffset? dischargedAt, long version = 0) =>
        new(id, patientId, ward, admittedAt) { DischargedAt = dischargedAt, Version = version };

    /// <summary>
    /// Records that this instance's state has been committed. Only a store calls it: the version is
    /// the store's, and an aggregate that bumped its own would report a version nothing was written at.
    /// </summary>
    internal void Committed() => Version++;

    /// <summary>
    /// Discharging twice is a conflict, not a no-op. The check and the write are taken under one
    /// lock so two concurrent discharges cannot both succeed and publish a duplicate event.
    /// </summary>
    public void Discharge(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (DischargedAt is not null) throw new InvalidOperationException("Admission is already discharged.");
            if (now < AdmittedAt) throw new ArgumentException("Discharge cannot precede admission.", nameof(now));
            DischargedAt = now;
        }
    }

    public void Transfer(string ward)
    {
        if (string.IsNullOrWhiteSpace(ward)) throw new ArgumentException("Ward is required.", nameof(ward));
        lock (_gate)
        {
            if (DischargedAt is not null) throw new InvalidOperationException("Admission is already discharged.");
            Ward = ward.Trim();
        }
    }
}

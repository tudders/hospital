using Microsoft.EntityFrameworkCore;

namespace Alcidion.Admissions.Infrastructure;

/// <summary>
/// Maps the Admissions context onto the existing hospital schema. The schema is owned by the
/// tracked SQL migrations in <c>backend/database</c>, so nothing here creates or alters tables.
/// See docs/adr/0002-ef-core-over-an-existing-schema.md.
/// </summary>
public sealed class AdmissionsDbContext(DbContextOptions<AdmissionsDbContext> options) : DbContext(options)
{
    internal DbSet<AdmissionRow> Admissions => Set<AdmissionRow>();
    internal DbSet<BedRequestRow> BedRequests => Set<BedRequestRow>();
    internal DbSet<BedStayRow> BedStays => Set<BedStayRow>();
    internal DbSet<BedRow> Beds => Set<BedRow>();
    internal DbSet<BedBlockRow> BedBlocks => Set<BedBlockRow>();
    internal DbSet<WardRow> Wards => Set<WardRow>();
    internal DbSet<WardCapacityPeriodRow> WardCapacityPeriods => Set<WardCapacityPeriodRow>();
    internal DbSet<KnownPatientRow> KnownPatients => Set<KnownPatientRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var admissions = model.Entity<AdmissionRow>();
        admissions.ToTable("admissions", "dbo");
        admissions.HasKey(a => a.Id);
        admissions.Property(a => a.Id).HasColumnName("id");
        admissions.Property(a => a.PatientId).HasColumnName("patient_id");
        admissions.Property(a => a.RequestedAt).HasColumnName("requested_at");
        admissions.Property(a => a.AdmittedAt).HasColumnName("admitted_at");
        admissions.Property(a => a.ExpectedDischargeAt).HasColumnName("expected_discharge_at");
        admissions.Property(a => a.DischargedAt).HasColumnName("discharged_at");
        admissions.Property(a => a.CancelledAt).HasColumnName("cancelled_at");
        admissions.Property(a => a.Priority).HasColumnName("priority");
        admissions.Property(a => a.ConcurrencyVersion).HasColumnName("concurrency_version");

        var requests = model.Entity<BedRequestRow>();
        requests.ToTable("bed_requests", "dbo");
        requests.HasKey(r => r.Id);
        requests.Property(r => r.Id).HasColumnName("id");
        requests.Property(r => r.AdmissionId).HasColumnName("admission_id");
        requests.Property(r => r.TreatmentOrderId).HasColumnName("treatment_order_id");
        requests.Property(r => r.TargetWardId).HasColumnName("target_ward_id");
        requests.Property(r => r.RequiredBedType).HasColumnName("required_bed_type").HasMaxLength(100);
        requests.Property(r => r.RequestedAt).HasColumnName("requested_at");
        requests.Property(r => r.Priority).HasColumnName("priority");
        requests.Property(r => r.FulfilledAt).HasColumnName("fulfilled_at");
        requests.Property(r => r.CancelledAt).HasColumnName("cancelled_at");
        requests.Property(r => r.ConcurrencyVersion).HasColumnName("concurrency_version");

        var stays = model.Entity<BedStayRow>();
        stays.ToTable("bed_stays", "dbo");
        stays.HasKey(s => s.Id);
        stays.Property(s => s.Id).HasColumnName("id");
        stays.Property(s => s.AdmissionId).HasColumnName("admission_id");
        stays.Property(s => s.BedId).HasColumnName("bed_id");
        stays.Property(s => s.BedRequestId).HasColumnName("bed_request_id");
        stays.Property(s => s.StartedAt).HasColumnName("started_at");
        stays.Property(s => s.ExpectedEndAt).HasColumnName("expected_end_at");
        stays.Property(s => s.EndedAt).HasColumnName("ended_at");
        stays.Property(s => s.EndReason).HasColumnName("end_reason").HasMaxLength(200);
        stays.Property(s => s.ConcurrencyVersion).HasColumnName("concurrency_version");

        var beds = model.Entity<BedRow>();
        beds.ToTable("beds", "dbo");
        beds.HasKey(b => b.Id);
        beds.Property(b => b.Id).HasColumnName("id");
        beds.Property(b => b.WardId).HasColumnName("ward_id");
        beds.Property(b => b.Code).HasColumnName("code").HasMaxLength(100);
        beds.Property(b => b.BedType).HasColumnName("bed_type").HasMaxLength(100);
        beds.Property(b => b.AvailableFrom).HasColumnName("available_from");
        beds.Property(b => b.RetiredAt).HasColumnName("retired_at");

        var blocks = model.Entity<BedBlockRow>();
        blocks.ToTable("bed_blocks", "dbo");
        blocks.HasKey(x => x.Id);
        blocks.Property(x => x.Id).HasColumnName("id");
        blocks.Property(x => x.BedId).HasColumnName("bed_id");
        blocks.Property(x => x.StartsAt).HasColumnName("starts_at");
        blocks.Property(x => x.EndsAt).HasColumnName("ends_at");
        blocks.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);

        var wards = model.Entity<WardRow>();
        wards.ToTable("wards", "dbo");
        wards.HasKey(w => w.Id);
        wards.Property(w => w.Id).HasColumnName("id");
        wards.Property(w => w.Code).HasColumnName("code").HasMaxLength(100);
        wards.Property(w => w.Name).HasColumnName("name").HasMaxLength(200);
        wards.Property(w => w.WardType).HasColumnName("ward_type").HasMaxLength(100);

        var capacity = model.Entity<WardCapacityPeriodRow>();
        capacity.ToTable("ward_capacity_periods", "dbo");
        capacity.HasKey(c => c.Id);
        capacity.Property(c => c.Id).HasColumnName("id");
        capacity.Property(c => c.WardId).HasColumnName("ward_id");
        capacity.Property(c => c.StartsAt).HasColumnName("starts_at");
        capacity.Property(c => c.EndsAt).HasColumnName("ends_at");
        capacity.Property(c => c.StaffedBedLimit).HasColumnName("staffed_bed_limit");

        // Read-only by design: Patients owns this table, Admissions only looks at it.
        var patients = model.Entity<KnownPatientRow>();
        patients.ToTable("patients", "dbo", t => t.ExcludeFromMigrations());
        patients.HasKey(p => p.Id);
        patients.Property(p => p.Id).HasColumnName("id");
        patients.Property(p => p.Mrn).HasColumnName("mrn").HasMaxLength(100);
        patients.Property(p => p.GivenName).HasColumnName("given_name").HasMaxLength(200);
        patients.Property(p => p.FamilyName).HasColumnName("family_name").HasMaxLength(200);
    }
}

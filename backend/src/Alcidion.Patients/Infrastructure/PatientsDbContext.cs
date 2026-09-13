using Microsoft.EntityFrameworkCore;

namespace Alcidion.Patients.Infrastructure;

/// <summary>
/// Maps the Patients context onto the existing hospital schema. The schema is owned by the tracked
/// SQL migrations in <c>backend/database</c>, so nothing here creates or alters tables: this is a
/// mapping over a database that already exists. See docs/adr/0002-ef-core-over-an-existing-schema.md.
/// </summary>
public sealed class PatientsDbContext(DbContextOptions<PatientsDbContext> options) : DbContext(options)
{
    internal DbSet<PatientRow> Patients => Set<PatientRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var patients = model.Entity<PatientRow>();
        patients.ToTable("patients", "dbo");
        patients.HasKey(p => p.Id);
        patients.Property(p => p.Id).HasColumnName("id");
        patients.Property(p => p.Mrn).HasColumnName("mrn").HasMaxLength(100);
        patients.Property(p => p.GivenName).HasColumnName("given_name").HasMaxLength(200);
        patients.Property(p => p.FamilyName).HasColumnName("family_name").HasMaxLength(200);
        patients.Property(p => p.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("date");
        patients.Property(p => p.Gender).HasColumnName("gender").HasMaxLength(100);
        patients.Property(p => p.RegisteredAt).HasColumnName("registered_at");

        // Mirrors ux_patients_mrn. Declaring it here is what lets the insert itself decide
        // uniqueness, rather than a read-then-write that two requests can interleave.
        patients.HasIndex(p => p.Mrn).IsUnique().HasDatabaseName("ux_patients_mrn");
    }
}

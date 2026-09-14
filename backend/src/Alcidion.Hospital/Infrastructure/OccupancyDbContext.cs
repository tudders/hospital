using Microsoft.EntityFrameworkCore;

namespace Alcidion.Hospital.Infrastructure;

/// <summary>
/// The context the occupancy view reads through. The schema is owned by the tracked SQL migrations
/// in <c>backend/database</c>, so nothing here creates or alters tables; see
/// docs/adr/0002-ef-core-over-an-existing-schema.md. It maps one keyless result shape rather than
/// the eight tables the snapshot crosses, because the snapshot is the only thing it is for: there
/// is nothing to write, nothing to track, and no second query that would want the tables separately.
/// </summary>
public sealed class OccupancyDbContext(DbContextOptions<OccupancyDbContext> options) : DbContext(options)
{
    internal DbSet<BedSnapshotRow> BedSnapshot => Set<BedSnapshotRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var beds = model.Entity<BedSnapshotRow>();
        beds.HasNoKey().ToView(null);
        beds.Property(b => b.Id).HasColumnName("id");
        beds.Property(b => b.Code).HasColumnName("code");
        beds.Property(b => b.Number).HasColumnName("bed_number");
        beds.Property(b => b.HospitalId).HasColumnName("hospital_id");
        beds.Property(b => b.HospitalName).HasColumnName("hospital_name");
        beds.Property(b => b.FloorId).HasColumnName("floor_id");
        beds.Property(b => b.FloorName).HasColumnName("floor_name");
        beds.Property(b => b.FloorNumber).HasColumnName("floor_number");
        beds.Property(b => b.WardId).HasColumnName("ward_id");
        beds.Property(b => b.WardName).HasColumnName("ward_name");
        beds.Property(b => b.RoomId).HasColumnName("room_id");
        beds.Property(b => b.RoomName).HasColumnName("room_name");
        beds.Property(b => b.RoomNumber).HasColumnName("room_number");
        beds.Property(b => b.Status).HasColumnName("status");
        beds.Property(b => b.PatientId).HasColumnName("patient_id");
        beds.Property(b => b.PatientName).HasColumnName("patient_name");
    }
}

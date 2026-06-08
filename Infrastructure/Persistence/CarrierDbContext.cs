using CarrierService.Domain;
using CarrierService.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CarrierService.Infrastructure.Persistence;

public sealed class CarrierDbContext : DbContext
{
    public CarrierDbContext(DbContextOptions<CarrierDbContext> options)
        : base(options)
    {
    }

    public DbSet<Carrier> Carriers => Set<Carrier>();
    public DbSet<CarrierServiceLevel> ServiceLevels => Set<CarrierServiceLevel>();
    public DbSet<CarrierLane> CarrierLanes => Set<CarrierLane>();
    public DbSet<CarrierCategoryRestriction> CategoryRestrictions => Set<CarrierCategoryRestriction>();
    public DbSet<CarrierIncident> Incidents => Set<CarrierIncident>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Carrier>(entity =>
        {
            entity.ToTable("carriers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            entity.HasMany(x => x.ServiceLevels).WithOne().HasForeignKey(x => x.CarrierId);
        });

        modelBuilder.Entity<CarrierServiceLevel>(entity =>
        {
            entity.ToTable("carrier_service_levels");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Mode).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.MaximumWeightKg).HasPrecision(12, 3);
            entity.Property(x => x.MaximumCubicWeightKg).HasPrecision(12, 3);
            entity.HasIndex(x => new { x.CarrierId, x.Code }).IsUnique();
            entity.HasMany(x => x.Lanes).WithOne().HasForeignKey(x => x.CarrierServiceLevelId);
            entity.HasMany(x => x.CategoryRestrictions).WithOne().HasForeignKey(x => x.CarrierServiceLevelId);
        });

        modelBuilder.Entity<CarrierLane>(entity =>
        {
            entity.ToTable("carrier_lanes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.OriginNodeId, x.DestinationNodeId, x.IsActive });
        });

        modelBuilder.Entity<CarrierCategoryRestriction>(entity =>
        {
            entity.ToTable("carrier_category_restrictions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Category).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.CarrierServiceLevelId, x.Category }).IsUnique();
        });

        modelBuilder.Entity<CarrierIncident>(entity =>
        {
            entity.ToTable("carrier_incidents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IncidentType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.CarrierId, x.ResolvedAt });
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => x.ProcessedAt);
        });
    }
}

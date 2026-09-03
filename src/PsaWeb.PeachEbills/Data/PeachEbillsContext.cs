using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

public partial class PeachEbillsContext : DbContext
{
    public PeachEbillsContext(DbContextOptions<PeachEbillsContext> options)
        : base(options)
    {
    }

    public virtual DbSet<CurrentAmbient> CurrentAmbient { get; set; }

    public virtual DbSet<DatilApi> DatilApi { get; set; }

    public virtual DbSet<PurchaseOrderSync> PurchaseOrderSync { get; set; }

    public virtual DbSet<TaxWithHoldings> TaxWithHoldings { get; set; }

    public virtual DbSet<Transmitter> Transmitter { get; set; }

    public virtual DbSet<TransmitterStatus> TransmitterStatus { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CurrentAmbient>(entity =>
        {
            entity.Property(e => e.Active).HasDefaultValue(true);

            entity.HasOne(d => d.RucNavigation).WithOne(p => p.CurrentAmbient)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CurrentAmbient_Transmitter");
        });

        modelBuilder.Entity<DatilApi>(entity =>
        {
            entity.HasOne(d => d.RucNavigation).WithMany(p => p.DatilApi)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DatilAPI_Transmitter");
        });

        modelBuilder.Entity<TaxWithHoldings>(entity =>
        {
            entity.HasKey(e => e.Thid).HasName("PK_Retencion");

            entity.Property(e => e.IsValid).HasDefaultValue(true);
            entity.Property(e => e.IssueType).HasDefaultValue((short)1);
            entity.Property(e => e.TransType).HasDefaultValue(2);

            entity.HasOne(d => d.TransmitterRucNavigation).WithMany(p => p.TaxWithHoldings)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TaxWithHoldings_Transmitter");
        });

        modelBuilder.Entity<Transmitter>(entity =>
        {
            entity.Property(e => e.ToSendToDatil).HasDefaultValue(true);
        });

        modelBuilder.Entity<TransmitterStatus>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}

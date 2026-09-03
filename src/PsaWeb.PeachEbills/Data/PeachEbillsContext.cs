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

    public virtual DbSet<DatilRequests> DatilRequests { get; set; }

    public virtual DbSet<EpoofGeneralAditionalInfo> EpoofGeneralAditionalInfo { get; set; }

    public virtual DbSet<Establishments> Establishments { get; set; }

    public virtual DbSet<PeachConnString> PeachConnString { get; set; }

    public virtual DbSet<Persons> Persons { get; set; }

    public virtual DbSet<PurchaseOrderSync> PurchaseOrderSync { get; set; }

    public virtual DbSet<TaxWithHoldings> TaxWithHoldings { get; set; }

    public virtual DbSet<Thdetails> Thdetails { get; set; }

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

        modelBuilder.Entity<EpoofGeneralAditionalInfo>(entity =>
        {
            entity.Property(e => e.CodDoc).IsFixedLength();

            entity.HasOne(d => d.RucNavigation).WithMany(p => p.EpoofGeneralAditionalInfo)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_EPoofGeneralAditionalInfo_Transmitter");
        });

        modelBuilder.Entity<Establishments>(entity =>
        {
            entity.Property(e => e.Code).IsFixedLength();
            entity.Property(e => e.IsElectronic).HasDefaultValue(true);
            entity.Property(e => e.IsFromPeach).HasDefaultValue(true);
            entity.Property(e => e.IssuePoint).IsFixedLength();

            entity.HasOne(d => d.RucNavigation).WithMany(p => p.Establishments)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Establishments_Transmitter");
        });

        modelBuilder.Entity<PeachConnString>(entity =>
        {
            entity.Property(e => e.CheckToLoadByRuc).HasDefaultValue(true);

            entity.HasOne(d => d.RucNavigation).WithMany(p => p.PeachConnString)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PeachConnString_Transmitter");
        });

        modelBuilder.Entity<Persons>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_persona");

            entity.Property(e => e.Type).HasComment("Tipo de Identificación");

            entity.HasOne(d => d.RuctransmitterNavigation).WithMany(p => p.Persons)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Persons_Transmitter");
        });

        modelBuilder.Entity<TaxWithHoldings>(entity =>
        {
            entity.HasKey(e => e.Thid).HasName("PK_Retencion");

            entity.Property(e => e.IsValid).HasDefaultValue(true);
            entity.Property(e => e.IssueType).HasDefaultValue((short)1);
            entity.Property(e => e.TransType).HasDefaultValue(2);

            entity.HasOne(d => d.TransmitterEstablishmentNavigation).WithMany(p => p.TaxWithHoldings).HasConstraintName("FK_TaxWithHoldings_Establishments");

            entity.HasOne(d => d.TransmitterRucNavigation).WithMany(p => p.TaxWithHoldings)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TaxWithHoldings_Transmitter");
        });

        modelBuilder.Entity<Thdetails>(entity =>
        {
            entity.Property(e => e.PurchaseCodDoc).IsFixedLength();

            entity.HasOne(d => d.TaxWithHoldingNavigation).WithMany(p => p.Thdetails).HasConstraintName("FK_THDetails_TaxWithHoldings");
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

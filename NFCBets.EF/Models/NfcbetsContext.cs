using Microsoft.EntityFrameworkCore;

namespace NFCBets.EF.Models;

public partial class NfcbetsContext : DbContext
{
    public NfcbetsContext()
    {
    }

    public NfcbetsContext(DbContextOptions<NfcbetsContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Arena> Arenas { get; set; }

    public virtual DbSet<Food> Foods { get; set; }

    public virtual DbSet<FoodCategory> FoodCategories { get; set; }

    public virtual DbSet<FoodCategoryAllergy> FoodCategoryAllergies { get; set; }

    public virtual DbSet<FoodCategoryFood> FoodCategoryFoods { get; set; }

    public virtual DbSet<FoodCategoryPreference> FoodCategoryPreferences { get; set; }

    public virtual DbSet<Pirate> Pirates { get; set; }

    public virtual DbSet<RoundFoodCourse> RoundFoodCourses { get; set; }

    public virtual DbSet<RoundPiratePlacement> RoundPiratePlacements { get; set; }

    public virtual DbSet<RoundResult> RoundResults { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https: //go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer(
            "Server=localhost;Database=NFCBets;Trusted_Connection=True;encrypt=true;TrustServerCertificate=true;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Arena>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Arena_pk");

            entity.ToTable("Arena");
        });

        modelBuilder.Entity<Food>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Food_pk");

            entity.ToTable("Food");
        });

        modelBuilder.Entity<FoodCategory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("FoodCategory_pk");

            entity.ToTable("FoodCategory");
        });

        modelBuilder.Entity<FoodCategoryAllergy>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("FoodCategoryAllergy_pk");

            entity.ToTable("FoodCategoryAllergy");

            entity.HasOne(d => d.FoodCategory).WithMany(p => p.FoodCategoryAllergies)
                .HasForeignKey(d => d.FoodCategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FoodCategoryAllergy_FoodCategory_Id_fk");

            entity.HasOne(d => d.Pirate).WithMany(p => p.FoodCategoryAllergies)
                .HasForeignKey(d => d.PirateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FoodCategoryAllergy_Pirate_Id_fk");
        });

        modelBuilder.Entity<FoodCategoryFood>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("FoodCategoryFood_pk");

            entity.ToTable("FoodCategoryFood");

            entity.HasOne(d => d.FoodCategory).WithMany(p => p.FoodCategoryFoods)
                .HasForeignKey(d => d.FoodCategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FoodCategoryFood_FoodCategory_Id_fk");

            entity.HasOne(d => d.Food).WithMany(p => p.FoodCategoryFoods)
                .HasForeignKey(d => d.FoodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FoodCategoryFood_Food_Id_fk");
        });

        modelBuilder.Entity<FoodCategoryPreference>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("FoodCategoryPreferences_pk");

            entity.HasOne(d => d.FoodCategory).WithMany(p => p.FoodCategoryPreferences)
                .HasForeignKey(d => d.FoodCategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FoodCategoryPreferences_FoodCategory_Id_fk");

            entity.HasOne(d => d.Pirate).WithMany(p => p.FoodCategoryPreferences)
                .HasForeignKey(d => d.PirateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FoodCategoryPreferences_Pirate_Id_fk");
        });

        modelBuilder.Entity<Pirate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Pirate_pk");

            entity.ToTable("Pirate");
        });

        modelBuilder.Entity<RoundFoodCourse>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("RoundFoodCourse_pk");

            entity.ToTable("RoundFoodCourse");

            entity.HasOne(d => d.Arena).WithMany(p => p.RoundFoodCourses)
                .HasForeignKey(d => d.ArenaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("RoundFoodCourse_Arena_Id_fk");

            entity.HasOne(d => d.Food).WithMany(p => p.RoundFoodCourses)
                .HasForeignKey(d => d.FoodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("RoundFoodCourse_Food_Id_fk");
        });

        modelBuilder.Entity<RoundPiratePlacement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("RoundPiratePlacement_pk");

            entity.ToTable("RoundPiratePlacement");

            entity.HasOne(d => d.Arena).WithMany(p => p.RoundPiratePlacements)
                .HasForeignKey(d => d.ArenaId)
                .HasConstraintName("RoundPiratePlacement_Arena_Id_fk");

            entity.HasOne(d => d.Pirate).WithMany(p => p.RoundPiratePlacements)
                .HasForeignKey(d => d.PirateId)
                .HasConstraintName("RoundPiratePlacement_Pirate_Id_fk");
        });

        modelBuilder.Entity<RoundResult>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("RoundResults_pk");

            entity.HasOne(d => d.Arena).WithMany(p => p.RoundResults)
                .HasForeignKey(d => d.ArenaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("RoundResults_Arena_Id_fk");

            entity.HasOne(d => d.Pirate).WithMany(p => p.RoundResults)
                .HasForeignKey(d => d.PirateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("RoundResults_Pirate_Id_fk");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
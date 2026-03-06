using Microsoft.EntityFrameworkCore;

namespace LMSupply.Console.Host.Data;

public sealed class ApiKeyDbContext(DbContextOptions<ApiKeyDbContext> options)
    : DbContext(options)
{
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiKeyRequest> ApiKeyRequests => Set<ApiKeyRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasKey(k => k.Id);
            e.HasIndex(k => k.KeyHash).IsUnique();
            e.Property(k => k.CreatedAt).HasConversion(
                v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            e.Property(k => k.LastUsedAt).HasConversion(
                v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        });

        modelBuilder.Entity<ApiKeyRequest>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.ApiKeyId);
            e.HasIndex(r => r.Timestamp);
            e.Property(r => r.Timestamp).HasConversion(
                v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            e.HasOne(r => r.ApiKey)
             .WithMany(k => k.Requests)
             .HasForeignKey(r => r.ApiKeyId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

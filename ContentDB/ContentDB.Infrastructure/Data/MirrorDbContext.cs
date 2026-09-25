// ContentDB C# Mirror
// EF Core DbContext,存放镜像缓存元数据(不是官方完整 schema)。

using ContentDB.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Infrastructure.Data;

public class MirrorDbContext : DbContext
{
	public MirrorDbContext(DbContextOptions<MirrorDbContext> options) : base(options)
	{
	}

	public DbSet<CachedResponse> CachedResponses => Set<CachedResponse>();
	public DbSet<MirroredFile> MirroredFiles => Set<MirroredFile>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.Entity<CachedResponse>(e =>
		{
			e.ToTable("cached_response");
			e.HasKey(x => x.CacheKey);
			e.Property(x => x.CacheKey).HasMaxLength(512);
			e.Property(x => x.Body).HasColumnType("text");
			e.Property(x => x.ContentType).HasMaxLength(128);
			e.HasIndex(x => x.ExpiresAt);
		});

		modelBuilder.Entity<MirroredFile>(e =>
		{
			e.ToTable("mirrored_file");
			e.HasKey(x => x.UpstreamPath);
			e.Property(x => x.UpstreamPath).HasMaxLength(512);
			e.Property(x => x.ObjectKey).HasMaxLength(512);
			e.Property(x => x.ContentType).HasMaxLength(128);
			e.HasIndex(x => x.IsStored);
		});
	}
}

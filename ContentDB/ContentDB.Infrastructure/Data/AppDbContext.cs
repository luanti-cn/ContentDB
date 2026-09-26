// ContentDB C# —— 本地领域数据库上下文(自主运营内容)
// 与 MirrorDbContext(上游缓存)分离。枚举以字符串存储,便于迁移与可读性。

using ContentDB.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Thread = ContentDB.Core.Domain.Thread;

namespace ContentDB.Infrastructure.Data;

public class AppDbContext : DbContext
{
	public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

	public DbSet<User> Users => Set<User>();
	public DbSet<Package> Packages => Set<Package>();
	public DbSet<PackageRelease> Releases => Set<PackageRelease>();
	public DbSet<PackageScreenshot> Screenshots => Set<PackageScreenshot>();
	public DbSet<Dependency> Dependencies => Set<Dependency>();
	public DbSet<MetaPackage> MetaPackages => Set<MetaPackage>();
	public DbSet<PackageGameSupport> GameSupport => Set<PackageGameSupport>();
	public DbSet<PackageTranslation> Translations => Set<PackageTranslation>();
	public DbSet<PackageAlias> Aliases => Set<PackageAlias>();
	public DbSet<PackageUpdateConfig> UpdateConfigs => Set<PackageUpdateConfig>();
	public DbSet<PackageDailyStats> DailyStats => Set<PackageDailyStats>();
	public DbSet<Tag> Tags => Set<Tag>();
	public DbSet<ContentWarning> ContentWarnings => Set<ContentWarning>();
	public DbSet<License> Licenses => Set<License>();
	public DbSet<LuantiRelease> LuantiReleases => Set<LuantiRelease>();
	public DbSet<Language> Languages => Set<Language>();
	public DbSet<Thread> Threads => Set<Thread>();
	public DbSet<ThreadReply> ThreadReplies => Set<ThreadReply>();
	public DbSet<PackageReview> Reviews => Set<PackageReview>();
	public DbSet<PackageReviewVote> ReviewVotes => Set<PackageReviewVote>();
	public DbSet<Collection> Collections => Set<Collection>();
	public DbSet<CollectionPackage> CollectionPackages => Set<CollectionPackage>();
	public DbSet<APIToken> ApiTokens => Set<APIToken>();
	public DbSet<OAuthClient> OAuthClients => Set<OAuthClient>();
	public DbSet<Notification> Notifications => Set<Notification>();
	public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
	public DbSet<ForumTopic> ForumTopics => Set<ForumTopic>();
	public DbSet<PairedDevice> PairedDevices => Set<PairedDevice>();
	public DbSet<DevicePairing> DevicePairings => Set<DevicePairing>();
	public DbSet<ServerCredential> ServerCredentials => Set<ServerCredential>();
	public DbSet<PlayerCharacter> PlayerCharacters => Set<PlayerCharacter>();
	public DbSet<FriendLink> FriendLinks => Set<FriendLink>();
	public DbSet<GameServer> GameServers => Set<GameServer>();
	public DbSet<Party> Parties => Set<Party>();
	public DbSet<PartyMember> PartyMembers => Set<PartyMember>();
	public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();

	/// <summary>
	/// 标准许可证种子。id=1 固定为 "Other"(与 Package.LicenseId/MediaLicenseId 默认值对齐)。
	/// 修改此列表后需新增一个迁移以同步数据库。
	/// </summary>
	private static readonly License[] SeedLicenses =
	[
		new() { Id = 1, Name = "Other", IsFoss = false, Url = null },
		new() { Id = 2, Name = "MIT", IsFoss = true, Url = "https://opensource.org/licenses/MIT" },
		new() { Id = 3, Name = "Apache-2.0", IsFoss = true, Url = "https://www.apache.org/licenses/LICENSE-2.0" },
		new() { Id = 4, Name = "GPL-2.0", IsFoss = true, Url = "https://www.gnu.org/licenses/old-licenses/gpl-2.0.html" },
		new() { Id = 5, Name = "GPL-3.0", IsFoss = true, Url = "https://www.gnu.org/licenses/gpl-3.0.html" },
		new() { Id = 6, Name = "LGPL-2.1", IsFoss = true, Url = "https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html" },
		new() { Id = 7, Name = "LGPL-3.0", IsFoss = true, Url = "https://www.gnu.org/licenses/lgpl-3.0.html" },
		new() { Id = 8, Name = "AGPL-3.0", IsFoss = true, Url = "https://www.gnu.org/licenses/agpl-3.0.html" },
		new() { Id = 9, Name = "BSD-2-Clause", IsFoss = true, Url = "https://opensource.org/licenses/BSD-2-Clause" },
		new() { Id = 10, Name = "BSD-3-Clause", IsFoss = true, Url = "https://opensource.org/licenses/BSD-3-Clause" },
		new() { Id = 11, Name = "CC0-1.0", IsFoss = true, Url = "https://creativecommons.org/publicdomain/zero/1.0/" },
		new() { Id = 12, Name = "CC-BY-3.0", IsFoss = true, Url = "https://creativecommons.org/licenses/by/3.0/" },
		new() { Id = 13, Name = "CC-BY-4.0", IsFoss = true, Url = "https://creativecommons.org/licenses/by/4.0/" },
		new() { Id = 14, Name = "CC-BY-SA-3.0", IsFoss = true, Url = "https://creativecommons.org/licenses/by-sa/3.0/" },
		new() { Id = 15, Name = "CC-BY-SA-4.0", IsFoss = true, Url = "https://creativecommons.org/licenses/by-sa/4.0/" },
	];

	protected override void OnModelCreating(ModelBuilder b)
	{
		base.OnModelCreating(b);

		// OpenIddict(本站作 OAuth2 提供方)所需的实体集
		b.UseOpenIddict();

		// 所有枚举以字符串存储
		var enumConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.EnumToStringConverter<PackageType>();

		b.Entity<User>(e =>
		{
			e.ToTable("user");
			e.HasKey(x => x.Id);
			e.HasIndex(x => x.Username).IsUnique();
			e.Property(x => x.Username).HasMaxLength(100);
			e.Property(x => x.Rank).HasConversion<string>().HasMaxLength(20);
			e.HasIndex(x => new { x.OidcIssuer, x.OidcSubject });
			e.Property(x => x.DefaultServerUsername).HasMaxLength(20);
			e.HasMany(x => x.Packages).WithOne(p => p.Author).HasForeignKey(p => p.AuthorId);
			e.HasMany(x => x.MaintainedPackages).WithMany(p => p.Maintainers)
				.UsingEntity("maintainers");
		});

		b.Entity<License>(e =>
		{
			e.ToTable("license");
			e.HasKey(x => x.Id);
			e.HasIndex(x => x.Name).IsUnique();
			e.Property(x => x.Name).HasMaxLength(50);
			// 播种标准许可证,与 InitialApp 迁移中的 InsertData 保持一致。
			// id=1 固定为 "Other",对齐 Package.LicenseId/MediaLicenseId 默认值,避免悬空 FK。
			e.HasData(SeedLicenses);
		});

		b.Entity<Tag>(e =>
		{
			e.ToTable("tag");
			e.HasKey(x => x.Id);
			e.HasIndex(x => x.Name).IsUnique();
			e.HasMany(x => x.Packages).WithMany(p => p.Tags).UsingEntity("tags");
		});

		b.Entity<ContentWarning>(e =>
		{
			e.ToTable("content_warning");
			e.HasKey(x => x.Id);
			e.HasIndex(x => x.Name).IsUnique();
			e.HasMany(x => x.Packages).WithMany(p => p.ContentWarnings).UsingEntity("content_warnings");
		});

		b.Entity<MetaPackage>(e =>
		{
			e.ToTable("meta_package");
			e.HasKey(x => x.Id);
			e.HasIndex(x => x.Name).IsUnique();
			e.HasMany(x => x.Packages).WithMany(p => p.Provides).UsingEntity("provides");
		});

		b.Entity<LuantiRelease>(e =>
		{
			e.ToTable("luanti_release");
			e.HasKey(x => x.Id);
			e.HasIndex(x => x.Name).IsUnique();
		});

		b.Entity<Language>(e =>
		{
			e.ToTable("language");
			e.HasKey(x => x.Id);
			e.Property(x => x.Id).HasMaxLength(10);
		});

		b.Entity<Package>(e =>
		{
			e.ToTable("package");
			e.HasKey(x => x.Id);
			e.HasIndex(x => new { x.AuthorId, x.Name }).IsUnique();
			e.Property(x => x.Name).HasMaxLength(100);
			e.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
			e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
			e.Property(x => x.DevState).HasConversion<string>().HasMaxLength(30);
			e.Property(x => x.AiDisclosure).HasConversion<string>().HasMaxLength(20);
			e.HasOne(x => x.License).WithMany(l => l.Packages).HasForeignKey(x => x.LicenseId);
			e.HasOne(x => x.MediaLicense).WithMany().HasForeignKey(x => x.MediaLicenseId);
			e.HasOne(x => x.ReviewThread).WithMany().HasForeignKey(x => x.ReviewThreadId);
			e.HasOne(x => x.CoverImage).WithMany().HasForeignKey(x => x.CoverImageId);
			e.HasMany(x => x.Releases).WithOne(r => r.Package).HasForeignKey(r => r.PackageId);
			e.HasMany(x => x.Screenshots).WithOne(s => s.Package).HasForeignKey(s => s.PackageId);
			e.HasMany(x => x.Dependencies).WithOne(d => d.Depender).HasForeignKey(d => d.DependerId);
			e.HasMany(x => x.Threads).WithOne(t => t.Package!).HasForeignKey(t => t.PackageId);
			e.HasMany(x => x.Reviews).WithOne(r => r.Package).HasForeignKey(r => r.PackageId);
			e.HasMany(x => x.Aliases).WithOne(a => a.Package).HasForeignKey(a => a.PackageId);
			e.HasMany(x => x.Translations).WithOne(t => t.Package).HasForeignKey(t => t.PackageId);
			e.HasOne(x => x.UpdateConfig).WithOne(u => u.Package).HasForeignKey<PackageUpdateConfig>(u => u.PackageId);
		});

		b.Entity<Dependency>(e =>
		{
			e.ToTable("dependency");
			e.HasKey(x => x.Id);
			e.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
			e.HasOne(x => x.MetaPackage).WithMany(m => m.Dependencies).HasForeignKey(x => x.MetaPackageId);
			e.HasIndex(x => new { x.DependerId, x.PackageId, x.MetaPackageId }).IsUnique();
		});

		b.Entity<PackageGameSupport>(e =>
		{
			e.ToTable("package_game_support");
			e.HasKey(x => x.Id);
			e.HasOne(x => x.Package).WithMany(p => p.SupportedGames).HasForeignKey(x => x.PackageId);
			e.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId);
			e.HasIndex(x => new { x.GameId, x.PackageId }).IsUnique();
		});

		b.Entity<PackageTranslation>(e =>
		{
			e.ToTable("package_translation");
			e.HasKey(x => new { x.PackageId, x.LanguageId });
			e.HasOne(x => x.Language).WithMany().HasForeignKey(x => x.LanguageId);
		});

		b.Entity<PackageAlias>(e =>
		{
			e.ToTable("package_alias");
			e.HasKey(x => x.Id);
		});

		b.Entity<PackageRelease>(e =>
		{
			e.ToTable("package_release");
			e.HasKey(x => x.Id);
			e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
			e.Property(x => x.Url).HasMaxLength(300);
			e.HasOne(x => x.MinRel).WithMany().HasForeignKey(x => x.MinRelId);
			e.HasOne(x => x.MaxRel).WithMany().HasForeignKey(x => x.MaxRelId);
		});

		b.Entity<PackageScreenshot>(e =>
		{
			e.ToTable("package_screenshot");
			e.HasKey(x => x.Id);
			e.Property(x => x.Url).HasMaxLength(200);
		});

		b.Entity<PackageUpdateConfig>(e =>
		{
			e.ToTable("package_update_config");
			e.HasKey(x => x.PackageId);
			e.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(10);
		});

		b.Entity<PackageDailyStats>(e =>
		{
			e.ToTable("package_daily_stats");
			e.HasKey(x => new { x.PackageId, x.Date });
			e.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
		});

		b.Entity<Thread>(e =>
		{
			e.ToTable("thread");
			e.HasKey(x => x.Id);
			e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
			e.HasOne(x => x.Review).WithMany().HasForeignKey(x => x.ReviewId);
			e.HasMany(x => x.Replies).WithOne(r => r.Thread).HasForeignKey(r => r.ThreadId);
			e.HasMany(x => x.Watchers).WithMany().UsingEntity("thread_watchers");
		});

		b.Entity<ThreadReply>(e =>
		{
			e.ToTable("thread_reply");
			e.HasKey(x => x.Id);
			e.Property(x => x.Comment).HasMaxLength(2000);
			e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
		});

		b.Entity<PackageReview>(e =>
		{
			e.ToTable("package_review");
			e.HasKey(x => x.Id);
			e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
			e.HasOne(x => x.Thread).WithMany().HasForeignKey(x => x.ThreadId);
			e.HasMany(x => x.ReviewVotes).WithOne(v => v.Review).HasForeignKey(v => v.ReviewId);
		});

		b.Entity<PackageReviewVote>(e =>
		{
			e.ToTable("package_review_vote");
			e.HasKey(x => new { x.ReviewId, x.UserId });
			e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
		});

		b.Entity<Collection>(e =>
		{
			e.ToTable("collection");
			e.HasKey(x => x.Id);
			e.HasIndex(x => new { x.AuthorId, x.Name }).IsUnique();
			e.HasOne(x => x.Author).WithMany(u => u.Collections).HasForeignKey(x => x.AuthorId);
			e.HasMany(x => x.Items).WithOne(i => i.Collection).HasForeignKey(i => i.CollectionId);
		});

		b.Entity<CollectionPackage>(e =>
		{
			e.ToTable("collection_package");
			e.HasKey(x => new { x.PackageId, x.CollectionId });
			e.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
		});

		b.Entity<APIToken>(e =>
		{
			e.ToTable("api_token");
			e.HasKey(x => x.Id);
			e.HasIndex(x => x.AccessToken).IsUnique();
			e.Property(x => x.AccessToken).HasMaxLength(100);
			e.HasOne(x => x.Owner).WithMany(u => u.Tokens).HasForeignKey(x => x.OwnerId);
			e.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
			e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId);
		});

		b.Entity<OAuthClient>(e =>
		{
			e.ToTable("oauth_client");
			e.HasKey(x => x.Id);
			e.Property(x => x.Id).HasMaxLength(32);
			e.HasOne(x => x.Owner).WithMany(u => u.OAuthClients).HasForeignKey(x => x.OwnerId);
		});

		b.Entity<Notification>(e =>
		{
			e.ToTable("notification");
			e.HasKey(x => x.Id);
			e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
			e.HasOne(x => x.User).WithMany(u => u.Notifications).HasForeignKey(x => x.UserId);
			e.HasOne(x => x.Causer).WithMany().HasForeignKey(x => x.CauserId);
			e.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
		});

		b.Entity<AuditLogEntry>(e =>
		{
			e.ToTable("audit_log_entry");
			e.HasKey(x => x.Id);
			e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
			e.HasOne(x => x.Causer).WithMany().HasForeignKey(x => x.CauserId);
			e.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
		});

		b.Entity<ForumTopic>(e =>
		{
			e.ToTable("forum_topic");
			e.HasKey(x => x.TopicId);
			e.Property(x => x.TopicId).ValueGeneratedNever();
			e.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
			e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
		});

		// ---- 云同步:设备配对 / 服务器账号保管库 ----

		b.Entity<PairedDevice>(e =>
		{
			e.ToTable("paired_device");
			e.HasKey(x => x.Id);
			e.HasIndex(x => x.TokenHash).IsUnique();
			e.Property(x => x.Name).HasMaxLength(60);
			e.Property(x => x.TokenHash).HasMaxLength(64);
			e.Property(x => x.TokenPrefix).HasMaxLength(8);
			e.Property(x => x.CurrentServerAddress).HasMaxLength(255);
			e.HasIndex(x => new { x.UserId, x.PresenceUpdatedAt });
			e.HasOne(x => x.User).WithMany(u => u.PairedDevices).HasForeignKey(x => x.UserId);
		});

		b.Entity<DevicePairing>(e =>
		{
			e.ToTable("device_pairing");
			e.HasKey(x => x.Id);
			e.Property(x => x.Id).HasMaxLength(36).ValueGeneratedNever();
			e.HasIndex(x => x.Code).IsUnique();
			e.Property(x => x.Code).HasMaxLength(9);
			e.Property(x => x.DeviceName).HasMaxLength(60);
			e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
			e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId);
		});

		b.Entity<ServerCredential>(e =>
		{
			e.ToTable("server_credential");
			e.HasKey(x => x.Id);
			e.HasIndex(x => new { x.UserId, x.Address }).IsUnique();
			e.Property(x => x.Address).HasMaxLength(255);
			e.Property(x => x.Username).HasMaxLength(20);
			e.Property(x => x.PasswordEncrypted).HasMaxLength(1000);
			e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
			e.HasOne(x => x.User).WithMany(u => u.ServerCredentials).HasForeignKey(x => x.UserId);
			// 删除角色时保留各服务器凭证,仅清空来源标记
			e.HasOne(x => x.Character).WithMany().HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.SetNull);
		});

		b.Entity<PlayerCharacter>(e =>
		{
			e.ToTable("player_character");
			e.HasKey(x => x.Id);
			e.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
			e.Property(x => x.Name).HasMaxLength(20);
			e.Property(x => x.PasswordType).HasConversion<string>().HasMaxLength(20);
			e.Property(x => x.FixedPasswordEncrypted).HasMaxLength(1000);
			e.Property(x => x.PasswordListEncrypted).HasMaxLength(20000);
			e.Property(x => x.SkinUrl).HasMaxLength(300);
			e.HasOne(x => x.User).WithMany(u => u.Characters).HasForeignKey(x => x.UserId);
		});

		b.Entity<FriendLink>(e =>
		{
			e.ToTable("friend_link");
			e.HasKey(x => x.Id);
			e.HasIndex(x => new { x.RequesterId, x.AddresseeId }).IsUnique();
			e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
			e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Cascade);
			e.HasOne(x => x.Addressee).WithMany().HasForeignKey(x => x.AddresseeId).OnDelete(DeleteBehavior.Cascade);
		});

		// ---- 联机:服务器大厅 / 组队房间 ----

		b.Entity<GameServer>(e =>
		{
			e.ToTable("game_server");
			e.HasKey(x => x.Id);
			e.HasIndex(x => x.Address).IsUnique();
			e.Property(x => x.Address).HasMaxLength(255);
			e.Property(x => x.Name).HasMaxLength(60);
			e.Property(x => x.Description).HasMaxLength(500);
			e.Property(x => x.WebsiteUrl).HasMaxLength(300);
			e.Property(x => x.Motd).HasMaxLength(200);
			e.Property(x => x.ReportTokenHash).HasMaxLength(64);
			e.Property(x => x.ReportTokenPrefix).HasMaxLength(8);
			e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId);
		});

		b.Entity<Party>(e =>
		{
			e.ToTable("party");
			e.HasKey(x => x.Id);
			e.HasIndex(x => x.Code).IsUnique();
			e.Property(x => x.Code).HasMaxLength(6);
			e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
			e.Property(x => x.ServerAddress).HasMaxLength(255);
			e.HasOne(x => x.Leader).WithMany().HasForeignKey(x => x.LeaderId);
			e.HasMany(x => x.Members).WithOne(m => m.Party).HasForeignKey(m => m.PartyId);
		});

		b.Entity<PartyMember>(e =>
		{
			e.ToTable("party_member");
			e.HasKey(x => x.Id);
			e.HasIndex(x => new { x.PartyId, x.UserId }).IsUnique();
			e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
		});

		// ---- 实时通讯:好友私聊 ----

		b.Entity<DirectMessage>(e =>
		{
			e.ToTable("direct_message");
			e.HasKey(x => x.Id);
			e.Property(x => x.Body).HasMaxLength(2000);
			e.HasIndex(x => new { x.RecipientId, x.ReadAt });
			e.HasIndex(x => new { x.SenderId, x.RecipientId, x.Id });
			e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
			e.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Cascade);
		});
	}
}

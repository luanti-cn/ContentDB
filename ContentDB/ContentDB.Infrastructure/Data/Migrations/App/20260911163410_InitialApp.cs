using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ContentDB.Infrastructure.Data.Migrations.App
{
    /// <inheritdoc />
    public partial class InitialApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_warning",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_warning", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "language",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_language", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "license",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsFoss = table.Column<bool>(type: "boolean", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "luanti_release",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Protocol = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_luanti_release", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "meta_package",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meta_package", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tag",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    BackgroundColor = table.Column<string>(type: "text", nullable: false),
                    TextColor = table.Column<string>(type: "text", nullable: false),
                    Views = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Rank = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OidcIssuer = table.Column<string>(type: "text", nullable: true),
                    OidcSubject = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    EmailConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Locale = table.Column<string>(type: "text", nullable: true),
                    ProfilePicUrl = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    WebsiteUrl = table.Column<string>(type: "text", nullable: true),
                    DonateUrl = table.Column<string>(type: "text", nullable: true),
                    GithubUsername = table.Column<string>(type: "text", nullable: true),
                    ForumsUsername = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "collection",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AuthorId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    ShortDescription = table.Column<string>(type: "text", nullable: false),
                    LongDescription = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Private = table.Column<bool>(type: "boolean", nullable: false),
                    Pinned = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collection_user_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "forum_topic",
                columns: table => new
                {
                    TopicId = table.Column<int>(type: "integer", nullable: false),
                    AuthorId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Views = table.Column<int>(type: "integer", nullable: false),
                    Wip = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forum_topic", x => x.TopicId);
                    table.ForeignKey(
                        name: "FK_forum_topic_user_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oauth_client",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: false),
                    RedirectUrl = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    IsClientSide = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_client", x => x.Id);
                    table.ForeignKey(
                        name: "FK_oauth_client_user_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "api_token",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AccessToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    PackageId = table.Column<int>(type: "integer", nullable: true),
                    ClientId = table.Column<string>(type: "character varying(32)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_token", x => x.Id);
                    table.ForeignKey(
                        name: "FK_api_token_oauth_client_ClientId",
                        column: x => x.ClientId,
                        principalTable: "oauth_client",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_api_token_user_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_log_entry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CauserId = table.Column<int>(type: "integer", nullable: true),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: true),
                    PackageId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log_entry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_log_entry_user_CauserId",
                        column: x => x.CauserId,
                        principalTable: "user",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "collection_package",
                columns: table => new
                {
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    CollectionId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_package", x => new { x.PackageId, x.CollectionId });
                    table.ForeignKey(
                        name: "FK_collection_package_collection_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "collection",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_warnings",
                columns: table => new
                {
                    ContentWarningsId = table.Column<int>(type: "integer", nullable: false),
                    PackagesId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_warnings", x => new { x.ContentWarningsId, x.PackagesId });
                    table.ForeignKey(
                        name: "FK_content_warnings_content_warning_ContentWarningsId",
                        column: x => x.ContentWarningsId,
                        principalTable: "content_warning",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dependency",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DependerId = table.Column<int>(type: "integer", nullable: false),
                    PackageId = table.Column<int>(type: "integer", nullable: true),
                    MetaPackageId = table.Column<int>(type: "integer", nullable: true),
                    Optional = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dependency", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dependency_meta_package_MetaPackageId",
                        column: x => x.MetaPackageId,
                        principalTable: "meta_package",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "maintainers",
                columns: table => new
                {
                    MaintainedPackagesId = table.Column<int>(type: "integer", nullable: false),
                    MaintainersId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintainers", x => new { x.MaintainedPackagesId, x.MaintainersId });
                    table.ForeignKey(
                        name: "FK_maintainers_user_MaintainersId",
                        column: x => x.MaintainersId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CauserId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    PackageId = table.Column<int>(type: "integer", nullable: true),
                    Read = table.Column<bool>(type: "boolean", nullable: false),
                    Emailed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_user_CauserId",
                        column: x => x.CauserId,
                        principalTable: "user",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_notification_user_UserId",
                        column: x => x.UserId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AuthorId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    ShortDesc = table.Column<string>(type: "text", nullable: false),
                    Desc = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LicenseId = table.Column<int>(type: "integer", nullable: false),
                    MediaLicenseId = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DevState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AiDisclosure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    ScoreDownloads = table.Column<double>(type: "double precision", nullable: false),
                    Downloads = table.Column<int>(type: "integer", nullable: false),
                    ReviewThreadId = table.Column<int>(type: "integer", nullable: true),
                    SupportsAllGames = table.Column<bool>(type: "boolean", nullable: false),
                    SensitivePackage = table.Column<bool>(type: "boolean", nullable: false),
                    Repo = table.Column<string>(type: "text", nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    IssueTracker = table.Column<string>(type: "text", nullable: true),
                    Forums = table.Column<int>(type: "integer", nullable: true),
                    VideoUrl = table.Column<string>(type: "text", nullable: true),
                    DonateUrl = table.Column<string>(type: "text", nullable: true),
                    TranslationUrl = table.Column<string>(type: "text", nullable: true),
                    InsecureEnvJustification = table.Column<string>(type: "text", nullable: true),
                    HttpApiJustification = table.Column<string>(type: "text", nullable: true),
                    EnableGameSupportDetection = table.Column<bool>(type: "boolean", nullable: false),
                    CoverImageId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_license_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "license",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_package_license_MediaLicenseId",
                        column: x => x.MediaLicenseId,
                        principalTable: "license",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_package_user_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_alias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    Author = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_alias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_alias_package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_daily_stats",
                columns: table => new
                {
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PlatformMinetest = table.Column<int>(type: "integer", nullable: false),
                    PlatformOther = table.Column<int>(type: "integer", nullable: false),
                    ReasonNew = table.Column<int>(type: "integer", nullable: false),
                    ReasonDependency = table.Column<int>(type: "integer", nullable: false),
                    ReasonUpdate = table.Column<int>(type: "integer", nullable: false),
                    DownloadsV510 = table.Column<int>(type: "integer", nullable: false),
                    ViewsLuanti = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_daily_stats", x => new { x.PackageId, x.Date });
                    table.ForeignKey(
                        name: "FK_package_daily_stats_package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_game_support",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Supports = table.Column<bool>(type: "boolean", nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_game_support", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_game_support_package_GameId",
                        column: x => x.GameId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_package_game_support_package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_release",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TaskId = table.Column<string>(type: "text", nullable: true),
                    CommitHash = table.Column<string>(type: "text", nullable: true),
                    Downloads = table.Column<int>(type: "integer", nullable: false),
                    ReleaseNotes = table.Column<string>(type: "text", nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UsesInsecureEnv = table.Column<bool>(type: "boolean", nullable: true),
                    UsesHttpApi = table.Column<bool>(type: "boolean", nullable: true),
                    MinRelId = table.Column<int>(type: "integer", nullable: true),
                    MaxRelId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_release", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_release_luanti_release_MaxRelId",
                        column: x => x.MaxRelId,
                        principalTable: "luanti_release",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_package_release_luanti_release_MinRelId",
                        column: x => x.MinRelId,
                        principalTable: "luanti_release",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_package_release_package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_screenshot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_screenshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_screenshot_package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_translation",
                columns: table => new
                {
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    LanguageId = table.Column<string>(type: "character varying(10)", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    ShortDesc = table.Column<string>(type: "text", nullable: true),
                    Desc = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_translation", x => new { x.PackageId, x.LanguageId });
                    table.ForeignKey(
                        name: "FK_package_translation_language_LanguageId",
                        column: x => x.LanguageId,
                        principalTable: "language",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_package_translation_package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_update_config",
                columns: table => new
                {
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    LastCommit = table.Column<string>(type: "text", nullable: true),
                    LastTag = table.Column<string>(type: "text", nullable: true),
                    OutdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TaskId = table.Column<string>(type: "text", nullable: true),
                    Trigger = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Ref = table.Column<string>(type: "text", nullable: true),
                    MakeRelease = table.Column<bool>(type: "boolean", nullable: false),
                    AutoCreated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_update_config", x => x.PackageId);
                    table.ForeignKey(
                        name: "FK_package_update_config_package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provides",
                columns: table => new
                {
                    PackagesId = table.Column<int>(type: "integer", nullable: false),
                    ProvidesId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provides", x => new { x.PackagesId, x.ProvidesId });
                    table.ForeignKey(
                        name: "FK_provides_meta_package_ProvidesId",
                        column: x => x.ProvidesId,
                        principalTable: "meta_package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_provides_package_PackagesId",
                        column: x => x.PackagesId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    PackagesId = table.Column<int>(type: "integer", nullable: false),
                    TagsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => new { x.PackagesId, x.TagsId });
                    table.ForeignKey(
                        name: "FK_tags_package_PackagesId",
                        column: x => x.PackagesId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tags_tag_TagsId",
                        column: x => x.TagsId,
                        principalTable: "tag",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_review",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    AuthorId = table.Column<int>(type: "integer", nullable: false),
                    LanguageId = table.Column<string>(type: "text", nullable: true),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    ThreadId = table.Column<int>(type: "integer", nullable: true),
                    Votes = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_review", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_review_package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "package",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_package_review_user_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_review_vote",
                columns: table => new
                {
                    ReviewId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    IsPositive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_review_vote", x => new { x.ReviewId, x.UserId });
                    table.ForeignKey(
                        name: "FK_package_review_vote_package_review_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "package_review",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_package_review_vote_user_UserId",
                        column: x => x.UserId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thread",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageId = table.Column<int>(type: "integer", nullable: true),
                    ReviewId = table.Column<int>(type: "integer", nullable: true),
                    AuthorId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Private = table.Column<bool>(type: "boolean", nullable: false),
                    Locked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thread", x => x.Id);
                    table.ForeignKey(
                        name: "FK_thread_package_PackageId",
                        column: x => x.PackageId,
                        principalTable: "package",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_thread_package_review_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "package_review",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_thread_user_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thread_reply",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ThreadId = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AuthorId = table.Column<int>(type: "integer", nullable: false),
                    IsStatusUpdate = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thread_reply", x => x.Id);
                    table.ForeignKey(
                        name: "FK_thread_reply_thread_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "thread",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_thread_reply_user_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thread_watchers",
                columns: table => new
                {
                    ThreadId = table.Column<int>(type: "integer", nullable: false),
                    WatchersId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thread_watchers", x => new { x.ThreadId, x.WatchersId });
                    table.ForeignKey(
                        name: "FK_thread_watchers_thread_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "thread",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_thread_watchers_user_WatchersId",
                        column: x => x.WatchersId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_token_AccessToken",
                table: "api_token",
                column: "AccessToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_token_ClientId",
                table: "api_token",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_api_token_OwnerId",
                table: "api_token",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_api_token_PackageId",
                table: "api_token",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_entry_CauserId",
                table: "audit_log_entry",
                column: "CauserId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_entry_PackageId",
                table: "audit_log_entry",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_collection_AuthorId_Name",
                table: "collection",
                columns: new[] { "AuthorId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_package_CollectionId",
                table: "collection_package",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_content_warning_Name",
                table: "content_warning",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_warnings_PackagesId",
                table: "content_warnings",
                column: "PackagesId");

            migrationBuilder.CreateIndex(
                name: "IX_dependency_DependerId_PackageId_MetaPackageId",
                table: "dependency",
                columns: new[] { "DependerId", "PackageId", "MetaPackageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dependency_MetaPackageId",
                table: "dependency",
                column: "MetaPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_dependency_PackageId",
                table: "dependency",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_forum_topic_AuthorId",
                table: "forum_topic",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_license_Name",
                table: "license",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_luanti_release_Name",
                table: "luanti_release",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintainers_MaintainersId",
                table: "maintainers",
                column: "MaintainersId");

            migrationBuilder.CreateIndex(
                name: "IX_meta_package_Name",
                table: "meta_package",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_CauserId",
                table: "notification",
                column: "CauserId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_PackageId",
                table: "notification",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_UserId",
                table: "notification",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_oauth_client_OwnerId",
                table: "oauth_client",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_package_AuthorId_Name",
                table: "package",
                columns: new[] { "AuthorId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_package_CoverImageId",
                table: "package",
                column: "CoverImageId");

            migrationBuilder.CreateIndex(
                name: "IX_package_LicenseId",
                table: "package",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_package_MediaLicenseId",
                table: "package",
                column: "MediaLicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_package_ReviewThreadId",
                table: "package",
                column: "ReviewThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_package_alias_PackageId",
                table: "package_alias",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_package_game_support_GameId_PackageId",
                table: "package_game_support",
                columns: new[] { "GameId", "PackageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_package_game_support_PackageId",
                table: "package_game_support",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_package_release_MaxRelId",
                table: "package_release",
                column: "MaxRelId");

            migrationBuilder.CreateIndex(
                name: "IX_package_release_MinRelId",
                table: "package_release",
                column: "MinRelId");

            migrationBuilder.CreateIndex(
                name: "IX_package_release_PackageId",
                table: "package_release",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_package_review_AuthorId",
                table: "package_review",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_package_review_PackageId",
                table: "package_review",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_package_review_ThreadId",
                table: "package_review",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_package_review_vote_UserId",
                table: "package_review_vote",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_package_screenshot_PackageId",
                table: "package_screenshot",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_package_translation_LanguageId",
                table: "package_translation",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_provides_ProvidesId",
                table: "provides",
                column: "ProvidesId");

            migrationBuilder.CreateIndex(
                name: "IX_tag_Name",
                table: "tag",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tags_TagsId",
                table: "tags",
                column: "TagsId");

            migrationBuilder.CreateIndex(
                name: "IX_thread_AuthorId",
                table: "thread",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_thread_PackageId",
                table: "thread",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_thread_ReviewId",
                table: "thread",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_thread_reply_AuthorId",
                table: "thread_reply",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_thread_reply_ThreadId",
                table: "thread_reply",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_thread_watchers_WatchersId",
                table: "thread_watchers",
                column: "WatchersId");

            migrationBuilder.CreateIndex(
                name: "IX_user_OidcIssuer_OidcSubject",
                table: "user",
                columns: new[] { "OidcIssuer", "OidcSubject" });

            migrationBuilder.CreateIndex(
                name: "IX_user_Username",
                table: "user",
                column: "Username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_api_token_package_PackageId",
                table: "api_token",
                column: "PackageId",
                principalTable: "package",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_entry_package_PackageId",
                table: "audit_log_entry",
                column: "PackageId",
                principalTable: "package",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_collection_package_package_PackageId",
                table: "collection_package",
                column: "PackageId",
                principalTable: "package",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_content_warnings_package_PackagesId",
                table: "content_warnings",
                column: "PackagesId",
                principalTable: "package",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_dependency_package_DependerId",
                table: "dependency",
                column: "DependerId",
                principalTable: "package",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_dependency_package_PackageId",
                table: "dependency",
                column: "PackageId",
                principalTable: "package",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_maintainers_package_MaintainedPackagesId",
                table: "maintainers",
                column: "MaintainedPackagesId",
                principalTable: "package",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_notification_package_PackageId",
                table: "notification",
                column: "PackageId",
                principalTable: "package",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_package_package_screenshot_CoverImageId",
                table: "package",
                column: "CoverImageId",
                principalTable: "package_screenshot",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_package_thread_ReviewThreadId",
                table: "package",
                column: "ReviewThreadId",
                principalTable: "thread",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_package_review_thread_ThreadId",
                table: "package_review",
                column: "ThreadId",
                principalTable: "thread",
                principalColumn: "Id");

            // 播种标准许可证。id=1 固定为 "Other",与 Package.LicenseId/MediaLicenseId 的
            // 默认值(=1)对齐,避免空库下新建包时悬空 FK(FK_package_license_LicenseId)。
            // Id 列使用 IdentityByDefaultColumn,允许显式指定 id。
            migrationBuilder.InsertData(
                table: "license",
                columns: new[] { "Id", "Name", "IsFoss", "Url" },
                values: new object[,]
                {
                    { 1, "Other", false, null },
                    { 2, "MIT", true, "https://opensource.org/licenses/MIT" },
                    { 3, "Apache-2.0", true, "https://www.apache.org/licenses/LICENSE-2.0" },
                    { 4, "GPL-2.0", true, "https://www.gnu.org/licenses/old-licenses/gpl-2.0.html" },
                    { 5, "GPL-3.0", true, "https://www.gnu.org/licenses/gpl-3.0.html" },
                    { 6, "LGPL-2.1", true, "https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html" },
                    { 7, "LGPL-3.0", true, "https://www.gnu.org/licenses/lgpl-3.0.html" },
                    { 8, "AGPL-3.0", true, "https://www.gnu.org/licenses/agpl-3.0.html" },
                    { 9, "BSD-2-Clause", true, "https://opensource.org/licenses/BSD-2-Clause" },
                    { 10, "BSD-3-Clause", true, "https://opensource.org/licenses/BSD-3-Clause" },
                    { 11, "CC0-1.0", true, "https://creativecommons.org/publicdomain/zero/1.0/" },
                    { 12, "CC-BY-3.0", true, "https://creativecommons.org/licenses/by/3.0/" },
                    { 13, "CC-BY-4.0", true, "https://creativecommons.org/licenses/by/4.0/" },
                    { 14, "CC-BY-SA-3.0", true, "https://creativecommons.org/licenses/by-sa/3.0/" },
                    { 15, "CC-BY-SA-4.0", true, "https://creativecommons.org/licenses/by-sa/4.0/" },
                });

            // 播种后,把 identity 序列推进到 MAX(Id) 之后,避免后续自增插入撞已用 id。
            migrationBuilder.Sql(
                "SELECT setval(pg_get_serial_sequence('license', 'Id'), (SELECT MAX(\"Id\") FROM license));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_package_review_package_PackageId",
                table: "package_review");

            migrationBuilder.DropForeignKey(
                name: "FK_package_screenshot_package_PackageId",
                table: "package_screenshot");

            migrationBuilder.DropForeignKey(
                name: "FK_thread_package_PackageId",
                table: "thread");

            migrationBuilder.DropForeignKey(
                name: "FK_package_review_user_AuthorId",
                table: "package_review");

            migrationBuilder.DropForeignKey(
                name: "FK_thread_user_AuthorId",
                table: "thread");

            migrationBuilder.DropForeignKey(
                name: "FK_package_review_thread_ThreadId",
                table: "package_review");

            migrationBuilder.DropTable(
                name: "api_token");

            migrationBuilder.DropTable(
                name: "audit_log_entry");

            migrationBuilder.DropTable(
                name: "collection_package");

            migrationBuilder.DropTable(
                name: "content_warnings");

            migrationBuilder.DropTable(
                name: "dependency");

            migrationBuilder.DropTable(
                name: "forum_topic");

            migrationBuilder.DropTable(
                name: "maintainers");

            migrationBuilder.DropTable(
                name: "notification");

            migrationBuilder.DropTable(
                name: "package_alias");

            migrationBuilder.DropTable(
                name: "package_daily_stats");

            migrationBuilder.DropTable(
                name: "package_game_support");

            migrationBuilder.DropTable(
                name: "package_release");

            migrationBuilder.DropTable(
                name: "package_review_vote");

            migrationBuilder.DropTable(
                name: "package_translation");

            migrationBuilder.DropTable(
                name: "package_update_config");

            migrationBuilder.DropTable(
                name: "provides");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "thread_reply");

            migrationBuilder.DropTable(
                name: "thread_watchers");

            migrationBuilder.DropTable(
                name: "oauth_client");

            migrationBuilder.DropTable(
                name: "collection");

            migrationBuilder.DropTable(
                name: "content_warning");

            migrationBuilder.DropTable(
                name: "luanti_release");

            migrationBuilder.DropTable(
                name: "language");

            migrationBuilder.DropTable(
                name: "meta_package");

            migrationBuilder.DropTable(
                name: "tag");

            migrationBuilder.DropTable(
                name: "package");

            migrationBuilder.DropTable(
                name: "license");

            migrationBuilder.DropTable(
                name: "package_screenshot");

            migrationBuilder.DropTable(
                name: "user");

            migrationBuilder.DropTable(
                name: "thread");

            migrationBuilder.DropTable(
                name: "package_review");
        }
    }
}

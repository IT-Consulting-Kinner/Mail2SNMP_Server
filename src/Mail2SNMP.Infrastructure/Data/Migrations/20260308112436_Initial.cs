using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    Name = table.Column<string>(maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    DisplayName = table.Column<string>(nullable: false),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    LastLoginUtc = table.Column<DateTime>(nullable: true),
                    IsActive = table.Column<bool>(nullable: false),
                    UserName = table.Column<string>(maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(maxLength: 256, nullable: true),
                    Email = table.Column<string>(maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(nullable: false),
                    PasswordHash = table.Column<string>(nullable: true),
                    SecurityStamp = table.Column<string>(nullable: true),
                    ConcurrencyStamp = table.Column<string>(nullable: true),
                    PhoneNumber = table.Column<string>(nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(nullable: false),
                    TwoFactorEnabled = table.Column<bool>(nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(nullable: true),
                    LockoutEnabled = table.Column<bool>(nullable: false),
                    AccessFailedCount = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TimestampUtc = table.Column<DateTime>(nullable: false),
                    SchemaVersion = table.Column<int>(nullable: false),
                    ActorType = table.Column<int>(nullable: false),
                    ActorId = table.Column<string>(maxLength: 200, nullable: false),
                    Action = table.Column<string>(maxLength: 200, nullable: false),
                    TargetType = table.Column<string>(nullable: true),
                    TargetId = table.Column<string>(nullable: true),
                    Details = table.Column<string>(maxLength: 4096, nullable: true),
                    IpAddress = table.Column<string>(maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(maxLength: 100, nullable: true),
                    Result = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Mailboxes",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    Host = table.Column<string>(maxLength: 500, nullable: false),
                    Port = table.Column<int>(nullable: false),
                    UseSsl = table.Column<bool>(nullable: false),
                    Username = table.Column<string>(maxLength: 500, nullable: false),
                    EncryptedPassword = table.Column<string>(maxLength: 2000, nullable: false),
                    Folder = table.Column<string>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    LastCheckedUtc = table.Column<DateTime>(nullable: true),
                    LastError = table.Column<string>(nullable: true),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mailboxes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceWindows",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    StartUtc = table.Column<DateTime>(nullable: false),
                    EndUtc = table.Column<DateTime>(nullable: false),
                    Scope = table.Column<string>(maxLength: 500, nullable: false),
                    RecurringCron = table.Column<string>(nullable: true),
                    CreatedBy = table.Column<string>(maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceWindows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rules",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    Field = table.Column<int>(nullable: false),
                    MatchType = table.Column<int>(nullable: false),
                    Criteria = table.Column<string>(maxLength: 2000, nullable: false),
                    Severity = table.Column<int>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    Priority = table.Column<int>(nullable: false),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SnmpTargets",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    Host = table.Column<string>(maxLength: 500, nullable: false),
                    Port = table.Column<int>(nullable: false),
                    Version = table.Column<int>(nullable: false),
                    CommunityString = table.Column<string>(maxLength: 500, nullable: true),
                    SecurityName = table.Column<string>(maxLength: 200, nullable: true),
                    AuthProtocol = table.Column<int>(nullable: false),
                    EncryptedAuthPassword = table.Column<string>(maxLength: 2000, nullable: true),
                    PrivProtocol = table.Column<int>(nullable: false),
                    EncryptedPrivPassword = table.Column<string>(maxLength: 2000, nullable: true),
                    EngineId = table.Column<string>(maxLength: 200, nullable: true),
                    EnterpriseTrapOid = table.Column<string>(maxLength: 500, nullable: true),
                    MaxTrapsPerMinute = table.Column<int>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnmpTargets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookTargets",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    Url = table.Column<string>(maxLength: 2000, nullable: false),
                    Headers = table.Column<string>(nullable: true),
                    PayloadTemplate = table.Column<string>(nullable: true),
                    EncryptedSecret = table.Column<string>(maxLength: 2000, nullable: true),
                    MaxRequestsPerMinute = table.Column<int>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookTargets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkerLeases",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstanceId = table.Column<string>(maxLength: 100, nullable: false),
                    StartedUtc = table.Column<DateTime>(nullable: false),
                    LastHeartbeatUtc = table.Column<DateTime>(nullable: false),
                    LicenseEdition = table.Column<string>(nullable: false),
                    MachineName = table.Column<string>(maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerLeases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(nullable: false),
                    ClaimType = table.Column<string>(nullable: true),
                    ClaimValue = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(nullable: false),
                    ClaimType = table.Column<string>(nullable: true),
                    ClaimValue = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(nullable: false),
                    ProviderKey = table.Column<string>(nullable: false),
                    ProviderDisplayName = table.Column<string>(nullable: true),
                    UserId = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(nullable: false),
                    RoleId = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(nullable: false),
                    LoginProvider = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    Value = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMails",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MailboxId = table.Column<int>(nullable: false),
                    MessageId = table.Column<string>(maxLength: 1000, nullable: false),
                    From = table.Column<string>(nullable: true),
                    Subject = table.Column<string>(nullable: true),
                    ReceivedUtc = table.Column<DateTime>(nullable: false),
                    ProcessedUtc = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessedMails_Mailboxes_MailboxId",
                        column: x => x.MailboxId,
                        principalTable: "Mailboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    MailboxId = table.Column<int>(nullable: false),
                    RuleId = table.Column<int>(nullable: false),
                    Channels = table.Column<string>(nullable: false),
                    TrapTemplate = table.Column<string>(nullable: true),
                    WebhookTemplate = table.Column<string>(nullable: true),
                    OidMapping = table.Column<string>(nullable: true),
                    MaxEventsPerHour = table.Column<int>(nullable: false),
                    MaxActiveEvents = table.Column<int>(nullable: false),
                    DedupWindowMinutes = table.Column<int>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Jobs_Mailboxes_MailboxId",
                        column: x => x.MailboxId,
                        principalTable: "Mailboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Jobs_Rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "Rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<int>(nullable: false),
                    State = table.Column<int>(nullable: false),
                    Severity = table.Column<int>(nullable: false),
                    MessageId = table.Column<string>(nullable: true),
                    MailFrom = table.Column<string>(maxLength: 500, nullable: true),
                    Subject = table.Column<string>(maxLength: 500, nullable: true),
                    RuleName = table.Column<string>(nullable: true),
                    HitCount = table.Column<int>(nullable: false),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    NotifiedUtc = table.Column<DateTime>(nullable: true),
                    AcknowledgedUtc = table.Column<DateTime>(nullable: true),
                    ResolvedUtc = table.Column<DateTime>(nullable: true),
                    LastStateChangeUtc = table.Column<DateTime>(nullable: true),
                    AcknowledgedBy = table.Column<string>(nullable: true),
                    ResolvedBy = table.Column<string>(nullable: true),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    JobId = table.Column<int>(nullable: false),
                    IntervalMinutes = table.Column<int>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    NextRunUtc = table.Column<DateTime>(nullable: true),
                    LastRunUtc = table.Column<DateTime>(nullable: true),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Schedules_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeadLetterEntries",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WebhookTargetId = table.Column<int>(nullable: false),
                    EventId = table.Column<long>(nullable: false),
                    PayloadJson = table.Column<string>(nullable: false),
                    LastError = table.Column<string>(nullable: true),
                    AttemptCount = table.Column<int>(nullable: false),
                    CreatedUtc = table.Column<DateTime>(nullable: false),
                    NextRetryUtc = table.Column<DateTime>(nullable: true),
                    LockedUntilUtc = table.Column<DateTime>(nullable: true),
                    LockedByInstanceId = table.Column<string>(maxLength: 100, nullable: true),
                    Status = table.Column<int>(nullable: false),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadLetterEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeadLetterEntries_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeadLetterEntries_WebhookTargets_WebhookTargetId",
                        column: x => x.WebhookTargetId,
                        principalTable: "WebhookTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventDedups",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DedupKeyHash = table.Column<string>(fixedLength: true, maxLength: 64, nullable: false),
                    JobId = table.Column<int>(nullable: false),
                    EventId = table.Column<long>(nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(nullable: false),
                    LastSeenUtc = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventDedups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventDedups_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventDedups_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Action",
                table: "AuditEvents",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TimestampUtc",
                table: "AuditEvents",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterEntries_EventId",
                table: "DeadLetterEntries",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterEntries_Status_LockedUntilUtc",
                table: "DeadLetterEntries",
                columns: new[] { "Status", "LockedUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterEntries_WebhookTargetId",
                table: "DeadLetterEntries",
                column: "WebhookTargetId");

            migrationBuilder.CreateIndex(
                name: "IX_EventDedups_DedupKeyHash_JobId",
                table: "EventDedups",
                columns: new[] { "DedupKeyHash", "JobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventDedups_EventId",
                table: "EventDedups",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventDedups_JobId",
                table: "EventDedups",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_EventDedups_LastSeenUtc",
                table: "EventDedups",
                column: "LastSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Events_JobId_State",
                table: "Events",
                columns: new[] { "JobId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_MailboxId",
                table: "Jobs",
                column: "MailboxId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_RuleId",
                table: "Jobs",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMails_MailboxId",
                table: "ProcessedMails",
                column: "MailboxId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMails_MessageId_MailboxId",
                table: "ProcessedMails",
                columns: new[] { "MessageId", "MailboxId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMails_ProcessedUtc",
                table: "ProcessedMails",
                column: "ProcessedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_JobId",
                table: "Schedules",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerLeases_InstanceId",
                table: "WorkerLeases",
                column: "InstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkerLeases_LastHeartbeatUtc",
                table: "WorkerLeases",
                column: "LastHeartbeatUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "DeadLetterEntries");

            migrationBuilder.DropTable(
                name: "EventDedups");

            migrationBuilder.DropTable(
                name: "MaintenanceWindows");

            migrationBuilder.DropTable(
                name: "ProcessedMails");

            migrationBuilder.DropTable(
                name: "Schedules");

            migrationBuilder.DropTable(
                name: "SnmpTargets");

            migrationBuilder.DropTable(
                name: "WorkerLeases");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "WebhookTargets");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "Mailboxes");

            migrationBuilder.DropTable(
                name: "Rules");
        }
    }
}

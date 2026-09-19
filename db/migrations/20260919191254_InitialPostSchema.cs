using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TalentBridge.Post.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "post");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "managers",
                schema: "post",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "citext", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    organization = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_managers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "post",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_postings",
                schema: "post",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    department = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    employment_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seniority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    openings = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    work_arrangement = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    location = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    salary_min = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    salary_max = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    salary_currency = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "CAD"),
                    pay_period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Annual"),
                    salary_visible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    responsibilities = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    skills = table.Column<string[]>(type: "text[]", nullable: false),
                    application_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    application_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    closing_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_postings", x => x.id);
                    table.CheckConstraint("ck_job_postings_employment_type", "employment_type in ('FullTime', 'PartTime', 'Contract', 'Temporary', 'Internship')");
                    table.CheckConstraint("ck_job_postings_openings", "openings > 0");
                    table.CheckConstraint("ck_job_postings_pay_period", "pay_period in ('Annual', 'Monthly', 'Hourly')");
                    table.CheckConstraint("ck_job_postings_salary_positive", "salary_min > 0");
                    table.CheckConstraint("ck_job_postings_salary_range", "salary_min < salary_max");
                    table.CheckConstraint("ck_job_postings_seniority", "seniority in ('Intern', 'Junior', 'Mid', 'Senior', 'Lead', 'Principal', 'Director', 'Executive')");
                    table.CheckConstraint("ck_job_postings_status", "status in ('Draft', 'Published', 'Closed')");
                    table.CheckConstraint("ck_job_postings_work_arrangement", "work_arrangement in ('OnSite', 'Hybrid', 'Remote')");
                    table.ForeignKey(
                        name: "fk_job_postings_managers_manager_id",
                        column: x => x.manager_id,
                        principalSchema: "post",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "post",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_managers_manager_id",
                        column: x => x.manager_id,
                        principalSchema: "post",
                        principalTable: "managers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_manager_id_created_at",
                schema: "post",
                table: "job_postings",
                columns: new[] { "manager_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_slug",
                schema: "post",
                table: "job_postings",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_managers_email",
                schema: "post",
                table: "managers",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "post",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_manager_id",
                schema: "post",
                table: "refresh_tokens",
                column: "manager_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "post",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_postings",
                schema: "post");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "post");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "post");

            migrationBuilder.DropTable(
                name: "managers",
                schema: "post");
        }
    }
}

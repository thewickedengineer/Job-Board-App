using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalentBridge.Post.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReferenceCodeUniquePerManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_job_postings_manager_id_reference_code",
                schema: "post",
                table: "job_postings",
                columns: new[] { "manager_id", "reference_code" },
                unique: true,
                filter: "reference_code is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_postings_manager_id_reference_code",
                schema: "post",
                table: "job_postings");
        }
    }
}

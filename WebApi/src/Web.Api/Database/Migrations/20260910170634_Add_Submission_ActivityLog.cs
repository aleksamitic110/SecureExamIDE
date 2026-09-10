using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class Add_Submission_ActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "size_bytes",
                schema: "public",
                table: "submissions",
                newName: "solution_size_bytes");

            migrationBuilder.RenameColumn(
                name: "sha256",
                schema: "public",
                table: "submissions",
                newName: "solution_sha256");

            migrationBuilder.AddColumn<string>(
                name: "activity_log_object_key",
                schema: "public",
                table: "submissions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "activity_log_sha256",
                schema: "public",
                table: "submissions",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "activity_log_size_bytes",
                schema: "public",
                table: "submissions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "ix_submissions_activity_log_object_key",
                schema: "public",
                table: "submissions",
                column: "activity_log_object_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_submissions_activity_log_object_key",
                schema: "public",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "activity_log_object_key",
                schema: "public",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "activity_log_sha256",
                schema: "public",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "activity_log_size_bytes",
                schema: "public",
                table: "submissions");

            migrationBuilder.RenameColumn(
                name: "solution_size_bytes",
                schema: "public",
                table: "submissions",
                newName: "size_bytes");

            migrationBuilder.RenameColumn(
                name: "solution_sha256",
                schema: "public",
                table: "submissions",
                newName: "sha256");
        }
    }
}

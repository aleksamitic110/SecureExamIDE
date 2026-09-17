using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class Add_ExamDependency_Platform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_exam_dependencies_exam_package_id_name_version",
                schema: "public",
                table: "exam_dependencies");

            // Existing dependencies were uploaded before anyone said which platform they run on.
            // "Any" keeps them offered to every client, as they were until now; an empty string
            // would not parse back into the enum and would break every read of those rows.
            migrationBuilder.AddColumn<string>(
                name: "platform",
                schema: "public",
                table: "exam_dependencies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Any");

            migrationBuilder.CreateIndex(
                name: "ix_exam_dependencies_exam_package_id_name_version_platform",
                schema: "public",
                table: "exam_dependencies",
                columns: new[] { "exam_package_id", "name", "version", "platform" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_exam_dependencies_exam_package_id_name_version_platform",
                schema: "public",
                table: "exam_dependencies");

            migrationBuilder.DropColumn(
                name: "platform",
                schema: "public",
                table: "exam_dependencies");

            migrationBuilder.CreateIndex(
                name: "ix_exam_dependencies_exam_package_id_name_version",
                schema: "public",
                table: "exam_dependencies",
                columns: new[] { "exam_package_id", "name", "version" },
                unique: true);
        }
    }
}

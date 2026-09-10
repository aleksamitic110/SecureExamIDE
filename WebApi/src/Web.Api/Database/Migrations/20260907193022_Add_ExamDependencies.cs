using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class Add_ExamDependencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "published_at",
                schema: "public",
                table: "exam_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "exam_dependencies",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_dependencies", x => x.id);
                    table.ForeignKey(
                        name: "fk_exam_dependencies_exam_packages_exam_package_id",
                        column: x => x.exam_package_id,
                        principalSchema: "public",
                        principalTable: "exam_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exam_dependencies_exam_package_id_name_version",
                schema: "public",
                table: "exam_dependencies",
                columns: new[] { "exam_package_id", "name", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_dependencies_object_key",
                schema: "public",
                table: "exam_dependencies",
                column: "object_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exam_dependencies",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "public",
                table: "exam_packages");
        }
    }
}

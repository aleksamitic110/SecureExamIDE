using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class Add_ExamSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exam_sessions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_professor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    one_time_code_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    package_object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    header_object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    package_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    package_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_exam_sessions_exam_packages_exam_package_id",
                        column: x => x.exam_package_id,
                        principalSchema: "public",
                        principalTable: "exam_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_sessions_users_created_by_professor_id",
                        column: x => x.created_by_professor_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exam_sessions_created_by_professor_id",
                schema: "public",
                table: "exam_sessions",
                column: "created_by_professor_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_sessions_exam_package_id_starts_at",
                schema: "public",
                table: "exam_sessions",
                columns: new[] { "exam_package_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_exam_sessions_one_time_code_hash",
                schema: "public",
                table: "exam_sessions",
                column: "one_time_code_hash");

            migrationBuilder.CreateIndex(
                name: "ix_exam_sessions_package_object_key",
                schema: "public",
                table: "exam_sessions",
                column: "package_object_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exam_sessions",
                schema: "public");
        }
    }
}

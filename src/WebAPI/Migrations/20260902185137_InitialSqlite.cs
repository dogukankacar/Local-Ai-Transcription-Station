using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transcription_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CensorLabelsCsv = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Diarization = table.Column<bool>(type: "INTEGER", nullable: false),
                    SrtFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    FullTextCensored = table.Column<string>(type: "TEXT", nullable: true),
                    FullText = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    AudioDurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    ProgressPercent = table.Column<int>(type: "INTEGER", nullable: true),
                    ProgressMessage = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transcription_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transcription_jobs_CreatedAtUtc",
                table: "transcription_jobs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_transcription_jobs_Status",
                table: "transcription_jobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transcription_jobs");
        }
    }
}

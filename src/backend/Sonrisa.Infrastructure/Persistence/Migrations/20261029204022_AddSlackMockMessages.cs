using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sonrisa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlackMockMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlackMockMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    HookName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Body = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlackMockMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlackMockMessages_ReceivedAt",
                table: "SlackMockMessages",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlackMockMessages");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_RefreshTokenHash",
                schema: "identity",
                table: "Users");

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_CustomerId_CreatedAt",
                schema: "identity",
                table: "RefreshTokens",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_CustomerId_RevokedAt",
                schema: "identity",
                table: "RefreshTokens",
                columns: new[] { "CustomerId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAt",
                schema: "identity",
                table: "RefreshTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                schema: "identity",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "identity"."RefreshTokens" (
                    "Id",
                    "CustomerId",
                    "TokenHash",
                    "CreatedAt",
                    "ExpiresAt",
                    "RevokedAt",
                    "ReplacedByTokenHash",
                    "DeviceInfo")
                SELECT
                    "Id",
                    "Id",
                    "RefreshTokenHash",
                    NOW(),
                    "RefreshTokenExpiryTime",
                    NULL,
                    NULL,
                    'Migrated session'
                FROM "identity"."Users"
                WHERE "RefreshTokenHash" IS NOT NULL
                  AND "RefreshTokenExpiryTime" IS NOT NULL
                ON CONFLICT ("TokenHash") DO NOTHING;
                """);

            migrationBuilder.DropColumn(
                name: "RefreshTokenExpiryTime",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefreshTokenHash",
                schema: "identity",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshTokenExpiryTime",
                schema: "identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefreshTokenHash",
                schema: "identity",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "identity"."Users" AS u
                SET
                    "RefreshTokenHash" = t."TokenHash",
                    "RefreshTokenExpiryTime" = t."ExpiresAt"
                FROM (
                    SELECT DISTINCT ON ("CustomerId")
                        "CustomerId",
                        "TokenHash",
                        "ExpiresAt",
                        "CreatedAt"
                    FROM "identity"."RefreshTokens"
                    WHERE "RevokedAt" IS NULL
                    ORDER BY "CustomerId", "CreatedAt" DESC
                ) AS t
                WHERE u."Id" = t."CustomerId";
                """);

            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "identity");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RefreshTokenHash",
                schema: "identity",
                table: "Users",
                column: "RefreshTokenHash",
                unique: true,
                filter: "\"RefreshTokenHash\" IS NOT NULL");
        }
    }
}

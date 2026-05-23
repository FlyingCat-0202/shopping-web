using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Product.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitPriceToStockReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                schema: "product",
                table: "StockReservations",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                UPDATE "product"."StockReservations" AS sr
                SET "UnitPrice" = p."Price"
                FROM "product"."Products" AS p
                WHERE sr."ProductId" = p."Id";
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_UnitPrice",
                schema: "product",
                table: "StockReservations",
                sql: "\"UnitPrice\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_UnitPrice",
                schema: "product",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                schema: "product",
                table: "StockReservations");
        }
    }
}

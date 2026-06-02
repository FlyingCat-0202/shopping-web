using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Product.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCatalogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Products_Catalog_Category_Name",
                schema: "product",
                table: "Products",
                columns: new[] { "IsActive", "CategoryId", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Catalog_Category_Price",
                schema: "product",
                table: "Products",
                columns: new[] { "IsActive", "CategoryId", "Price", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Catalog_Name",
                schema: "product",
                table: "Products",
                columns: new[] { "IsActive", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Catalog_Price",
                schema: "product",
                table: "Products",
                columns: new[] { "IsActive", "Price", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Catalog_Stock_Name",
                schema: "product",
                table: "Products",
                columns: new[] { "IsActive", "StockQuantity", "Name", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Catalog_Category_Name",
                schema: "product",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Catalog_Category_Price",
                schema: "product",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Catalog_Name",
                schema: "product",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Catalog_Price",
                schema: "product",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Catalog_Stock_Name",
                schema: "product",
                table: "Products");
        }
    }
}

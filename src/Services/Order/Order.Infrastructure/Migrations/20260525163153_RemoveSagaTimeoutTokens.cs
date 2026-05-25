using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Order.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSagaTimeoutTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentTimeoutTokenId",
                schema: "order",
                table: "OrderSagaInstances");

            migrationBuilder.DropColumn(
                name: "StockTimeoutTokenId",
                schema: "order",
                table: "OrderSagaInstances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaymentTimeoutTokenId",
                schema: "order",
                table: "OrderSagaInstances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StockTimeoutTokenId",
                schema: "order",
                table: "OrderSagaInstances",
                type: "uuid",
                nullable: true);
        }
    }
}

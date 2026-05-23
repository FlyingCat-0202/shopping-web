using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Order.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderSagaStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderSagaStates",
                schema: "order",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentStep = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StockReservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSagaStates", x => x.OrderId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSagaStates_CurrentStep_UpdatedAt",
                schema: "order",
                table: "OrderSagaStates",
                columns: new[] { "CurrentStep", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSagaStates_IsCompleted_UpdatedAt",
                schema: "order",
                table: "OrderSagaStates",
                columns: new[] { "IsCompleted", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderSagaStates",
                schema: "order");
        }
    }
}

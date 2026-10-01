using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReturnsDiscountsAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "Sales",
                type: "TEXT",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OriginalSaleId",
                table: "Sales",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "Sales",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Discount",
                table: "SaleLines",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "OriginalLineId",
                table: "SaleLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RectifiedInvoiceId",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AuthorizedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_OriginalSaleId",
                table: "Sales",
                column: "OriginalSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_OriginalLineId",
                table: "SaleLines",
                column: "OriginalLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_RectifiedInvoiceId",
                table: "Invoices",
                column: "RectifiedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Action",
                table: "AuditEntries",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_AtUtc",
                table: "AuditEntries",
                column: "AtUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Invoices_RectifiedInvoiceId",
                table: "Invoices",
                column: "RectifiedInvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleLines_SaleLines_OriginalLineId",
                table: "SaleLines",
                column: "OriginalLineId",
                principalTable: "SaleLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Sales_OriginalSaleId",
                table: "Sales",
                column: "OriginalSaleId",
                principalTable: "Sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // AddForeignKey reconstruye Sales, SaleLines e Invoices en SQLite y se pierden sus triggers.
            // EF hace las reconstrucciones al final de la migración, así que se recrean en la siguiente
            // (RestoreBillingTriggers).

            // USR-03: el registro de auditoría no se puede modificar ni borrar.
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_AuditEntries_NoUpdate" BEFORE UPDATE ON "AuditEntries"
                BEGIN SELECT RAISE(ABORT, 'El registro de auditoría no se puede modificar'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_AuditEntries_NoDelete" BEFORE DELETE ON "AuditEntries"
                BEGIN SELECT RAISE(ABORT, 'El registro de auditoría no se puede borrar'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Invoices_RectifiedInvoiceId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleLines_SaleLines_OriginalLineId",
                table: "SaleLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Sales_OriginalSaleId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_Sales_OriginalSaleId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_SaleLines_OriginalLineId",
                table: "SaleLines");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_RectifiedInvoiceId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "OriginalSaleId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Discount",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "OriginalLineId",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "RectifiedInvoiceId",
                table: "Invoices");
        }
    }
}

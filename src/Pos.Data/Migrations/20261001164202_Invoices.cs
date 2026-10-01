using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Data.Migrations
{
    /// <inheritdoc />
    public partial class Invoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nif = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PostalCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Series = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SaleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    IssuerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IssuerNif = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IssuerAddress = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CustomerNif = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CustomerAddress = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    ReplacesInvoiceId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Invoices_ReplacesInvoiceId",
                        column: x => x.ReplacesInvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceVatLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    Base = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceVatLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceVatLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Nif",
                table: "Customers",
                column: "Nif",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Code",
                table: "Invoices",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_IssuedAtUtc",
                table: "Invoices",
                column: "IssuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ReplacesInvoiceId",
                table: "Invoices",
                column: "ReplacesInvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SaleId",
                table: "Invoices",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Series_Number",
                table: "Invoices",
                columns: new[] { "Series", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceVatLines_InvoiceId",
                table: "InvoiceVatLines",
                column: "InvoiceId");

            // Datos de facturación: no se pueden modificar ni borrar (requisito de seguridad).
            foreach (var table in BillingTables)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER "TR_{table}_NoUpdate" BEFORE UPDATE ON "{table}"
                    BEGIN SELECT RAISE(ABORT, 'Los datos de facturación no se pueden modificar'); END;
                    """);
                migrationBuilder.Sql($"""
                    CREATE TRIGGER "TR_{table}_NoDelete" BEFORE DELETE ON "{table}"
                    BEGIN SELECT RAISE(ABORT, 'Los datos de facturación no se pueden borrar'); END;
                    """);
            }
        }

        private static readonly string[] BillingTables = ["Invoices", "InvoiceVatLines"];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in BillingTables)
            {
                migrationBuilder.Sql($"""DROP TRIGGER IF EXISTS "TR_{table}_NoUpdate";""");
                migrationBuilder.Sql($"""DROP TRIGGER IF EXISTS "TR_{table}_NoDelete";""");
            }

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "InvoiceVatLines");

            migrationBuilder.DropTable(
                name: "Invoices");
        }
    }
}

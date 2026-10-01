using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Data.Migrations
{
    /// <inheritdoc />
    public partial class Verifactu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VerifactuRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    IssuerNif = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IssuerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    IssueDate = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    InvoiceType = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    TotalVat = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    PreviousHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PreviousRecordId = table.Column<int>(type: "INTEGER", nullable: true),
                    GeneratedAt = table.Column<string>(type: "TEXT", maxLength: 25, nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AnsweredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerifactuRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerifactuRecords_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerifactuRecords_VerifactuRecords_PreviousRecordId",
                        column: x => x.PreviousRecordId,
                        principalTable: "VerifactuRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerifactuRecords_Hash",
                table: "VerifactuRecords",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerifactuRecords_InvoiceId",
                table: "VerifactuRecords",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_VerifactuRecords_PreviousRecordId",
                table: "VerifactuRecords",
                column: "PreviousRecordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerifactuRecords_Status",
                table: "VerifactuRecords",
                column: "Status");

            // El registro y su huella no se pueden borrar ni cambiar; solo el estado del envío.
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_VerifactuRecords_NoDelete" BEFORE DELETE ON "VerifactuRecords"
                BEGIN SELECT RAISE(ABORT, 'Los registros de facturación no se pueden borrar'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_VerifactuRecords_NoUpdate" BEFORE UPDATE OF
                    "InvoiceId", "Kind", "IssuerNif", "IssuerName", "InvoiceNumber", "IssueDate", "InvoiceType",
                    "TotalVat", "Total", "PreviousHash", "PreviousRecordId", "GeneratedAt", "Hash"
                ON "VerifactuRecords"
                BEGIN SELECT RAISE(ABORT, 'Los registros de facturación no se pueden modificar'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_VerifactuRecords_NoDelete";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_VerifactuRecords_NoUpdate";""");
            migrationBuilder.DropTable(
                name: "VerifactuRecords");
        }
    }
}

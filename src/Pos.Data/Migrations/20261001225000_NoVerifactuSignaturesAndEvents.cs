using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Data.Migrations
{
    /// <inheritdoc />
    public partial class NoVerifactuSignaturesAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CorrectsRecordId",
                table: "VerifactuRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PreviouslyRejected",
                table: "VerifactuRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "VerifactuEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PreviousHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerifactuEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VerifactuSignatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    SignedXml = table.Column<string>(type: "TEXT", nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CertificateSubject = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerifactuSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerifactuSignatures_VerifactuRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "VerifactuRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerifactuSignatures_RecordId",
                table: "VerifactuSignatures",
                column: "RecordId",
                unique: true);

            // VFA-04: firmas y eventos no se modifican ni se borran.
            BillingTriggers.Recreate(migrationBuilder, BillingTriggers.VerifactuProtected);

            // VFA-05: los datos de subsanación forman parte del registro: tampoco se pueden cambiar.
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_VerifactuRecords_NoUpdate";""");
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_VerifactuRecords_NoUpdate" BEFORE UPDATE OF
                    "InvoiceId", "Kind", "IssuerNif", "IssuerName", "InvoiceNumber", "IssueDate", "InvoiceType",
                    "TotalVat", "Total", "PreviousHash", "PreviousRecordId", "GeneratedAt", "Hash",
                    "CorrectsRecordId", "PreviouslyRejected"
                ON "VerifactuRecords"
                BEGIN SELECT RAISE(ABORT, 'Los registros de facturación no se pueden modificar'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in BillingTriggers.VerifactuProtected)
            {
                migrationBuilder.Sql($"""DROP TRIGGER IF EXISTS "TR_{table}_NoUpdate";""");
                migrationBuilder.Sql($"""DROP TRIGGER IF EXISTS "TR_{table}_NoDelete";""");
            }

            migrationBuilder.DropTable(
                name: "VerifactuEvents");

            migrationBuilder.DropTable(
                name: "VerifactuSignatures");

            migrationBuilder.DropColumn(
                name: "CorrectsRecordId",
                table: "VerifactuRecords");

            migrationBuilder.DropColumn(
                name: "PreviouslyRejected",
                table: "VerifactuRecords");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

namespace Pos.Data.Migrations;

/// <summary>
/// Triggers que impiden modificar o borrar los datos de facturación (requisito de seguridad).
///
/// ¡Ojo con las migraciones! En SQLite, añadir una clave ajena o cambiar una columna obliga a EF a
/// reconstruir la tabla (crear otra, copiar, borrar la original), y al borrarla se pierden sus triggers
/// sin ningún aviso. Toda migración que reconstruya una de estas tablas debe llamar a <see cref="Recreate"/>
/// al final. El test BillingTablesAreProtected comprueba que ninguna se queda sin triggers.
/// </summary>
public static class BillingTriggers
{
    /// <summary>Tablas que no se pueden modificar ni borrar en absoluto.</summary>
    public static readonly string[] FullyProtected = ["Sales", "SaleLines", "Payments", "Invoices", "InvoiceVatLines"];

    /// <summary>VFA-04: firmas de los registros No VERI*FACTU y registro de eventos.</summary>
    public static readonly string[] VerifactuProtected = ["VerifactuSignatures", "VerifactuEvents"];

    public static void Recreate(MigrationBuilder migrationBuilder, params string[] tables)
    {
        foreach (var table in tables)
        {
            migrationBuilder.Sql($"""
                CREATE TRIGGER IF NOT EXISTS "TR_{table}_NoUpdate" BEFORE UPDATE ON "{table}"
                BEGIN SELECT RAISE(ABORT, 'Los datos de facturación no se pueden modificar'); END;
                """);
            migrationBuilder.Sql($"""
                CREATE TRIGGER IF NOT EXISTS "TR_{table}_NoDelete" BEFORE DELETE ON "{table}"
                BEGIN SELECT RAISE(ABORT, 'Los datos de facturación no se pueden borrar'); END;
                """);
        }
    }
}

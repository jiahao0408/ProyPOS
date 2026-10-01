using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.DataTransfer;
using Pos.Modules.Products;

namespace Pos.Modules.Tests;

/// <summary>Sección 4: importación (DAT-01) y copias de seguridad (DAT-03).</summary>
public sealed class DataTransferTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly string _folder = Directory.CreateTempSubdirectory("starseapos-dat-").FullName;
    private readonly TestDatabase _memory = new();
    private readonly ImportService _import;

    public DataTransferTests()
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "locales"));
        _import = new ImportService(_memory.Factory, localizer, new RegionFormatter(localizer));
    }

    public void Dispose()
    {
        _memory.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WriteCsv(string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(true));
        return path;
    }

    // --- DAT-01 ---

    [Fact]
    public void Products_PreviewShowsErrorsPerRow()
    {
        var csv = WriteCsv("""
            Nombre;Precio;IVA;Código;Categoría
            Taza;3,50;21;8410000000011;Hogar
            ;2;21;;
            Vaso;abc;21;;
            Plato;4;7;;
            Taza bis;3;21;8410000000011;
            Libro;10,40;4%;;Papelería
            """);

        var preview = _import.Preview(ImportKind.Products, csv);

        Assert.Equal(6, preview.Rows.Count);
        Assert.Equal([null, "ErrorNameRequired", "ErrorAmountFormat", "ErrorVatRate", "ErrorDuplicateInFile", null],
            preview.Rows.Select(r => r.ErrorKey));
        Assert.Equal(3, preview.Rows[1].Line); // la línea 1 son los encabezados
        Assert.Equal((2, 4), (preview.ValidCount, preview.ErrorCount));
    }

    [Fact]
    public void Products_ImportCreatesCategories()
    {
        // Las columnas de más (p. ej. el stock de una plantilla antigua) se ignoran: la tienda no lleva stock.
        var csv = WriteCsv("""
            Nombre;Precio;IVA;Código;Categoría;Stock
            Taza;3,50;21;8410000000011;Hogar;24
            Libro;10.40;4;;Papelería;
            """);

        var imported = _import.Import(_import.Preview(ImportKind.Products, csv));

        Assert.Equal(2, imported);
        var catalog = new CatalogService(_memory.Factory);
        var taza = catalog.FindByBarcode("8410000000011")!;
        Assert.Equal((3.50m, 21m, "Hogar"), (taza.Price, taza.VatRate, catalog.GetCategories().Single(c => c.Id == taza.CategoryId).Name));
        Assert.Equal(["Hogar", "Papelería"], catalog.GetCategories().Select(c => c.Name).Order());
    }

    [Fact]
    public void Products_DuplicateCodesAreNotImported()
    {
        // DAT-01: los códigos duplicados no se importan.
        new CatalogService(_memory.Factory).SaveProduct(null, new ProductInput("Taza", 3m, 21m, "8410000000011", null, null));
        var csv = WriteCsv("Nombre;Precio;IVA;Código\nOtra taza;3;21;8410000000011\n");

        var preview = _import.Preview(ImportKind.Products, csv);

        Assert.Equal("ErrorBarcodeTaken", Assert.Single(preview.Rows).ErrorKey);
        Assert.Equal(0, _import.Import(preview));
    }

    [Fact]
    public void Csv_FromSpanishExcelInWindows1252_IsRead()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var csv = WriteCsv("Nombre;Precio;IVA\nCerámica;2,5;21\n", Encoding.GetEncoding(1252));

        Assert.Equal("Cerámica", Assert.Single(_import.Preview(ImportKind.Products, csv).Rows).Values[0]);
    }

    [Fact]
    public void Csv_CommaDelimitedWithQuotes()
    {
        var rows = TabularFile.ParseCsv("a,\"b, con coma\",\"c \"\"comillas\"\"\"\n1,2,3", ',').ToList();

        Assert.Equal<string[]>(["a", "b, con coma", "c \"comillas\""], rows[0]);
        Assert.Equal<string[]>(["1", "2", "3"], rows[1]);
    }

    [Theory]
    [InlineData(ImportKind.Products, "plantilla.xlsx")]
    [InlineData(ImportKind.Customers, "plantilla.csv")]
    public void Template_CanBeImportedBack(ImportKind kind, string name)
    {
        // DAT-01: plantilla descargable (su fila de ejemplo es válida).
        var path = Path.Combine(_folder, name);
        _import.WriteTemplate(kind, path);

        var preview = _import.Preview(kind, path);

        Assert.True(Assert.Single(preview.Rows).IsValid, preview.Rows[0].ErrorKey);
        Assert.Equal(_import.Headers(kind), TabularFile.Read(path)[0]);
    }

    [Fact]
    public void Customers_ValidatesNifAndDuplicates()
    {
        var csv = WriteCsv("""
            NIF;Nombre;Dirección;CP;Población
            B12345674;Papelería Pérez;Calle Sol 5;08001;Barcelona
            12345678A;Mal;Calle 1;;
            b-12345674;Repetido;Calle 2;;
            """);

        var preview = _import.Preview(ImportKind.Customers, csv);

        Assert.Equal([null, "ErrorCustomerNif", "ErrorDuplicateInFile"], preview.Rows.Select(r => r.ErrorKey));
        Assert.Equal(1, _import.Import(preview));
    }

    // --- DAT-03 ---

    private DatabaseFile OpenRealDatabase(string name)
    {
        var dir = Path.Combine(_folder, name);
        return PosDatabase.OpenFile(dir);
    }

    [Fact]
    public void Backup_RoundTripRestoresTheData()
    {
        var database = OpenRealDatabase("pc");
        var catalog = new CatalogService(database.Factory);
        catalog.SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "111", null, null));
        var backups = new BackupService(database, _clock);
        var file = Path.Combine(_folder, "copia.sspos");

        Assert.True(backups.CreateBackup(file, "contraseña-segura").Success);
        catalog.SaveProduct(null, new ProductInput("Plato", 5m, 21m, "222", null, null)); // cambio posterior

        var restore = backups.Restore(file, "contraseña-segura");

        Assert.True(restore.Success, restore.ErrorKey);
        Assert.Equal(["Taza"], catalog.Search(null).Select(p => p.Name));
        // "Guarda antes una copia del estado actual"
        Assert.Single(Directory.GetFiles(database.BackupsDirectory, "pre-restore-*.db"));
    }

    [Fact]
    public void Backup_CanBeRestoredOnAnotherPc()
    {
        // La copia no depende de la clave del PC: se restaura en otra instalación (otra clave).
        var oldPc = OpenRealDatabase("viejo");
        new CatalogService(oldPc.Factory).SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "111", null, null));
        var file = Path.Combine(_folder, "copia.sspos");
        new BackupService(oldPc, _clock).CreateBackup(file, "contraseña-segura");

        var newPc = OpenRealDatabase("nuevo");
        File.Delete(Path.Combine(newPc.DataDirectory, "pos.key"));
        newPc = OpenRealDatabase("nuevo2");
        var result = new BackupService(newPc, _clock).Restore(file, "contraseña-segura");

        Assert.True(result.Success, result.ErrorKey);
        Assert.Equal("Taza", new CatalogService(newPc.Factory).FindByBarcode("111")!.Name);
    }

    [Fact]
    public void Backup_IsEncrypted()
    {
        var database = OpenRealDatabase("pc");
        new CatalogService(database.Factory).SaveProduct(null, new ProductInput("ProductoSecretoXYZ", 1m, 21m, null, null, null));
        var file = Path.Combine(_folder, "copia.sspos");

        new BackupService(database, _clock).CreateBackup(file, "contraseña-segura");

        var bytes = File.ReadAllBytes(file);
        Assert.Equal("SSPOSBK1", Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.DoesNotContain("ProductoSecretoXYZ", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("SQLite format", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void Restore_WrongPasswordOrBadFile()
    {
        var database = OpenRealDatabase("pc");
        var backups = new BackupService(database, _clock);
        var file = Path.Combine(_folder, "copia.sspos");
        backups.CreateBackup(file, "contraseña-segura");
        var bad = Path.Combine(_folder, "otra-cosa.txt");
        File.WriteAllText(bad, "hola");

        Assert.Equal("ErrorBackupPassword", backups.Restore(file, "otra-contraseña").ErrorKey);
        Assert.Equal("ErrorBackupInvalid", backups.Restore(bad, "contraseña-segura").ErrorKey);
        Assert.Equal("ErrorBackupPasswordShort", backups.CreateBackup(file, "corta").ErrorKey);
    }

    [Fact]
    public void DailyBackup_OncePerDayKeepingSeven()
    {
        var database = OpenRealDatabase("pc");
        var backups = new BackupService(database, _clock);

        Assert.NotNull(backups.RunDailyBackupIfDue());
        Assert.Null(backups.RunDailyBackupIfDue()); // ya hay copia de hoy
        for (var i = 0; i < 9; i++)
        {
            _clock.Advance(TimeSpan.FromDays(1));
            backups.RunDailyBackupIfDue();
        }

        Assert.Equal(BackupService.AutoBackupsKept, Directory.GetFiles(database.BackupsDirectory, "auto-*.db").Length);
    }
}

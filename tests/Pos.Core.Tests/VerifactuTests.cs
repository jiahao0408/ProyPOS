using Pos.Core.Verifactu;

namespace Pos.Core.Tests;

public class VerifactuTests
{
    /// <summary>
    /// Ejemplo de la documentación técnica de la AEAT ("Detalle de las especificaciones técnicas para
    /// la generación de la huella"): primer registro de la cadena, sin huella anterior.
    /// </summary>
    [Fact]
    public void Hash_MatchesAeatExample_FirstRecord()
    {
        var hash = VerifactuHash.ForAlta("89890001K", "12345678/G33", "01-01-2024", "F1", 12.35m, 123.45m,
            previousHash: null, generatedAt: "2024-01-01T19:20:30+01:00");

        Assert.Equal("3C464DAF61ACB827C65FDA19F352A4E3BDC2C640E9E9FC4CC058073F38F12F60", hash);
    }

    [Fact]
    public void Hash_ChangesWithAnyFieldAndWithThePreviousHash()
    {
        string Hash(string number = "T2026-000001", decimal total = 10m, string? previous = null) =>
            VerifactuHash.ForAlta("B12345674", number, "01-10-2026", "F2", 1.74m, total, previous, "2026-10-01T11:00:00+02:00");

        var original = Hash();
        Assert.NotEqual(original, Hash(number: "T2026-000002"));
        Assert.NotEqual(original, Hash(total: 10.01m));
        Assert.NotEqual(original, Hash(previous: "ABC"));
        Assert.Matches("^[0-9A-F]{64}$", original);
    }

    [Fact]
    public void Formats()
    {
        Assert.Equal("1234.50", VerifactuHash.Amount(1234.5m));
        Assert.Equal("01-10-2026", VerifactuHash.Date(new DateTime(2026, 10, 1)));
        Assert.Equal("2026-10-01T11:05:09+02:00", VerifactuHash.Timestamp(new DateTimeOffset(2026, 10, 1, 11, 5, 9, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void Qr_BuildsAeatUrl()
    {
        var url = VerifactuQr.Build("B12345674", "T2026-000001", new DateTime(2026, 10, 1), 17.4m, testEnvironment: true);

        Assert.Equal("https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR?nif=B12345674&numserie=T2026-000001&fecha=01-10-2026&importe=17.40", url);
        Assert.StartsWith(VerifactuQr.ProductionUrl, VerifactuQr.Build("B12345674", "T1", DateTime.Today, 1m, false));
    }

    [Fact]
    public void Qr_InvoiceNumberCanBeReadBack()
    {
        var url = VerifactuQr.Build("B12345674", "A/2026&1", new DateTime(2026, 10, 1), 1m, false);

        Assert.Equal("A/2026&1", VerifactuQr.InvoiceNumberFrom(url));
        Assert.Null(VerifactuQr.InvoiceNumberFrom("T2026-000001"));
    }
}

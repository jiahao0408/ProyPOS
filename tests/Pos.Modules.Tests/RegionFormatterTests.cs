using Pos.Localization;

namespace Pos.Modules.Tests;

public class RegionFormatterTests
{
    private static readonly string LocalesDirectory = Path.Combine(AppContext.BaseDirectory, "locales");

    private static RegionFormatter Formatter(string language)
    {
        var localizer = new JsonLocalizer(LocalesDirectory, "es");
        localizer.SetLanguage(language);
        return new RegionFormatter(localizer);
    }

    [Theory]
    [InlineData("es", "1.234,50 €")]
    [InlineData("de", "1.234,50 €")]
    [InlineData("en", "€1,234.50")]
    [InlineData("zh", "€1,234.50")]
    public void FormatMoney_UsesEuroAndLanguageSeparators(string language, string expected)
    {
        // CFG-04: euro por defecto; separadores decimales según el idioma.
        // Se normalizan los espacios porque .NET usa un espacio duro antes del símbolo.
        var text = Formatter(language).FormatMoney(1234.5m).Replace(' ', ' ').Replace(' ', ' ');

        Assert.Equal(expected, text);
    }

    [Fact]
    public void FormatMoney_UsesConfiguredCurrency()
    {
        var formatter = Formatter("en");
        formatter.CurrencySymbol = "£";

        Assert.Equal("£2.00", formatter.FormatMoney(2m));
    }

    [Theory]
    [InlineData("es", "12,50", 12.50)]
    [InlineData("es", "12.50", 12.50)]
    [InlineData("es", "1.234,50", 1234.50)]
    [InlineData("es", "1.234", 1234)]
    [InlineData("en", "1,234.50", 1234.50)]
    [InlineData("es", "-2,5", -2.5)]
    [InlineData("en", "12.50", 12.50)]
    [InlineData("en", "12,50", 12.50)]
    [InlineData("es", "7", 7)]
    [InlineData("es", "3,00 €", 3)]
    public void TryParseAmount_AcceptsLanguageAndCommonSeparators(string language, string text, decimal expected)
    {
        Assert.True(Formatter(language).TryParseAmount(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1,2.3,4")]
    public void TryParseAmount_RejectsGarbage(string text)
    {
        Assert.False(Formatter("es").TryParseAmount(text, out _));
    }

    [Fact]
    public void FormatDate_UsesConfiguredFormat()
    {
        var formatter = Formatter("en");
        formatter.DateFormat = "yyyy-MM-dd";

        Assert.Equal("2026-10-01", formatter.FormatDate(new DateTime(2026, 10, 1)));
    }
}

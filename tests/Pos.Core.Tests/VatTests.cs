using System.Globalization;
using Pos.Core.Pricing;

namespace Pos.Core.Tests;

public class VatTests
{
    [Theory]
    [InlineData("12.10", "21", "10.00", "2.10")]
    [InlineData("1.00", "10", "0.91", "0.09")]
    [InlineData("1.04", "4", "1.00", "0.04")]
    [InlineData("5.00", "0", "5.00", "0.00")]
    public void FromGross_SplitsBaseAndVat(string total, string rate, string expectedBase, string expectedVat)
    {
        var result = Vat.FromGross(decimal.Parse(total, CultureInfo.InvariantCulture), decimal.Parse(rate, CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expectedBase, CultureInfo.InvariantCulture), result.Base);
        Assert.Equal(decimal.Parse(expectedVat, CultureInfo.InvariantCulture), result.VatAmount);
    }

    [Theory]
    [InlineData("0.01", "21")]
    [InlineData("3.33", "10")]
    [InlineData("999.99", "21")]
    public void FromGross_BasePlusVatEqualsTotal(string total, string rate)
    {
        var result = Vat.FromGross(decimal.Parse(total, CultureInfo.InvariantCulture), decimal.Parse(rate, CultureInfo.InvariantCulture));

        Assert.Equal(result.Total, result.Base + result.VatAmount);
    }

    [Fact]
    public void FromGross_RejectsNegativeRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Vat.FromGross(10m, -1m));
    }
}

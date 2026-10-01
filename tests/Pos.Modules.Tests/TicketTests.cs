using Pos.Core.Domain;
using Pos.Modules.Sales;

namespace Pos.Modules.Tests;

public class TicketTests
{
    private static readonly TicketItem Taza = new(1, "Taza", null, 3.50m, 21m);
    private static readonly TicketItem Libro = new(2, "Libro", null, 10.40m, 4m);

    [Fact]
    public void Add_SameProductAccumulatesInOneLine()
    {
        var ticket = new Ticket();

        ticket.Add(Taza);
        ticket.Add(Taza, 2);

        var line = Assert.Single(ticket.Lines);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(10.50m, ticket.Total);
    }

    [Fact]
    public void Add_GenericItemsAlwaysGoInSeparateLines()
    {
        var section = new Category { Id = 5, Name = "Hogar" };
        var ticket = new Ticket();

        ticket.Add(TicketItem.Generic(section, 2m));
        ticket.Add(TicketItem.Generic(section, 2m));

        Assert.Equal(2, ticket.Lines.Count);
        Assert.All(ticket.Lines, l => Assert.Null(l.Item.ProductId));
        Assert.All(ticket.Lines, l => Assert.Equal(5, l.Item.CategoryId));
    }

    [Fact]
    public void SetQuantity_ZeroRemovesTheLine()
    {
        // VEN-02: quitar una línea no deja rastro; el total se actualiza.
        var ticket = new Ticket();
        var line = ticket.Add(Taza, 2);
        ticket.Add(Libro);

        ticket.SetQuantity(line, 0);

        Assert.Equal("Libro", Assert.Single(ticket.Lines).Item.Description);
        Assert.Equal(10.40m, ticket.Total);
    }

    [Fact]
    public void VatBreakdown_GroupsByRate()
    {
        var ticket = new Ticket();
        ticket.Add(Taza, 2);  // 7,00 al 21 %
        ticket.Add(Libro);    // 10,40 al 4 %

        var breakdown = ticket.VatBreakdown();

        Assert.Equal(2, breakdown.Count);
        Assert.Equal(new TicketVatLine(21m, 5.79m, 1.21m, 7.00m), breakdown[0]);
        Assert.Equal(new TicketVatLine(4m, 10.00m, 0.40m, 10.40m), breakdown[1]);
        Assert.Equal(ticket.Total, breakdown.Sum(b => b.Total));
    }
}

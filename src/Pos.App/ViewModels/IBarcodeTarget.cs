namespace Pos.App.ViewModels;

/// <summary>v1.1: página que acepta códigos de un lector por puerto COM (la venta).</summary>
public interface IBarcodeTarget
{
    void OnBarcode(string code);
}

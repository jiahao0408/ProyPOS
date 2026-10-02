using System.Globalization;
using System.IO.Ports;
using System.Text;
using Pos.Data;

namespace Pos.Modules.Hardware;

/// <summary>
/// v1.1 (HW-02): visor de cliente por puerto serie, los VFD de 2 líneas × 20 caracteres que se ponen
/// encima del mostrador. Habla ESC/POS de visor (Epson DM-D y compatibles) o CD5220. Va en paralelo a
/// la pantalla de cliente en un segundo monitor: se puede usar uno, otro o los dos.
/// </summary>
public sealed class PoleDisplay(ProfileStore profiles)
{
    public const int Columns = 20;

    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    /// <summary>Último error de comunicación (para mostrarlo en la prueba del visor).</summary>
    public string? LastError { get; private set; }

    /// <summary>Muestra dos líneas. Sin visor configurado no hace nada; un fallo nunca para la venta.</summary>
    public Task ShowAsync(string line1, string line2)
    {
        var hardware = profiles.GetHardware();
        if (hardware.PolePort.Length == 0)
            return Task.CompletedTask;
        var bytes = Encode(hardware.PoleProtocol, line1, line2);
        return Task.Run(async () =>
        {
            await OneAtATime.WaitAsync();
            try
            {
                using var port = new SerialPort(hardware.PolePort, hardware.PoleBaudRate) { WriteTimeout = 2000 };
                port.Open();
                port.Write(bytes, 0, bytes.Length);
                LastError = null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException or ArgumentException)
            {
                LastError = e.Message;
            }
            finally
            {
                OneAtATime.Release();
            }
        });
    }

    /// <summary>Bytes para el visor: borrar y escribir las dos líneas, recortadas a 20 caracteres.</summary>
    public static byte[] Encode(PoleDisplayProtocol protocol, string line1, string line2)
    {
        var a = Fit(line1);
        var b = Fit(line2);
        var o = new List<byte>(64);
        if (protocol == PoleDisplayProtocol.Cd5220)
        {
            o.AddRange([0x1B, 0x40, 0x0C]);                    // iniciar y borrar
            o.AddRange([0x1B, (byte)'Q', (byte)'A']);           // línea de arriba
            o.AddRange(Encoding.ASCII.GetBytes(a));
            o.Add(0x0D);
            o.AddRange([0x1B, (byte)'Q', (byte)'B']);           // línea de abajo
            o.AddRange(Encoding.ASCII.GetBytes(b));
            o.Add(0x0D);
        }
        else
        {
            o.AddRange([0x1B, 0x40, 0x0C]);                    // iniciar y borrar
            o.AddRange([0x1F, 0x24, 1, 1]);                     // US $ x y: columna 1, fila 1
            o.AddRange(Encoding.ASCII.GetBytes(a));
            o.AddRange([0x1F, 0x24, 1, 2]);                     // columna 1, fila 2
            o.AddRange(Encoding.ASCII.GetBytes(b));
        }
        return o.ToArray();
    }

    /// <summary>Dos textos en una línea: el primero a la izquierda y el segundo a la derecha.</summary>
    public static string Columns2(string left, string right)
    {
        left = Ascii(left);
        right = Ascii(right);
        var room = Math.Max(0, Columns - right.Length - 1);
        if (left.Length > room)
            left = left[..room];
        return left + new string(' ', Math.Max(1, Columns - left.Length - right.Length)) + right;
    }

    public static string Center(string text)
    {
        text = Ascii(text);
        if (text.Length >= Columns)
            return text[..Columns];
        return text.PadLeft((Columns + text.Length) / 2).PadRight(Columns);
    }

    /// <summary>Los visores solo tienen ASCII fiable: € → EUR y sin acentos.</summary>
    public static string Ascii(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Replace("€", "EUR").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(c is >= ' ' and <= '~' ? c : c is ' ' or ' ' ? ' ' : '?');
        }
        return sb.ToString().Trim();
    }

    private static string Fit(string line)
    {
        var text = line.Length == Columns ? line : Ascii(line);
        return text.Length > Columns ? text[..Columns] : text.PadRight(Columns);
    }
}

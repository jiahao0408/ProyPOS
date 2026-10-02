using System.Drawing.Printing;
using System.IO.Ports;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Pos.Core.Hardware;
using Pos.Data;

namespace Pos.Modules.Hardware;

/// <summary>
/// HW-01: envía los bytes ESC/POS a la impresora según los ajustes: impresora de Windows (USB),
/// puerto COM (también Bluetooth, que Windows expone como COM virtual), red o fichero.
/// </summary>
public sealed class RawPrinter(ProfileStore profiles) : IRawPrinter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public Task PrintAsync(byte[] data, PrinterDestination destination, CancellationToken cancellationToken = default)
    {
        var profile = profiles.GetPrinter(destination);
        return profile.Connection switch
        {
            PrinterConnection.Windows => Task.Run(() => WindowsSpooler.SendRaw(Required(profile.Target), data), cancellationToken),
            PrinterConnection.Serial => Task.Run(() => SendSerial(Required(profile.Target), profile.BaudRate, profile.Handshake, data), cancellationToken),
            PrinterConnection.Network => SendNetworkAsync(Required(profile.Target), data, cancellationToken),
            _ => SendFileAsync(profile.Target, data, cancellationToken),
        };
    }

    /// <summary>Impresoras instaladas en Windows, para elegirla en los ajustes.</summary>
    public static IReadOnlyList<string> InstalledPrinters() => PrinterSettings.InstalledPrinters.Cast<string>().ToList();

    public static IReadOnlyList<string> SerialPorts() => SerialPort.GetPortNames().Order().ToList();

    /// <summary>Carpeta por defecto de la "impresora" de fichero.</summary>
    public static string DefaultOutputFolder => Path.Combine(PosDatabase.DefaultDataDirectory, "tickets");

    private static string Required(string target) =>
        string.IsNullOrWhiteSpace(target) ? throw new InvalidOperationException("No printer selected") : target.Trim();

    private static void SendSerial(string port, int baudRate, SerialHandshake handshake, byte[] data)
    {
        using var serial = new SerialPort(port, baudRate)
        {
            WriteTimeout = (int)Timeout.TotalMilliseconds,
            // v1.1: control de flujo; con DTR/DSR (muy usado en impresoras serie) se activa DTR y se espera a DSR.
            Handshake = handshake switch
            {
                SerialHandshake.XOnXOff => Handshake.XOnXOff,
                SerialHandshake.RtsCts => Handshake.RequestToSend,
                _ => Handshake.None,
            },
            DtrEnable = true,
            RtsEnable = handshake != SerialHandshake.RtsCts,
        };
        serial.Open();
        if (handshake == SerialHandshake.DtrDsr)
            WaitForDsr(serial);
        // Por bloques: las impresoras serie tienen poco búfer y un ticket con logo puede pasar de 20 KB.
        for (var offset = 0; offset < data.Length; offset += 1024)
        {
            if (handshake == SerialHandshake.DtrDsr)
                WaitForDsr(serial);
            serial.Write(data, offset, Math.Min(1024, data.Length - offset));
        }
        // Que salga todo antes de cerrar el puerto (al cerrarlo se descarta lo que quede en el búfer).
        var deadline = DateTime.UtcNow + Timeout;
        while (serial.BytesToWrite > 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(20);
    }

    private static void WaitForDsr(SerialPort serial)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!serial.DsrHolding)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"{serial.PortName}: la impresora no está lista (DSR)");
            Thread.Sleep(20);
        }
    }

    /// <summary>"192.168.1.50" o "192.168.1.50:9100".</summary>
    private static async Task SendNetworkAsync(string target, byte[] data, CancellationToken cancellationToken)
    {
        var parts = target.Split(':');
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 9100;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var client = new TcpClient();
        await client.ConnectAsync(parts[0], port, timeout.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(data, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }

    /// <summary>Sin impresora: guarda los bytes en un fichero .bin (se puede enviar luego con "copy /b").</summary>
    private static async Task SendFileAsync(string folder, byte[] data, CancellationToken cancellationToken)
    {
        folder = string.IsNullOrWhiteSpace(folder) ? DefaultOutputFolder : folder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"ticket-{DateTime.Now:yyyyMMdd-HHmmss-fff}.bin");
        await File.WriteAllBytesAsync(path, data, cancellationToken);
    }
}

/// <summary>Envío RAW a través de la cola de impresión de Windows (winspool).</summary>
internal static class WindowsSpooler
{
    public static void SendRaw(string printerName, byte[] data)
    {
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
            throw new InvalidOperationException($"Printer '{printerName}' not found ({Marshal.GetLastWin32Error()})");
        try
        {
            var doc = new DocInfo1 { DocName = "StarSeaPOS ticket", DataType = "RAW" };
            if (StartDocPrinter(handle, 1, ref doc) == 0)
                throw new InvalidOperationException($"StartDocPrinter failed ({Marshal.GetLastWin32Error()})");
            try
            {
                StartPagePrinter(handle);
                if (!WritePrinter(handle, data, data.Length, out var written) || written != data.Length)
                    throw new InvalidOperationException($"WritePrinter failed ({Marshal.GetLastWin32Error()})");
                EndPagePrinter(handle);
            }
            finally
            {
                EndDocPrinter(handle);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr handle, int level, ref DocInfo1 docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, byte[] data, int count, out int written);
}

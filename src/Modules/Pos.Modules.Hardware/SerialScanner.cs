using System.IO.Ports;
using System.Text;
using Pos.Data;

namespace Pos.Modules.Hardware;

/// <summary>
/// Junta los trozos que llegan por el puerto serie y devuelve cada código completo. Los lectores
/// terminan el código con CR, LF o los dos (según cómo estén programados).
/// </summary>
public sealed class ScanBuffer
{
    private readonly StringBuilder _pending = new();

    public IReadOnlyList<string> Append(string chunk)
    {
        var codes = new List<string>();
        foreach (var c in chunk)
        {
            if (c is '\r' or '\n' or '\t')
            {
                var code = _pending.ToString().Trim();
                _pending.Clear();
                if (code.Length > 0)
                    codes.Add(code);
            }
            else if (!char.IsControl(c))
                _pending.Append(c);
        }
        return codes;
    }
}

/// <summary>
/// v1.1 (HW-04): lector de códigos por puerto COM (lectores serie, o USB configurados como "USB COM /
/// puerto serie virtual"). El lector USB en modo teclado sigue funcionando sin configurar nada.
/// </summary>
public sealed class SerialScanner(ProfileStore profiles) : IDisposable
{
    private readonly object _lock = new();
    private SerialPort? _port;
    private ScanBuffer _buffer = new();

    /// <summary>Un código leído. Llega desde el hilo del puerto serie.</summary>
    public event Action<string>? Scanned;

    public bool IsRunning => _port?.IsOpen == true;

    public string? LastError { get; private set; }

    /// <summary>Abre el puerto configurado (o lo cierra si no hay ninguno). Se llama al arrancar y al guardar los ajustes.</summary>
    public void Restart()
    {
        lock (_lock)
        {
            Close();
            var hardware = profiles.GetHardware();
            if (hardware.ScannerPort.Length == 0)
                return;
            try
            {
                var port = new SerialPort(hardware.ScannerPort, hardware.ScannerBaudRate) { Encoding = Encoding.ASCII, DtrEnable = true, RtsEnable = true };
                port.DataReceived += OnData;
                port.Open();
                _port = port;
                _buffer = new ScanBuffer();
                LastError = null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                LastError = e.Message; // puerto ocupado o inexistente: se avisa en los ajustes; el lector de teclado sigue
            }
        }
    }

    /// <summary>Para las pruebas y para lectores que entregan el código por otra vía.</summary>
    public void Receive(string chunk)
    {
        foreach (var code in _buffer.Append(chunk))
            Scanned?.Invoke(code);
    }

    public void Dispose()
    {
        lock (_lock)
            Close();
    }

    private void OnData(object sender, SerialDataReceivedEventArgs e)
    {
        if (sender is SerialPort port)
        {
            try
            {
                Receive(port.ReadExisting());
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
            {
                LastError = ex.Message;
            }
        }
    }

    private void Close()
    {
        if (_port is null)
            return;
        _port.DataReceived -= OnData;
        try
        {
            _port.Close();
        }
        catch (IOException)
        {
        }
        _port.Dispose();
        _port = null;
    }
}

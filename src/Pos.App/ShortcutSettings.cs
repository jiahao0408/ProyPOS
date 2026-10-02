using Avalonia.Input;
using Pos.Core;
using Pos.Data;

namespace Pos.App;

/// <summary>Acciones de la pantalla de venta que tienen atajo de teclado (v1.1.5).</summary>
public enum ShortcutAction
{
    /// <summary>Cobrar (efectivo; Enter otra vez = importe justo).</summary>
    Pay,

    /// <summary>Cobrar con tarjeta.</summary>
    PayCard,

    /// <summary>Con la caja cerrada, abrirla; con la caja abierta, abrir el cajón.</summary>
    OpenCashOrDrawer,

    RemoveLastLine,
    ClearTicket,
    TicketDiscount,
    LineDiscount,
    PriceCheck,

    /// <summary>Cambiar entre el modo táctil y el modo teclado.</summary>
    ToggleMode,

    Help,
}

/// <summary>Un atajo: una tecla (con o sin Ctrl/Alt/Mayús) o un carácter como "+".</summary>
public sealed record Shortcut(KeyGesture? Key, string? Character)
{
    /// <summary>
    /// Las teclas que también sirven para escribir (Enter, Supr, retroceso, espacio) y los caracteres
    /// solo actúan con el buscador vacío; así no estorban al lector de códigos ni al escribir.
    /// </summary>
    public bool OnlyWhenSearchIsEmpty =>
        Character is not null
        || Key is { KeyModifiers: KeyModifiers.None or KeyModifiers.Shift, Key: Avalonia.Input.Key.Enter or Avalonia.Input.Key.Delete
            or Avalonia.Input.Key.Back or Avalonia.Input.Key.Space or Avalonia.Input.Key.Add or Avalonia.Input.Key.Subtract
            or Avalonia.Input.Key.Multiply or Avalonia.Input.Key.Divide or Avalonia.Input.Key.Decimal };

    public override string ToString() => Character ?? Key?.ToString() ?? "";
}

/// <summary>
/// v1.1.5: atajos de teclado de la pantalla de venta, configurables en Ajustes. Cada acción admite varias
/// teclas separadas por comas ("Enter, F12"). Se guardan en la tabla de ajustes y valen para toda la tienda.
/// </summary>
public sealed class ShortcutSettings(SettingsStore settings)
{
    public static IReadOnlyDictionary<ShortcutAction, string> Defaults { get; } = new Dictionary<ShortcutAction, string>
    {
        [ShortcutAction.Pay] = "Enter, F12",
        [ShortcutAction.PayCard] = "+",
        [ShortcutAction.OpenCashOrDrawer] = "Insert",
        [ShortcutAction.RemoveLastLine] = "Delete",
        [ShortcutAction.ClearTicket] = "F8",
        [ShortcutAction.TicketDiscount] = "F2",
        [ShortcutAction.LineDiscount] = "F3",
        [ShortcutAction.PriceCheck] = "F9",
        [ShortcutAction.ToggleMode] = "F10",
        [ShortcutAction.Help] = "F1",
    };

    /// <summary>Caracteres que el buscador usa para su propia sintaxis (n*, -n*, 2,50): no pueden ser atajos.</summary>
    private const string ReservedCharacters = "*-.,";

    private Dictionary<ShortcutAction, IReadOnlyList<Shortcut>>? _cache;

    /// <summary>Lo guardado para una acción sin atajo ("" en pantalla).</summary>
    private const string NoShortcut = "none";

    public string GetText(ShortcutAction action) => settings.Get(SettingName(action)) switch
    {
        NoShortcut => "",
        { Length: > 0 } text => text,
        _ => Defaults[action],
    };

    public IReadOnlyList<Shortcut> Get(ShortcutAction action)
    {
        _cache ??= Enum.GetValues<ShortcutAction>().ToDictionary(a => a, a => TryParse(GetText(a), out var list) ? list : Parse(Defaults[a]));
        return _cache[action];
    }

    /// <summary>Guarda todos los atajos. Comprueba que se entienden y que ninguna tecla se repite.</summary>
    public OperationResult Save(IReadOnlyDictionary<ShortcutAction, string> texts)
    {
        var seen = new Dictionary<string, ShortcutAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var (action, text) in texts)
        {
            if (!TryParse(text, out var list))
                return OperationResult.Fail("ErrorShortcutInvalid");
            foreach (var shortcut in list)
            {
                if (!seen.TryAdd(shortcut.ToString(), action))
                    return OperationResult.Fail("ErrorShortcutDuplicate");
            }
        }
        foreach (var (action, text) in texts)
            settings.Set(SettingName(action), Normalize(text) is { Length: > 0 } normalized ? normalized : NoShortcut);
        _cache = null;
        return OperationResult.Ok();
    }

    public void ResetToDefaults()
    {
        foreach (var action in Enum.GetValues<ShortcutAction>())
            settings.Set(SettingName(action), "");
        _cache = null;
    }

    /// <summary>La acción de una tecla pulsada, o null.</summary>
    public ShortcutAction? Match(KeyEventArgs e, bool searchIsEmpty)
    {
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            foreach (var s in Get(action))
            {
                if (s.Key is { } key && key.Matches(e) && (searchIsEmpty || !s.OnlyWhenSearchIsEmpty))
                    return action;
            }
        }
        return null;
    }

    /// <summary>La acción de un carácter escrito (como "+"), o null. Solo con el buscador vacío.</summary>
    public ShortcutAction? MatchCharacter(string? text, bool searchIsEmpty)
    {
        if (!searchIsEmpty || string.IsNullOrEmpty(text))
            return null;
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            if (Get(action).Any(s => s.Character == text))
                return action;
        }
        return null;
    }

    /// <summary>
    /// Entiende "Enter, F12", "Ctrl+P", "Insert" o un carácter suelto como "+". No admite letras ni números
    /// sin Ctrl/Alt (se escriben en el buscador) ni los caracteres de la sintaxis del buscador.
    /// </summary>
    public static bool TryParse(string text, out IReadOnlyList<Shortcut> shortcuts)
    {
        var list = new List<Shortcut>();
        shortcuts = list;
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return true; // sin atajo
        foreach (var part in parts)
        {
            if (part.Length == 1)
            {
                var c = part[0];
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || ReservedCharacters.Contains(c))
                    return false;
                list.Add(new Shortcut(null, part));
                continue;
            }
            KeyGesture gesture;
            try
            {
                gesture = KeyGesture.Parse(part);
            }
            catch (Exception e) when (e is ArgumentException or FormatException or KeyNotFoundException)
            {
                return false;
            }
            var plainKey = gesture.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift;
            if (gesture.Key == Key.None || (plainKey && gesture.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9))
                return false;
            list.Add(new Shortcut(gesture, null));
        }
        return true;
    }

    private static IReadOnlyList<Shortcut> Parse(string text) => TryParse(text, out var list) ? list : [];

    private static string Normalize(string text) =>
        string.Join(", ", text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    private static string SettingName(ShortcutAction action) => $"shortcut.{action}";
}

using System.Text.Json;
using System.Text.RegularExpressions;
using Pos.Localization;

namespace Pos.Core.Tests;

public class LocalizationTests
{
    private static readonly string LocalesDirectory = Path.Combine(AppContext.BaseDirectory, "locales");

    public static TheoryData<string> LanguageFiles()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(LocalesDirectory, "*.json"))
            data.Add(Path.GetFileName(path));
        return data;
    }

    private static Dictionary<string, string> Read(string fileName) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(LocalesDirectory, fileName)))!;

    [Theory]
    [MemberData(nameof(LanguageFiles))]
    public void EveryLanguage_HasTheSameKeysAsSpanish(string fileName)
    {
        // CFG-01: ningún texto queda sin traducir.
        var reference = Read("es.json").Keys.ToHashSet();
        var keys = Read(fileName).Keys.ToHashSet();

        Assert.Empty(reference.Except(keys));
        Assert.Empty(keys.Except(reference));
    }

    [Theory]
    [MemberData(nameof(LanguageFiles))]
    public void EveryLanguage_HasNoEmptyTexts(string fileName)
    {
        Assert.DoesNotContain(Read(fileName), entry => string.IsNullOrWhiteSpace(entry.Value));
    }

    /// <summary>
    /// CFG-01: ningún texto queda sin traducir. Busca en el código fuente las claves que se usan
    /// (L[Clave] en XAML, L["Clave"] y claves de error en C#) y comprueba que existen.
    /// </summary>
    [Fact]
    public void EveryKeyUsedInSourceCode_ExistsInSpanish()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StarSeaPOS.sln")))
            root = root.Parent;
        Assert.NotNull(root);

        Regex[] xamlPatterns = [new(@"\{Binding [^}]*L\[([A-Za-z0-9]+)\]")];
        Regex[] csharpPatterns =
        [
            new(@"L\[""([A-Za-z0-9]+)""\]"),
            new(@"""((?:Error|Confirm|Nav|Role|Module|Col)[A-Z][A-Za-z]+)"""),
            new(@"Show(?:Error|Info)\(""([A-Za-z]+)""\)"),
        ];
        var used = Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => (f.EndsWith(".axaml") ? xamlPatterns : f.EndsWith(".cs") ? csharpPatterns : [])
                .SelectMany(p => p.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)))
            .ToHashSet();

        var missing = used.Except(Read("es.json").Keys).Order().ToList();

        Assert.True(used.Count > 50, "No se encontraron claves: ¿ha cambiado la forma de usarlas?");
        Assert.Empty(missing);
    }

    [Fact]
    public void SetLanguage_ChangesTextsAndNotifiesIndexer()
    {
        var localizer = new JsonLocalizer(LocalesDirectory, "es");
        var changed = new List<string?>();
        localizer.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        localizer.SetLanguage("en");

        Assert.Equal("Exit", localizer["Exit"]);
        Assert.Contains("Item[]", changed);
    }

    [Fact]
    public void ReadsUtf8Chinese()
    {
        var localizer = new JsonLocalizer(LocalesDirectory, "es");

        localizer.SetLanguage("zh");

        Assert.Equal("退出", localizer["Exit"]);
    }

    [Fact]
    public void MissingKey_FallsBackToDefaultLanguageThenToKey()
    {
        var dir = Directory.CreateTempSubdirectory("pos-locales-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "es.json"), """{ "LanguageName": "Español", "Hello": "Hola" }""");
            File.WriteAllText(Path.Combine(dir, "en.json"), """{ "LanguageName": "English" }""");
            var localizer = new JsonLocalizer(dir, "es");

            localizer.SetLanguage("en");

            Assert.Equal("Hola", localizer["Hello"]);
            Assert.Equal("Unknown", localizer["Unknown"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AvailableLanguages_ComeFromFilesWithTheirNames()
    {
        var localizer = new JsonLocalizer(LocalesDirectory, "es");

        Assert.Contains(localizer.AvailableLanguages, l => l is { Code: "es", Name: "Español" });
        Assert.Contains(localizer.AvailableLanguages, l => l is { Code: "zh", Name: "中文" });
    }
}

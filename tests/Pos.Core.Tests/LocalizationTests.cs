using System.ComponentModel;
using System.Text.Json;
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

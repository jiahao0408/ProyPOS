using Pos.Modules.Verifactu;

namespace Pos.Modules.Tests;

/// <summary>VFA-06: producer.json es local de cada PC; la plantilla deja la declaración responsable incompleta.</summary>
public sealed class ProducerInfoTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("starseapos-producer-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ProducerInfo LoadWith(string json)
    {
        File.WriteAllText(Path.Combine(_folder, ProducerInfo.FileName), json);
        return ProducerInfo.Load(_folder);
    }

    [Fact]
    public void Template_IsIncomplete()
    {
        var template = File.ReadAllText(Path.Combine(FindRoot(), "src", "Pos.App", "producer.example.json"));

        Assert.False(LoadWith(template).IsComplete);
        Assert.False(ProducerInfo.Load(Path.Combine(_folder, "no-existe")).IsComplete);
    }

    [Fact]
    public void FilledIn_IsComplete()
    {
        var producer = LoadWith("""
            { "name": "Productor S.L.", "nif": "B12345674", "address": "Calle 1, 28001 Madrid",
              "declarationDate": "01-10-2026", "declarationPlace": "Madrid" }
            """);

        Assert.True(producer.IsComplete);
        Assert.Equal(("StarSeaPOS", "SS"), (producer.SystemName, producer.SystemId));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarSeaPOS.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}

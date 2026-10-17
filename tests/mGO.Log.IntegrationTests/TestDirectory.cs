namespace mGO.Log.IntegrationTests;

internal sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "golog-tests-" + Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public string[] Logs() => Directory.GetFiles(Path, "*.log");
    public string[] Lines() => Logs().SelectMany(ReadShared).ToArray();
    private static IEnumerable<string> ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }
    public void Dispose() => Directory.Delete(Path, true);
}

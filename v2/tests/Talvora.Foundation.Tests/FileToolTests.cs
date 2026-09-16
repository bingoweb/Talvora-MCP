using Talvora.Gateway.Tools;

namespace Talvora.Foundation.Tests;

public sealed class FileToolTests
{
    [Fact]
    public async Task Write_then_read_then_delete_round_trip_works()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "sample.txt");

        try
        {
            await FileTools.WriteText(path, "talvora");
            var content = await FileTools.ReadText(path);
            Assert.Equal("talvora", content);

            FileTools.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void List_returns_created_entry()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "entry.txt");
        File.WriteAllText(path, "x");

        try
        {
            var entries = FileTools.List(root);
            Assert.Contains(entries, e => e.Name == "entry.txt" && !e.IsDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

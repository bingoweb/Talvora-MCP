using Talvora.Modules.FileSystem;

namespace Talvora.FileSystem.Tests;

[TestClass]
public sealed class FileSystemServiceTests
{
    [TestMethod]
    public async Task WriteTextAsyncAndReadTextAsyncRoundTripContent()
    {
        var service = new FileSystemService();
        var root = Path.Combine(Path.GetTempPath(), $"talvora-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "nested", "sample.txt");

        try
        {
            await service.WriteTextAsync(path, "Talvora", createParentDirectory: true);
            var content = await service.ReadTextAsync(path);

            Assert.AreEqual("Talvora", content);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task DeleteFileAsyncRemovesExistingFile()
    {
        var service = new FileSystemService();
        var path = Path.Combine(Path.GetTempPath(), $"talvora-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "delete me");

        await service.DeleteFileAsync(path);

        Assert.IsFalse(File.Exists(path));
    }
}

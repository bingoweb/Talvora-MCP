using System.Reflection;
using ModelContextProtocol;
using Talvora.Shared;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunKnowledgeS6LocalAsync()
    {
        const string rootsVariable = "TALVORA_KNOWLEDGE_ROOTS";
        var originalRoots = Environment.GetEnvironmentVariable(rootsVariable);
        var workspace = Path.Combine(
            Path.GetTempPath(), "Talvora-S6-" + Guid.NewGuid().ToString("N"));
        var allowed = Path.Combine(workspace, "allowed");
        var denied = Path.Combine(workspace, "denied");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(denied);

        static void Check(bool condition, string reason)
        {
            if (!condition)
            {
                throw new InvalidOperationException("S6: " + reason);
            }
        }

        static async Task ExpectOutsideAsync(string id)
        {
            try
            {
                await KnowledgeTools.Fetch(id);
            }
            catch (McpException ex) when (
                ex.Message.Contains("talvora_read_text_range", StringComparison.Ordinal))
            {
                return;
            }
            throw new InvalidOperationException(
                "S6: fetch accepted an out-of-root document: " + Path.GetFileName(id));
        }

        try
        {
            Environment.SetEnvironmentVariable(rootsVariable, null);
            var rootsMethod = typeof(KnowledgeTools).GetMethod(
                "GetSearchRoots", BindingFlags.NonPublic | BindingFlags.Static)!;
            var defaultRoots = ((IEnumerable<string>)rootsMethod.Invoke(null, null)!)
                .ToArray();

            var user = WindowsSessionLauncher.GetActiveInteractiveUser();
            var profile = Path.GetFullPath(user.UserProfile!);
            var profilesParent = Path.GetDirectoryName(profile)!;
            var publicDocs = Path.GetFullPath(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments));
            foreach (var defaultRoot in defaultRoots)
            {
                var underUsers = defaultRoot.StartsWith(
                    profilesParent + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
                var inActiveProfile = defaultRoot.StartsWith(
                    profile + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
                var inPublicDocs = defaultRoot.StartsWith(
                    publicDocs, StringComparison.OrdinalIgnoreCase);
                Check(!underUsers || inActiveProfile || inPublicDocs,
                    "a different local Windows user's profile was included by default");
            }
            Console.WriteLine("PASS S6 active-session roots exclude other profiles");

            Environment.SetEnvironmentVariable(rootsVariable, allowed);
            var inside = Path.Combine(allowed, "inside-s6.txt");
            var outside = Path.Combine(denied, "outside-s6.txt");
            const string searched = "S6_BOUNDARY_ALLOWED_MARKER_4EDB";
            await File.WriteAllTextAsync(inside, searched);
            await File.WriteAllTextAsync(outside, searched);

            var result = await KnowledgeTools.Search(searched);
            Check(result.Results.Count == 1 &&
                string.Equals(Path.GetFullPath(result.Results[0].Id), inside,
                    StringComparison.OrdinalIgnoreCase),
                "search did not stay inside the overridden root");
            var fetched = await KnowledgeTools.Fetch(inside);
            Check(fetched.Text == searched, "authorized knowledge fetch failed");
            await ExpectOutsideAsync(outside);
            Console.WriteLine("PASS S6 allowed fetch and outside-root rejection");

            var symlink = Path.Combine(allowed, "symlink-outside-s6.txt");
            try
            {
                File.CreateSymbolicLink(symlink, outside);
                await ExpectOutsideAsync(symlink);
                Console.WriteLine("PASS S6 symlink escape rejected after final-path resolution");
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException or IOException or
                System.ComponentModel.Win32Exception)
            {
                Console.WriteLine("SKIP S6 optional OS symlink fixture: " +
                    ex.GetType().Name);
            }

            var offline = Path.Combine(allowed, "offline-s6.txt");
            await File.WriteAllTextAsync(offline, "S6_OFFLINE_MARKER_1D7F");
            var originalAttributes = File.GetAttributes(offline);
            try
            {
                File.SetAttributes(offline, originalAttributes | FileAttributes.Offline);
                Check((File.GetAttributes(offline) & FileAttributes.Offline) != 0,
                    "the offline attribute was not applied");
                var offlineSearch = await KnowledgeTools.Search("S6_OFFLINE_MARKER_1D7F");
                Check(offlineSearch.Results.Count == 0,
                    "offline content was read by knowledge search");
                try
                {
                    await KnowledgeTools.Fetch(offline);
                    throw new InvalidOperationException(
                        "S6: fetch opened an offline placeholder");
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("otomatik indirilmeyecek",
                        StringComparison.Ordinal))
                {
                    Console.WriteLine("PASS S6 offline placeholder skipped");
                }
            }
            finally
            {
                File.SetAttributes(offline, originalAttributes);
            }

            var cloudAttributes = typeof(KnowledgeTools).GetMethod(
                "IsCloudOnly", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var bit in new uint[] { 0x00001000, 0x00040000, 0x00400000 })
            {
                Check((bool)cloudAttributes.Invoke(null, [(FileAttributes)bit])!,
                    "a cloud placeholder attribute was ignored");
            }

            var cloudTag = typeof(KnowledgeTools)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(method => method.Name == "IsCloudReparseTag" &&
                    method.GetParameters().Length == 1);
            Check((bool)cloudTag.Invoke(null, [0x9000001Au])! &&
                  (bool)cloudTag.Invoke(null, [0x9000101Au])! &&
                  !(bool)cloudTag.Invoke(null, [0xA000000Cu])!,
                "cloud reparse tag classification failed");
            Console.WriteLine("PASS S6 RECALL flags and cloud reparse tag classification");
            Console.WriteLine("TALVORA S6 KNOWLEDGE LOCAL GREEN");
        }
        finally
        {
            Environment.SetEnvironmentVariable(rootsVariable, originalRoots);
            Directory.Delete(workspace, recursive: true);
        }
    }
}

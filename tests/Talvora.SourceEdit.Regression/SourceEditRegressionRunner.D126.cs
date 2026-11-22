using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    public static Task D126UserGlobalExcludesAsync()
    {
        static void InTemporaryFolder(string name, Action<string> action)
        {
            var root = Path.Combine(Path.GetTempPath(), "talvora-d126-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                action(root);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        static string MakeFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);
            return Path.GetFullPath(path);
        }

        InTemporaryFolder("d126-default-home", root =>
        {
            var home = Path.Combine(root, "home");
            var ignore = MakeFile(Path.Combine(home, ".config", "git", "ignore"));
            Assert(GitUserExcludes.ResolvePath(home, null, null) == ignore,
                "d126-default-home: HOME's default git ignore was not used.");
        });

        InTemporaryFolder("d126-xdg", root =>
        {
            var home = Path.Combine(root, "home");
            var xdg = Path.Combine(root, "xdg");
            var xdgIgnore = MakeFile(Path.Combine(xdg, "git", "ignore"));
            Assert(GitUserExcludes.ResolvePath(home, xdg, null) == xdgIgnore,
                "d126-xdg: XDG_CONFIG_HOME/git/ignore was not used.");
            Assert(GitUserExcludes.ResolvePath(home, "", null) is null,
                "d126-xdg: empty XDG_CONFIG_HOME must fall back to missing HOME ignore.");
            var homeIgnore = MakeFile(Path.Combine(home, ".config", "git", "ignore"));
            Assert(GitUserExcludes.ResolvePath(home, "", null) == homeIgnore,
                "d126-xdg: empty XDG_CONFIG_HOME must use HOME default.");
        });

        InTemporaryFolder("d126-configured-tilde", root =>
        {
            var home = Path.Combine(root, "home");
            var expected = MakeFile(Path.Combine(home, "my-ignore"));
            Assert(GitUserExcludes.ResolvePath(home, null, "~/my-ignore") == expected,
                "d126-configured-tilde: ~/ must expand to HOME.");
        });

        InTemporaryFolder("d126-configured-absolute", root =>
        {
            var home = Path.Combine(root, "home");
            var expected = MakeFile(Path.Combine(root, "custom-ignore"));
            Assert(GitUserExcludes.ResolvePath(home, null, expected) == expected,
                "d126-configured-absolute: absolute ignore file was not resolved.");
            var missing = Path.Combine(root, "does-not-exist");
            Assert(GitUserExcludes.ResolvePath(home, null, ":(optional)" + missing) is null,
                "d126-configured-absolute: missing optional ignore must be null.");
            Assert(GitUserExcludes.ResolvePath(home, null, "relative-ignore") is null,
                "d126-configured-absolute: relative paths must not be accepted.");
            Assert(GitUserExcludes.ResolvePath(home, null, "~other/ignore") is null,
                "d126-configured-absolute: another user's home must not be expanded.");
        });

        InTemporaryFolder("d126-missing", root =>
        {
            var home = Path.Combine(root, "home");
            Assert(GitUserExcludes.ResolvePath(home, null, null) is null,
                "d126-missing: non-existing default ignore must be null.");
        });

        InTemporaryFolder("d126-argument", _ =>
        {
            string[] input = ["-c", "core.quotepath=false", "status"];
            string[] expected =
            [
                "-c", "core.quotepath=false",
                "-c", "core.excludesFile=C:/Users/x/.config/git/ignore", "status",
            ];
            Assert(GitUserExcludes.AddArgument(input, @"C:\Users\x\.config\git\ignore").SequenceEqual(expected),
                "d126-argument: excludes option must follow existing -c options.");
            Assert(GitUserExcludes.AddArgument(input, null).SequenceEqual(input),
                "d126-argument: null excludes must leave arguments unchanged.");
            string[] already =
            [
                "-c", "core.quotepath=false",
                "-c", "CORE.EXCLUDESFILE=C:/custom/ignore", "status",
            ];
            Assert(GitUserExcludes.AddArgument(already, @"C:\Users\x\.config\git\ignore").SequenceEqual(already),
                "d126-argument: existing excludes configuration must not be overridden.");
        });

        return Task.CompletedTask;
    }
}

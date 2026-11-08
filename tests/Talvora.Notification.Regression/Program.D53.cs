using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;
using Talvora.Tools;

internal static partial class Program
{
    private static void AssertD53DescriptionsAsWholeSentences()
    {
        var checkedCount = 0;
        foreach (var type in typeof(AgentBriefTools).Assembly.GetTypes())
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var tool = method.GetCustomAttribute<McpServerToolAttribute>();
            var description = method.GetCustomAttribute<DescriptionAttribute>();
            if (string.IsNullOrWhiteSpace(tool?.Name) || description is null)
                continue;
            checkedCount++;
            var name = tool.Name;
            var text = description.Description;
            var once = TalvoraMcpToolMetadataPolicy.NormalizeDescription(name, text)!;
            var twice = TalvoraMcpToolMetadataPolicy.NormalizeDescription(name, once)!;
            if (once != twice)
                throw new InvalidOperationException($"D53: {name} changes when normalized twice: {once} → {twice}");
            if (Regex.IsMatch(once, @"\ban caller-supplied\b", RegexOptions.IgnoreCase) || once.EndsWith(';'))
                throw new InvalidOperationException($"D53: {name} reads: {once}");
            var expected = Regex.Matches(text, "any accessible", RegexOptions.IgnoreCase).Count;
            if (Regex.Matches(once, "an accessible", RegexOptions.IgnoreCase).Count < expected)
                throw new InvalidOperationException($"D53: {name} reads: {once}");
        }
        if (checkedCount < 200)
            throw new InvalidOperationException($"D53: only {checkedCount} tools checked.");
        Console.WriteLine("D53_DESCRIPTIONS_GREEN");
    }
}

namespace Talvora.Memory;

public sealed partial class TalvoraMemoryStore
{
    public async Task<TalvoraMemoryHandoffCandidatesResult> HandoffCandidatesAsync(
        string project,
        int limit,
        double minImportance,
        double minConfidence,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Handoff candidate limit must be between 1 and 100.");
        }
        ValidateScore(minImportance, nameof(minImportance));
        ValidateScore(minConfidence, nameof(minConfidence));

        var normalizedProject = NormalizeProjectIdentity(project)
            ?? throw new ArgumentException(
                "Project identity cannot be empty.",
                nameof(project));

        await EnsureInitializedAsync(cancellationToken);
        var candidates = new List<TalvoraMemoryHandoffCandidate>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, category, title, content, importance, confidence,
                   source, updated_utc, claim_key
            FROM memory_items
            WHERE scope = 'project'
              AND project = $project
              AND superseded_by IS NULL
              AND (expires_utc IS NULL OR expires_utc > $now)
              AND importance >= $minImportance
              AND confidence >= $minConfidence
              AND category IN (
                    'decision', 'preference', 'lesson', 'solution',
                    'architecture', 'workflow', 'fact'
                  )
            ORDER BY
                importance DESC,
                confidence DESC,
                updated_utc DESC,
                id
            LIMIT $limit;
            """;
        Add(command, "$project", normalizedProject);
        Add(command, "$now", Format(DateTimeOffset.UtcNow));
        Add(command, "$minImportance", minImportance);
        Add(command, "$minConfidence", minConfidence);
        Add(command, "$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(
                new TalvoraMemoryHandoffCandidate(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetDouble(4),
                    reader.GetDouble(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    Parse(reader.GetString(7)),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return new TalvoraMemoryHandoffCandidatesResult(
            normalizedProject,
            candidates.Count,
            candidates,
            BuildHandoffMarkdown(candidates));
    }

    private static string BuildHandoffMarkdown(
        IReadOnlyList<TalvoraMemoryHandoffCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return "## Hafıza kaynaklı handoff adayları\n\n- Yüksek değerli aktif proje hafızası bulunamadı.\n";
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("## Hafıza kaynaklı handoff adayları");
        builder.AppendLine();

        foreach (var group in candidates
                     .GroupBy(candidate => candidate.Category)
                     .OrderBy(group => HandoffCategoryOrder(group.Key))
                     .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            builder.Append("### ");
            builder.AppendLine(HandoffCategoryLabel(group.Key));
            builder.AppendLine();
            foreach (var candidate in group)
            {
                builder.Append("- **");
                builder.Append(CollapseForMarkdown(candidate.Title, 220));
                builder.Append("** — ");
                builder.Append(CollapseForMarkdown(candidate.Content, 900));
                builder.Append(" _(önem ");
                builder.Append(candidate.Importance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                builder.Append(", güven ");
                builder.Append(candidate.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(candidate.Source))
                {
                    builder.Append(", kaynak ");
                    builder.Append(CollapseForMarkdown(candidate.Source!, 80));
                }
                builder.AppendLine(")_");
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static int HandoffCategoryOrder(string category) =>
        category switch
        {
            "decision" => 0,
            "architecture" => 1,
            "workflow" => 2,
            "preference" => 3,
            "solution" => 4,
            "lesson" => 5,
            "fact" => 6,
            _ => 99,
        };

    private static string HandoffCategoryLabel(string category) =>
        category switch
        {
            "decision" => "Kararlar",
            "architecture" => "Mimari",
            "workflow" => "İş akışı",
            "preference" => "Tercihler",
            "solution" => "Çözümler",
            "lesson" => "Öğrenilenler",
            "fact" => "Doğrulanmış bilgiler",
            _ => category,
        };

    private static string CollapseForMarkdown(
        string value,
        int maxCharacters)
    {
        var collapsed = string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
        if (collapsed.Length <= maxCharacters)
        {
            return collapsed;
        }
        return collapsed[..Math.Max(1, maxCharacters - 1)] + "…";
    }
}

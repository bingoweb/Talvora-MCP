using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Talvora.Memory;

public sealed partial class TalvoraMemoryStore
{
    public async Task<TalvoraMemorySessionCloseResult> CloseSessionAsync(
        string sessionId,
        string summary,
        string? project,
        IReadOnlyList<TalvoraMemoryCandidateInput>? candidates,
        bool autoPromote,
        CancellationToken cancellationToken)
    {
        ValidateText(sessionId, nameof(sessionId), 256);
        ValidateText(summary, nameof(summary), 16 * 1024);
        await EnsureInitializedAsync(cancellationToken);

        var normalizedSession = sessionId.Trim();
        var normalizedProject = NormalizeProjectIdentity(project);
        var existing =
            await GetSessionCloseAsync(normalizedSession, cancellationToken);
        if (existing is not null)
        {
            return existing with { Replayed = true };
        }

        var normalizedCandidates = (candidates ?? [])
            .Select(input => NormalizeCandidateInput(
                normalizedSession,
                normalizedProject,
                input))
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.Transaction = (SqliteTransaction)transaction;
            sessionCommand.CommandText =
                """
                INSERT INTO memory_sessions(
                    session_id, project, summary, created_utc)
                VALUES($sessionId, $project, $summary, $createdUtc);
                """;
            Add(sessionCommand, "$sessionId", normalizedSession);
            Add(sessionCommand, "$project", normalizedProject);
            Add(sessionCommand, "$summary", summary.Trim());
            Add(sessionCommand, "$createdUtc", Format(now));
            await sessionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var candidate in normalizedCandidates)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                INSERT INTO memory_candidates(
                    id, session_id, project, target_scope,
                    category, title, content,
                    importance, confidence, source, source_ref,
                    claim_key, retention_class,
                    promotion_score, recommendation,
                    status, created_utc)
                VALUES(
                    $id, $sessionId, $project, $targetScope,
                    $category, $title, $content,
                    $importance, $confidence, $source, $sourceRef,
                    $claimKey, $retentionClass,
                    $promotionScore, $recommendation,
                    'pending', $createdUtc);
                """;
            Add(command, "$id", candidate.Id);
            Add(command, "$sessionId", candidate.SessionId);
            Add(command, "$project", candidate.Project);
            Add(command, "$targetScope", candidate.TargetScope);
            Add(command, "$category", candidate.Category);
            Add(command, "$title", candidate.Title);
            Add(command, "$content", candidate.Content);
            Add(command, "$importance", candidate.Importance);
            Add(command, "$confidence", candidate.Confidence);
            Add(command, "$source", candidate.Source);
            Add(command, "$sourceRef", candidate.SourceReference);
            Add(command, "$claimKey", candidate.ClaimKey);
            Add(command, "$retentionClass", candidate.RetentionClass);
            Add(command, "$promotionScore", candidate.PromotionScore);
            Add(command, "$recommendation", candidate.Recommendation);
            Add(command, "$createdUtc", Format(now));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (autoPromote)
        {
            foreach (var candidate in normalizedCandidates.Where(
                         item => string.Equals(
                             item.Recommendation,
                             "auto-promote",
                             StringComparison.Ordinal)))
            {
                await PromoteCandidateAsync(
                    candidate.Id,
                    "policy-auto-promotion",
                    cancellationToken);
            }
        }

        return (await GetSessionCloseAsync(
            normalizedSession,
            cancellationToken))!;
    }

    public async Task<TalvoraMemoryCandidateListResult> ListCandidatesAsync(
        string? sessionId,
        string? project,
        string? status,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Candidate limit must be between 1 and 500.");
        }

        var normalizedStatus = NormalizeCandidateStatus(status);
        await EnsureInitializedAsync(cancellationToken);
        var items = new List<TalvoraMemoryCandidate>();
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, session_id, project, target_scope,
                   category, title, content, importance, confidence,
                   source, source_ref, claim_key, retention_class,
                   promotion_score, recommendation, status,
                   promoted_memory_id, resolution_reason,
                   created_utc, resolved_utc
            FROM memory_candidates
            WHERE ($sessionId IS NULL OR session_id = $sessionId)
              AND ($project IS NULL OR project = $project)
              AND ($status IS NULL OR status = $status)
            ORDER BY created_utc DESC
            LIMIT $limit;
            """;
        Add(command, "$sessionId", NormalizeOptional(sessionId));
        Add(command, "$project", NormalizeProjectIdentity(project));
        Add(command, "$status", normalizedStatus);
        Add(command, "$limit", limit);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadCandidate(reader));
        }

        return new TalvoraMemoryCandidateListResult(items.Count, items);
    }

    public async Task<TalvoraMemoryCandidateResolutionResult>
        PromoteCandidateAsync(
            string candidateId,
            string? reason,
            CancellationToken cancellationToken)
    {
        ValidateId(candidateId);
        await EnsureInitializedAsync(cancellationToken);
        var candidate =
            await GetCandidateAsync(candidateId, cancellationToken);
        if (candidate is null)
        {
            return new TalvoraMemoryCandidateResolutionResult(
                false, false, null, null);
        }

        if (string.Equals(
                candidate.Status,
                "promoted",
                StringComparison.Ordinal))
        {
            var existingMemory = candidate.PromotedMemoryId is null
                ? null
                : await GetAsync(
                    candidate.PromotedMemoryId,
                    cancellationToken);
            return new TalvoraMemoryCandidateResolutionResult(
                true, true, candidate, existingMemory);
        }

        if (string.Equals(
                candidate.Status,
                "rejected",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Rejected candidates cannot be promoted.");
        }

        var memory = await RememberAsync(
            candidate.TargetScope,
            candidate.Category,
            candidate.Title,
            candidate.Content,
            candidate.Project,
            null,
            candidate.Importance,
            candidate.Confidence,
            candidate.Source,
            candidate.SourceReference,
            null,
            candidate.RetentionClass,
            candidate.ClaimKey,
            cancellationToken);

        var resolvedAt = DateTimeOffset.UtcNow;
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE memory_candidates
            SET status = 'promoted',
                promoted_memory_id = $memoryId,
                resolution_reason = $reason,
                resolved_utc = $resolvedUtc
            WHERE id = $candidateId
              AND status = 'pending';
            """;
        Add(command, "$memoryId", memory.Id);
        Add(command, "$reason",
            NormalizeOptional(reason) ?? "explicit-promotion");
        Add(command, "$resolvedUtc", Format(resolvedAt));
        Add(command, "$candidateId", candidateId);
        var changed =
            await command.ExecuteNonQueryAsync(cancellationToken);

        if (changed == 0)
        {
            await ForgetAsync(memory.Id, cancellationToken);
        }

        var updated = await GetCandidateAsync(
            candidateId,
            cancellationToken);
        var finalMemory = updated?.PromotedMemoryId is null
            ? null
            : await GetAsync(updated.PromotedMemoryId, cancellationToken);
        return new TalvoraMemoryCandidateResolutionResult(
            updated is not null,
            changed == 0,
            updated,
            finalMemory);
    }

    public async Task<TalvoraMemoryCandidateResolutionResult>
        RejectCandidateAsync(
            string candidateId,
            string? reason,
            CancellationToken cancellationToken)
    {
        ValidateId(candidateId);
        await EnsureInitializedAsync(cancellationToken);
        var candidate =
            await GetCandidateAsync(candidateId, cancellationToken);
        if (candidate is null)
        {
            return new TalvoraMemoryCandidateResolutionResult(
                false, false, null, null);
        }

        if (string.Equals(
                candidate.Status,
                "rejected",
                StringComparison.Ordinal))
        {
            return new TalvoraMemoryCandidateResolutionResult(
                true, true, candidate, null);
        }

        if (string.Equals(
                candidate.Status,
                "promoted",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Promoted candidates cannot be rejected.");
        }

        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE memory_candidates
            SET status = 'rejected',
                resolution_reason = $reason,
                resolved_utc = $resolvedUtc
            WHERE id = $candidateId
              AND status = 'pending';
            """;
        Add(command, "$reason",
            NormalizeOptional(reason) ?? "explicit-rejection");
        Add(command, "$resolvedUtc", Format(DateTimeOffset.UtcNow));
        Add(command, "$candidateId", candidateId);
        var changed =
            await command.ExecuteNonQueryAsync(cancellationToken);

        var updated = await GetCandidateAsync(
            candidateId,
            cancellationToken);
        return new TalvoraMemoryCandidateResolutionResult(
            updated is not null,
            changed == 0,
            updated,
            null);
    }

    public async Task<TalvoraMemorySessionForgetResult> ForgetSessionAsync(
        string sessionId,
        bool deletePromotedMemories,
        CancellationToken cancellationToken)
    {
        ValidateText(sessionId, nameof(sessionId), 256);
        await EnsureInitializedAsync(cancellationToken);
        var normalizedSession = sessionId.Trim();
        var list = await ListCandidatesAsync(
            normalizedSession,
            null,
            null,
            500,
            cancellationToken);
        var existing =
            await GetSessionCloseAsync(normalizedSession, cancellationToken);
        if (existing is null)
        {
            return new TalvoraMemorySessionForgetResult(
                false,
                normalizedSession,
                0,
                0);
        }

        var promotedIds = list.Items
            .Where(item => item.PromotedMemoryId is not null)
            .Select(item => item.PromotedMemoryId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var deletedMemories = 0;
        if (deletePromotedMemories)
        {
            foreach (var memoryId in promotedIds)
            {
                if (await ForgetAsync(memoryId, cancellationToken))
                {
                    deletedMemories++;
                }
            }
        }

        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM memory_sessions WHERE session_id = $sessionId;";
        Add(command, "$sessionId", normalizedSession);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new TalvoraMemorySessionForgetResult(
            true,
            normalizedSession,
            list.Count,
            deletedMemories);
    }

    private TalvoraMemoryCandidate NormalizeCandidateInput(
        string sessionId,
        string? project,
        TalvoraMemoryCandidateInput input)
    {
        ValidateCategory(input.Category);
        ValidateText(input.Title, nameof(input.Title), 512);
        ValidateText(input.Content, nameof(input.Content), 256 * 1024);
        ValidateScore(input.Importance, nameof(input.Importance));
        ValidateScore(input.Confidence, nameof(input.Confidence));

        var targetScope = string.IsNullOrWhiteSpace(input.TargetScope)
            ? project is null ? "user" : "project"
            : input.TargetScope.Trim().ToLowerInvariant();
        ValidateScope(targetScope);
        if (string.Equals(
                targetScope,
                "session",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Candidate targetScope cannot be session.");
        }

        var retention = ResolveRetentionClass(
            targetScope,
            input.Category,
            input.RetentionClass);
        var source = NormalizeOptional(input.Source);
        var authority = GetSourceAuthority(source);
        var score =
            (authority * 0.35) +
            (input.Confidence * 0.35) +
            (input.Importance * 0.30);
        var recommendation = ResolvePromotionRecommendation(
            input.Category,
            authority,
            input.Confidence,
            input.Importance,
            score);

        return new TalvoraMemoryCandidate(
            Guid.NewGuid().ToString("N"),
            sessionId,
            project,
            targetScope,
            input.Category,
            input.Title.Trim(),
            input.Content.Trim(),
            input.Importance,
            input.Confidence,
            source,
            NormalizeOptional(input.SourceReference),
            NormalizeClaimKey(input.ClaimKey),
            retention,
            score,
            recommendation,
            "pending",
            null,
            null,
            DateTimeOffset.UtcNow,
            null);
    }

    private static string ResolvePromotionRecommendation(
        string category,
        double authority,
        double confidence,
        double importance,
        double score)
    {
        if (category is "todo" or "error")
        {
            return "review";
        }

        if (category == "fact")
        {
            return authority >= 1.0 &&
                   confidence >= 0.95 &&
                   importance >= 0.70 &&
                   score >= 0.88
                ? "auto-promote"
                : "review";
        }

        return authority >= 0.90 &&
               confidence >= 0.90 &&
               importance >= 0.70 &&
               score >= 0.85
            ? "auto-promote"
            : "review";
    }

    private async Task<TalvoraMemoryCandidate?> GetCandidateAsync(
        string candidateId,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, session_id, project, target_scope,
                   category, title, content, importance, confidence,
                   source, source_ref, claim_key, retention_class,
                   promotion_score, recommendation, status,
                   promoted_memory_id, resolution_reason,
                   created_utc, resolved_utc
            FROM memory_candidates
            WHERE id = $id;
            """;
        Add(command, "$id", candidateId);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadCandidate(reader)
            : null;
    }

    private async Task<TalvoraMemorySessionCloseResult?>
        GetSessionCloseAsync(
            string sessionId,
            CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        string? project;
        string summary;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT project, summary
                FROM memory_sessions
                WHERE session_id = $sessionId;
                """;
            Add(command, "$sessionId", sessionId);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }
            project =
                reader.IsDBNull(0) ? null : reader.GetString(0);
            summary = reader.GetString(1);
        }

        var list = await ListCandidatesAsync(
            sessionId,
            project,
            null,
            500,
            cancellationToken);
        return new TalvoraMemorySessionCloseResult(
            sessionId,
            project,
            summary,
            false,
            list.Count,
            list.Items);
    }

    private static string? NormalizeCandidateStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = status.Trim().ToLowerInvariant();
        if (normalized is not ("pending" or "promoted" or "rejected"))
        {
            throw new ArgumentException(
                $"Unsupported candidate status '{status}'.",
                nameof(status));
        }
        return normalized;
    }

    private static TalvoraMemoryCandidate ReadCandidate(
        SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetDouble(7),
            reader.GetDouble(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetString(12),
            reader.GetDouble(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            Parse(reader.GetString(18)),
            reader.IsDBNull(19) ? null : Parse(reader.GetString(19)));
}

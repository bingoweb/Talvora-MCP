using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Talvora.Memory;

public sealed partial class TalvoraMemoryStore
{
    private static readonly HashSet<string> LearningEditors =
        new(StringComparer.Ordinal)
        {
            "talvora_apply_patch",
            "talvora_apply_edits",
            "talvora_structural_edit",
            "talvora_semantic_edit",
        };

    private static readonly HashSet<string> LearningVerifiers =
        new(StringComparer.Ordinal)
        {
            "talvora_dotnet_build",
            "talvora_dotnet_test",
            "talvora_msbuild_run",
        };

    public static bool IsAutomaticLearningTool(string toolName) =>
        LearningEditors.Contains(toolName) ||
        LearningVerifiers.Contains(toolName);

    public async Task ObserveToolCallAsync(
        string toolName,
        string? project,
        string outcome,
        string? failureKind,
        long elapsedMilliseconds,
        CancellationToken cancellationToken)
    {
        if (!IsAutomaticLearningTool(toolName) ||
            toolName.StartsWith(
                "talvora_memory_",
                StringComparison.Ordinal))
        {
            return;
        }

        var normalizedProject = NormalizeProjectIdentity(project);
        if (normalizedProject is null ||
            await IsLearningSuppressedAsync(
                normalizedProject,
                toolName,
                cancellationToken))
        {
            return;
        }

        var normalizedOutcome = outcome.Trim().ToLowerInvariant();
        if (normalizedOutcome is not ("success" or "failure" or "cancelled"))
        {
            throw new ArgumentException(
                $"Unsupported learning outcome '{outcome}'.",
                nameof(outcome));
        }

        await EnsureInitializedAsync(cancellationToken);
        var observationId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        await using (var connection =
            await OpenConnectionAsync(cancellationToken))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO memory_learning_observations(
                    id, project, tool_name, tool_role,
                    outcome, failure_kind, elapsed_ms,
                    recovery_linked, created_utc)
                VALUES(
                    $id, $project, $toolName, $toolRole,
                    $outcome, $failureKind, $elapsedMs,
                    0, $createdUtc);

                DELETE FROM memory_learning_observations
                WHERE created_utc < $cutoffUtc;
                """;
            Add(command, "$id", observationId);
            Add(command, "$project", normalizedProject);
            Add(command, "$toolName", toolName);
            Add(command, "$toolRole",
                LearningEditors.Contains(toolName)
                    ? "editor"
                    : "verifier");
            Add(command, "$outcome", normalizedOutcome);
            Add(command, "$failureKind",
                NormalizeOptional(failureKind));
            Add(command, "$elapsedMs",
                Math.Clamp(elapsedMilliseconds, 0, 86_400_000));
            Add(command, "$createdUtc", Format(now));
            Add(command, "$cutoffUtc", Format(now.AddDays(-7)));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (normalizedOutcome == "success" &&
            LearningVerifiers.Contains(toolName))
        {
            await TryCreateRecoveryPatternAsync(
                normalizedProject,
                toolName,
                now,
                cancellationToken);
        }
    }

    public async Task<TalvoraLearningStatusResult> LearningStatusAsync(
        string? project,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var normalizedProject = NormalizeProjectIdentity(project);
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*)
                 FROM memory_learning_observations
                 WHERE created_utc >= $cutoffUtc
                   AND ($project IS NULL OR project = $project)),
                (SELECT COUNT(*)
                 FROM memory_learning_patterns
                 WHERE status = 'pending'
                   AND ($project IS NULL OR project = $project)),
                (SELECT COUNT(*)
                 FROM memory_learning_patterns
                 WHERE status = 'promoted'
                   AND ($project IS NULL OR project = $project)),
                (SELECT COUNT(*)
                 FROM memory_learning_patterns
                 WHERE status = 'suppressed'
                   AND ($project IS NULL OR project = $project)),
                (SELECT COUNT(*)
                 FROM memory_learning_suppressions
                 WHERE $project IS NULL
                    OR project IS NULL
                    OR project = $project);
            """;
        Add(command, "$cutoffUtc",
            Format(DateTimeOffset.UtcNow.AddDays(-7)));
        Add(command, "$project", normalizedProject);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new TalvoraLearningStatusResult(
            normalizedProject,
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4));
    }

    public async Task<TalvoraLearningPatternListResult>
        ListLearningPatternsAsync(
            string? project,
            string? status,
            int limit,
            CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Learning pattern limit must be between 1 and 500.");
        }
        var normalizedStatus =
            string.IsNullOrWhiteSpace(status)
                ? null
                : status.Trim().ToLowerInvariant();
        if (normalizedStatus is not null &&
            normalizedStatus is not ("pending" or "promoted" or "suppressed"))
        {
            throw new ArgumentException(
                $"Unsupported learning pattern status '{status}'.",
                nameof(status));
        }

        await EnsureInitializedAsync(cancellationToken);
        var items = new List<TalvoraLearningPattern>();
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT fingerprint, project, pattern_kind,
                   title, content, source_tools,
                   occurrence_count, confidence, importance,
                   status, promoted_memory_id,
                   first_seen_utc, last_seen_utc
            FROM memory_learning_patterns
            WHERE ($project IS NULL OR project = $project)
              AND ($status IS NULL OR status = $status)
            ORDER BY last_seen_utc DESC
            LIMIT $limit;
            """;
        Add(command, "$project", NormalizeProjectIdentity(project));
        Add(command, "$status", normalizedStatus);
        Add(command, "$limit", limit);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadLearningPattern(reader));
        }
        return new TalvoraLearningPatternListResult(
            items.Count,
            items);
    }

    public async Task<TalvoraLearningSuppressionResult>
        SetLearningSuppressionAsync(
            string? project,
            string? toolName,
            bool suppressed,
            string? reason,
            CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var normalizedProject = NormalizeProjectIdentity(project);
        var normalizedTool = string.IsNullOrWhiteSpace(toolName)
            ? null
            : toolName.Trim();
        if (normalizedTool is not null &&
            !IsAutomaticLearningTool(normalizedTool))
        {
            throw new ArgumentException(
                $"Tool '{toolName}' is not in the automatic-learning allowlist.",
                nameof(toolName));
        }
        var key = HashLearningKey(
            normalizedProject ?? "*",
            normalizedTool ?? "*");
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        if (suppressed)
        {
            command.CommandText =
                """
                INSERT INTO memory_learning_suppressions(
                    suppression_key, project, tool_name,
                    reason, created_utc)
                VALUES(
                    $key, $project, $toolName,
                    $reason, $createdUtc)
                ON CONFLICT(suppression_key) DO UPDATE SET
                    reason = excluded.reason,
                    created_utc = excluded.created_utc;

                UPDATE memory_learning_patterns
                SET status = 'suppressed',
                    last_seen_utc = $createdUtc
                WHERE status = 'pending'
                  AND ($project IS NULL OR project = $project)
                  AND ($toolName IS NULL OR instr(source_tools, $toolName) > 0);
                """;
            Add(command, "$project", normalizedProject);
            Add(command, "$toolName", normalizedTool);
            Add(command, "$reason", NormalizeOptional(reason));
            Add(command, "$createdUtc", Format(DateTimeOffset.UtcNow));
        }
        else
        {
            command.CommandText =
                """
                DELETE FROM memory_learning_suppressions
                WHERE suppression_key = $key;
                """;
        }
        Add(command, "$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new TalvoraLearningSuppressionResult(
            suppressed,
            normalizedProject,
            normalizedTool,
            NormalizeOptional(reason));
    }

    public async Task<TalvoraLearningPatternResolutionResult>
        PromoteLearningPatternAsync(
            string fingerprint,
            CancellationToken cancellationToken)
    {
        ValidateText(fingerprint, nameof(fingerprint), 128);
        await EnsureInitializedAsync(cancellationToken);
        var pattern =
            await GetLearningPatternAsync(fingerprint, cancellationToken);
        if (pattern is null)
        {
            return new TalvoraLearningPatternResolutionResult(
                false, false, null, null);
        }
        if (pattern.Status == "promoted")
        {
            var existing = pattern.PromotedMemoryId is null
                ? null
                : await GetAsync(
                    pattern.PromotedMemoryId,
                    cancellationToken);
            return new TalvoraLearningPatternResolutionResult(
                true, true, pattern, existing);
        }
        if (pattern.Status == "suppressed")
        {
            throw new InvalidOperationException(
                "Suppressed learning patterns cannot be promoted.");
        }

        var memory = await PromotePatternCoreAsync(
            pattern,
            cancellationToken);
        var updated =
            await GetLearningPatternAsync(
                fingerprint,
                cancellationToken);
        return new TalvoraLearningPatternResolutionResult(
            true, false, updated, memory);
    }

    public async Task<TalvoraLearningPatternForgetResult>
        ForgetLearningPatternAsync(
            string fingerprint,
            bool deletePromotedMemory,
            CancellationToken cancellationToken)
    {
        ValidateText(fingerprint, nameof(fingerprint), 128);
        await EnsureInitializedAsync(cancellationToken);
        var pattern =
            await GetLearningPatternAsync(fingerprint, cancellationToken);
        if (pattern is null)
        {
            return new TalvoraLearningPatternForgetResult(
                false, fingerprint, false);
        }

        var deletedMemory = false;
        if (deletePromotedMemory &&
            pattern.PromotedMemoryId is not null)
        {
            deletedMemory = await ForgetAsync(
                pattern.PromotedMemoryId,
                cancellationToken);
        }

        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM memory_learning_patterns
            WHERE fingerprint = $fingerprint;
            """;
        Add(command, "$fingerprint", fingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new TalvoraLearningPatternForgetResult(
            true,
            fingerprint,
            deletedMemory);
    }

    public async Task<TalvoraLearningDecisionResult>
        RecordLearningDecisionAsync(
            string title,
            string content,
            string? project,
            double importance,
            double confidence,
            string? claimKey,
            bool promote,
            CancellationToken cancellationToken)
    {
        ValidateText(title, nameof(title), 512);
        ValidateText(content, nameof(content), 64 * 1024);
        ValidateScore(importance, nameof(importance));
        ValidateScore(confidence, nameof(confidence));
        if (LooksLikeSecret(title) ||
            LooksLikeSecret(content) ||
            LooksLikeSecret(claimKey))
        {
            throw new InvalidOperationException(
                "Decision memory was rejected because it appears to contain a credential or secret.");
        }

        var sessionId =
            $"explicit-decision-{Guid.NewGuid():N}";
        var closed = await CloseSessionAsync(
            sessionId,
            "Explicit user/project decision candidate.",
            project,
            [
                new TalvoraMemoryCandidateInput(
                    "decision",
                    title,
                    content,
                    importance,
                    confidence,
                    "user",
                    "explicit-decision",
                    claimKey,
                    "durable",
                    project is null ? "user" : "project")
            ],
            false,
            cancellationToken);
        var candidate = closed.Candidates.Single();
        if (!promote)
        {
            return new TalvoraLearningDecisionResult(
                candidate,
                null);
        }

        var resolution = await PromoteCandidateAsync(
            candidate.Id,
            "explicit-decision-promotion",
            cancellationToken);
        return new TalvoraLearningDecisionResult(
            resolution.Candidate ?? candidate,
            resolution.Memory);
    }

    private async Task TryCreateRecoveryPatternAsync(
        string project,
        string verifierTool,
        DateTimeOffset successAt,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        string? failureId = null;
        DateTimeOffset failureAt = default;
        await using (var failure = connection.CreateCommand())
        {
            failure.CommandText =
                """
                SELECT id, created_utc
                FROM memory_learning_observations
                WHERE project = $project
                  AND tool_name = $toolName
                  AND outcome = 'failure'
                  AND recovery_linked = 0
                  AND created_utc >= $cutoffUtc
                  AND created_utc < $successUtc
                ORDER BY created_utc DESC
                LIMIT 1;
                """;
            Add(failure, "$project", project);
            Add(failure, "$toolName", verifierTool);
            Add(failure, "$cutoffUtc",
                Format(successAt.AddMinutes(-30)));
            Add(failure, "$successUtc", Format(successAt));
            await using var reader =
                await failure.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                failureId = reader.GetString(0);
                failureAt = Parse(reader.GetString(1));
            }
        }
        if (failureId is null)
        {
            return;
        }

        var editors = new List<string>();
        await using (var edits = connection.CreateCommand())
        {
            edits.CommandText =
                """
                SELECT tool_name
                FROM memory_learning_observations
                WHERE project = $project
                  AND tool_role = 'editor'
                  AND outcome = 'success'
                  AND created_utc > $failureUtc
                  AND created_utc < $successUtc
                ORDER BY created_utc ASC;
                """;
            Add(edits, "$project", project);
            Add(edits, "$failureUtc", Format(failureAt));
            Add(edits, "$successUtc", Format(successAt));
            await using var reader =
                await edits.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tool = reader.GetString(0);
                if (!editors.Contains(tool, StringComparer.Ordinal))
                {
                    editors.Add(tool);
                }
            }
        }
        if (editors.Count == 0)
        {
            return;
        }

        var sourceTools =
            string.Join(" > ", [verifierTool, ..editors, verifierTool]);
        var fingerprint = HashLearningKey(
            "verified-recovery",
            project,
            verifierTool,
            string.Join(",", editors));
        var title =
            $"Verified recovery: {ShortToolName(verifierTool)} after source edit";
        var content =
            $"Talvora observed a failed {verifierTool}, then successful source editing with {string.Join(", ", editors)}, followed by a successful {verifierTool}. This lesson contains only tool identities and verification state; no raw arguments or outputs were stored.";

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await using (var link = connection.CreateCommand())
        {
            link.Transaction = (SqliteTransaction)transaction;
            link.CommandText =
                """
                UPDATE memory_learning_observations
                SET recovery_linked = 1
                WHERE id = $id;
                """;
            Add(link, "$id", failureId);
            await link.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = (SqliteTransaction)transaction;
            upsert.CommandText =
                """
                INSERT INTO memory_learning_patterns(
                    fingerprint, project, pattern_kind,
                    title, content, source_tools,
                    occurrence_count, confidence, importance,
                    status, promoted_memory_id,
                    first_seen_utc, last_seen_utc)
                VALUES(
                    $fingerprint, $project, 'verified-recovery',
                    $title, $content, $sourceTools,
                    1, 0.95, 0.80,
                    'pending', NULL,
                    $seenUtc, $seenUtc)
                ON CONFLICT(fingerprint) DO UPDATE SET
                    occurrence_count = occurrence_count + 1,
                    confidence = MAX(confidence, excluded.confidence),
                    importance = MAX(importance, excluded.importance),
                    source_tools = excluded.source_tools,
                    title = excluded.title,
                    content = excluded.content,
                    last_seen_utc = excluded.last_seen_utc;
                """;
            Add(upsert, "$fingerprint", fingerprint);
            Add(upsert, "$project", project);
            Add(upsert, "$title", title);
            Add(upsert, "$content", content);
            Add(upsert, "$sourceTools", sourceTools);
            Add(upsert, "$seenUtc", Format(successAt));
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        var pattern =
            await GetLearningPatternAsync(
                fingerprint,
                cancellationToken);
        if (pattern is not null &&
            pattern.Status == "pending" &&
            pattern.OccurrenceCount >= 2 &&
            pattern.Confidence >= 0.90 &&
            pattern.Importance >= 0.75 &&
            !await IsLearningSuppressedAsync(
                project,
                verifierTool,
                cancellationToken))
        {
            await PromotePatternCoreAsync(
                pattern,
                cancellationToken);
        }
    }

    private async Task<TalvoraMemoryItem> PromotePatternCoreAsync(
        TalvoraLearningPattern pattern,
        CancellationToken cancellationToken)
    {
        if (pattern.Project is null)
        {
            throw new InvalidOperationException(
                "Automatic learning patterns require a project boundary.");
        }

        var claimKey =
            $"automatic-learning:{pattern.Fingerprint}";
        var existingId =
            await FindActiveMemoryByClaimKeyAsync(
                pattern.Project,
                claimKey,
                cancellationToken);
        var memory = existingId is null
            ? await RememberAsync(
                "project",
                "lesson",
                pattern.Title,
                pattern.Content,
                pattern.Project,
                null,
                pattern.Importance,
                pattern.Confidence,
                "verified",
                "automatic-learning",
                null,
                "durable",
                claimKey,
                cancellationToken)
            : (await GetAsync(existingId, cancellationToken))!;

        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE memory_learning_patterns
            SET status = 'promoted',
                promoted_memory_id = $memoryId,
                last_seen_utc = $lastSeenUtc
            WHERE fingerprint = $fingerprint;
            """;
        Add(command, "$memoryId", memory.Id);
        Add(command, "$lastSeenUtc", Format(DateTimeOffset.UtcNow));
        Add(command, "$fingerprint", pattern.Fingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return memory;
    }

    private async Task<string?> FindActiveMemoryByClaimKeyAsync(
        string project,
        string claimKey,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id
            FROM memory_items
            WHERE project = $project
              AND claim_key = $claimKey
              AND superseded_by IS NULL
              AND (expires_utc IS NULL OR expires_utc > $now)
            LIMIT 1;
            """;
        Add(command, "$project", project);
        Add(command, "$claimKey", claimKey);
        Add(command, "$now", Format(DateTimeOffset.UtcNow));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text && text.Length > 0
            ? text
            : null;
    }

    private async Task<bool> IsLearningSuppressedAsync(
        string project,
        string toolName,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM memory_learning_suppressions
            WHERE (project IS NULL OR project = $project)
              AND (tool_name IS NULL OR tool_name = $toolName);
            """;
        Add(command, "$project", project);
        Add(command, "$toolName", toolName);
        var count = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        return count > 0;
    }

    private async Task<TalvoraLearningPattern?> GetLearningPatternAsync(
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT fingerprint, project, pattern_kind,
                   title, content, source_tools,
                   occurrence_count, confidence, importance,
                   status, promoted_memory_id,
                   first_seen_utc, last_seen_utc
            FROM memory_learning_patterns
            WHERE fingerprint = $fingerprint;
            """;
        Add(command, "$fingerprint", fingerprint);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadLearningPattern(reader)
            : null;
    }

    private static TalvoraLearningPattern ReadLearningPattern(
        SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetDouble(7),
            reader.GetDouble(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            Parse(reader.GetString(11)),
            Parse(reader.GetString(12)));

    private static string HashLearningKey(params string[] parts)
    {
        var bytes = Encoding.UTF8.GetBytes(
            string.Join('\u001F', parts));
        return Convert.ToHexString(
                SHA256.HashData(bytes))
            .ToLowerInvariant();
    }

    private static string ShortToolName(string toolName) =>
        toolName.StartsWith("talvora_", StringComparison.Ordinal)
            ? toolName["talvora_".Length..]
            : toolName;

    private static bool LooksLikeSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var text = value.Trim();
        var lower = text.ToLowerInvariant();
        return lower.Contains("-----begin private key-----", StringComparison.Ordinal) ||
               lower.Contains("password=", StringComparison.Ordinal) ||
               lower.Contains("api_key=", StringComparison.Ordinal) ||
               lower.Contains("apikey=", StringComparison.Ordinal) ||
               lower.Contains("access_token=", StringComparison.Ordinal) ||
               lower.Contains("secret=", StringComparison.Ordinal) ||
               text.StartsWith("sk-", StringComparison.Ordinal) ||
               text.StartsWith("ghp_", StringComparison.Ordinal) ||
               text.StartsWith("github_pat_", StringComparison.Ordinal) ||
               text.StartsWith("AKIA", StringComparison.Ordinal);
    }
}

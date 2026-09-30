using System.Globalization;
using System.Text.Json;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class DataQualityProfileProcessor(
    AppDbContext db,
    IQualityProfileCancellationRegistry cancellations,
    IAuditService audit,
    ILogger<DataQualityProfileProcessor> logger) : IDataQualityProfileProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(Guid profileRunId, CancellationToken hostToken)
    {
        var run = await db.DataQualityProfileRuns.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == profileRunId, hostToken);
        if (run is null || run.Status == QualityProfileStatuses.Cancelled) return;

        var claimed = await db.DataQualityProfileRuns.IgnoreQueryFilters()
            .Where(x => x.Id == profileRunId && x.Status == QualityProfileStatuses.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, QualityProfileStatuses.Processing)
                .SetProperty(x => x.StartedAtUtc, DateTime.UtcNow), hostToken);
        if (claimed != 1) return;

        var token = cancellations.Register(profileRunId, hostToken);
        try
        {
            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await CalculateAndPersistAsync(profileRunId, token);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex))
                {
                    logger.LogWarning(ex, "Transient quality profiling failure for {ProfileRunId}; retry {Attempt}.",
                        profileRunId, attempt);
                    db.ChangeTracker.Clear();
                    await db.DataQualityProfileRuns.IgnoreQueryFilters().Where(x => x.Id == profileRunId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RetryCount, attempt), CancellationToken.None);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            await MarkCancelledAsync(profileRunId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Quality profile {ProfileRunId} failed.", profileRunId);
            await MarkFailedAsync(profileRunId, ex);
        }
        finally
        {
            cancellations.Unregister(profileRunId);
        }
    }

    private async Task CalculateAndPersistAsync(Guid profileRunId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var run = await db.DataQualityProfileRuns.IgnoreQueryFilters().FirstAsync(x => x.Id == profileRunId, ct);
        if (run.CancellationRequested) throw new OperationCanceledException(ct);

        var columns = await db.DatasetColumns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.DatasetId == run.DatasetId && x.WorkspaceId == run.WorkspaceId)
            .OrderBy(x => x.Ordinal).ToListAsync(ct);
        var records = await db.DatasetRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.DatasetId == run.DatasetId && x.WorkspaceId == run.WorkspaceId)
            .Include(x => x.Values).ToListAsync(ct);
        var threshold = await db.DataQualityThresholds.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.DatasetId == run.DatasetId && x.WorkspaceId == run.WorkspaceId, ct);
        var configuration = await LoadConfigurationAsync(run, ct);
        var mappings = configuration?.Mappings.GroupBy(x => x.TargetColumnId)
            .ToDictionary(x => x.Key, x => x.Last()) ?? [];

        var existingMetrics = await db.DataQualityColumnMetrics.IgnoreQueryFilters()
            .Where(x => x.ProfileRunId == profileRunId).ToListAsync(ct);
        var existingIssues = await db.DataQualityIssues.IgnoreQueryFilters()
            .Where(x => x.ProfileRunId == profileRunId).ToListAsync(ct);
        db.DataQualityColumnMetrics.RemoveRange(existingMetrics);
        db.DataQualityIssues.RemoveRange(existingIssues);

        var issues = new List<DataQualityIssue>();
        var affectedRecords = new HashSet<Guid>();
        var inconsistentRecords = new HashSet<Guid>();
        var invalidRecords = new HashSet<Guid>();
        var requiredApplicableValues = 0;
        var requiredCompleteValues = 0;
        var metrics = new List<DataQualityColumnMetric>();
        var keyColumns = columns.Where(x => x.IsKey).ToList();

        foreach (var column in columns)
        {
            ct.ThrowIfCancellationRequested();
            mappings.TryGetValue(column.Id, out var mapping);
            var effectiveRequired = QualityRuleEvaluator.IsRequired(column, mapping);
            var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
            var nonNull = 0;
            var nullCount = 0;
            var invalidCount = 0;
            var completeValues = 0;
            var validValues = 0;
            var consistentValues = 0;
            var consistencyApplicable = 0;
            var numericValues = new List<decimal>();
            var dateValues = new List<DateTime>();

            foreach (var record in records)
            {
                var recordValue = record.Values.FirstOrDefault(x => x.DatasetColumnId == column.Id);
                var raw = recordValue?.RawValue;
                var missing = string.IsNullOrWhiteSpace(raw);
                if (missing) nullCount++; else nonNull++;

                var failures = QualityRuleEvaluator.Evaluate(raw, column, mapping);
                if (failures.Count == 0) validValues++;
                else
                {
                    invalidCount++;
                    invalidRecords.Add(record.Id);
                    affectedRecords.Add(record.Id);
                    foreach (var failure in failures)
                    {
                        if (failure.AffectsConsistency) inconsistentRecords.Add(record.Id);
                        issues.Add(CreateIssue(run, record.Id, column, failure, raw));
                    }
                }

                if (effectiveRequired)
                {
                    requiredApplicableValues++;
                    if (!missing && failures.Count == 0)
                    {
                        requiredCompleteValues++;
                        completeValues++;
                    }
                }

                if (!missing)
                {
                    consistencyApplicable++;
                    if (failures.All(x => !x.AffectsConsistency)) consistentValues++;
                    var canonical = CanonicalValue(raw!, column.DataType);
                    frequencies[canonical] = frequencies.GetValueOrDefault(canonical) + 1;
                    CollectTypedValue(recordValue, column.DataType, numericValues, dateValues);
                }
            }

            var distinct = frequencies.Count;
            var unique = frequencies.Count(x => x.Value == 1);
            var duplicates = QualityCalculations.DuplicateCount(nonNull, distinct);
            var completeness = effectiveRequired ? QualityCalculations.Percentage(completeValues, records.Count) : null;
            var validity = QualityCalculations.Percentage(validValues, records.Count);
            var uniqueness = column.IsKey ? QualityCalculations.Percentage(distinct, nonNull) : null;
            var consistency = QualityCalculations.Percentage(consistentValues, consistencyApplicable);
            var qualityScore = QualityCalculations.WeightedScore(
                (completeness, QualityDefaults.CompletenessWeight),
                (validity, QualityDefaults.ValidityWeight),
                (uniqueness, QualityDefaults.UniquenessWeight),
                (consistency, QualityDefaults.ConsistencyWeight));

            var (minimum, maximum, average) = Statistics(column.DataType, numericValues, dateValues);
            metrics.Add(new DataQualityColumnMetric
            {
                ProfileRunId = run.Id, DatasetColumnId = column.Id, WorkspaceId = run.WorkspaceId,
                ColumnName = column.Name, DataType = column.DataType, IsRequired = effectiveRequired, IsKey = column.IsKey,
                TotalRecords = records.Count, NonNullRecords = nonNull, NullCount = nullCount,
                NullPercentage = QualityCalculations.Percentage(nullCount, records.Count), DistinctCount = distinct,
                UniqueCount = unique, DuplicateCount = duplicates, InvalidCount = invalidCount,
                CompletenessScore = completeness, ValidityScore = validity, UniquenessScore = uniqueness,
                ConsistencyScore = consistency, QualityScore = qualityScore, MinimumValue = minimum,
                MaximumValue = maximum, AverageValue = average
            });
        }

        decimal? datasetUniqueness = null;
        if (keyColumns.Count > 0 && records.Count > 0)
        {
            var groups = records.GroupBy(x => x.KeyHash, StringComparer.Ordinal).ToList();
            datasetUniqueness = QualityCalculations.Percentage(groups.Count, records.Count);
            foreach (var duplicate in groups.Where(x => x.Count() > 1).SelectMany(x => x.Skip(1)))
            {
                affectedRecords.Add(duplicate.Id);
                issues.Add(new DataQualityIssue
                {
                    ProfileRunId = run.Id, DatasetId = run.DatasetId, WorkspaceId = run.WorkspaceId,
                    ImportId = run.ImportId, DatasetRecordId = duplicate.Id, IssueType = QualityIssueTypes.DuplicateValue,
                    ValidationRule = ValidationRuleTypes.Duplicate, Description = "Duplicate configured dataset key detected.",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        var completenessScore = QualityCalculations.Percentage(requiredCompleteValues, requiredApplicableValues);
        var validityScore = QualityCalculations.Percentage(records.Count - invalidRecords.Count, records.Count);
        var consistencyScore = QualityCalculations.Percentage(records.Count - inconsistentRecords.Count, records.Count);
        var errorRate = QualityCalculations.Percentage(affectedRecords.Count, records.Count);
        var overall = QualityCalculations.OverallScore(completenessScore, validityScore, datasetUniqueness, consistencyScore);
        var minimumCompleteness = threshold?.MinimumCompleteness ?? QualityDefaults.CompletenessThreshold;
        var minimumValidity = threshold?.MinimumValidity ?? QualityDefaults.ValidityThreshold;
        var minimumUniqueness = threshold?.MinimumUniqueness ?? QualityDefaults.UniquenessThreshold;
        var minimumConsistency = threshold?.MinimumConsistency ?? QualityDefaults.ConsistencyThreshold;
        var minimumOverall = threshold?.MinimumOverallScore ?? QualityDefaults.OverallThreshold;

        run.TotalRecords = records.Count;
        run.CompletenessScore = completenessScore;
        run.ValidityScore = validityScore;
        run.UniquenessScore = datasetUniqueness;
        run.ConsistencyScore = consistencyScore;
        run.ErrorRate = errorRate;
        run.OverallQualityScore = overall;
        run.AppliedCompletenessThreshold = minimumCompleteness;
        run.AppliedValidityThreshold = minimumValidity;
        run.AppliedUniquenessThreshold = minimumUniqueness;
        run.AppliedConsistencyThreshold = minimumConsistency;
        run.AppliedOverallThreshold = minimumOverall;
        run.ThresholdStatus = QualityCalculations.EvaluateThresholds(records.Count, completenessScore, validityScore,
            datasetUniqueness, consistencyScore, overall, minimumCompleteness, minimumValidity, minimumUniqueness,
            minimumConsistency, minimumOverall);
        run.Status = QualityProfileStatuses.Completed;
        run.CompletedAtUtc = DateTime.UtcNow;
        run.FailureMessage = null;
        db.DataQualityColumnMetrics.AddRange(metrics);
        db.DataQualityIssues.AddRange(issues);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync("Profiling Completed", "DataQualityProfileRun", run.Id.ToString(),
            $"DatasetId={run.DatasetId}; ImportId={run.ImportId}; Total={run.TotalRecords}; " +
            $"Overall={run.OverallQualityScore}; ThresholdStatus={run.ThresholdStatus}",
            userId: run.RequestedByUserId, workspaceId: run.WorkspaceId, cancellationToken: ct);
    }

    private async Task<SaveTransformationConfigurationRequest?> LoadConfigurationAsync(
        DataQualityProfileRun run, CancellationToken ct)
    {
        Guid? configurationId = null;
        if (run.ImportId.HasValue)
            configurationId = await db.DataImports.IgnoreQueryFilters().Where(x => x.Id == run.ImportId)
                .Select(x => x.TransformationConfigurationId).FirstOrDefaultAsync(ct);
        var json = configurationId.HasValue
            ? await db.DatasetTransformationConfigurations.IgnoreQueryFilters().Where(x => x.Id == configurationId)
                .Select(x => x.ConfigurationJson).FirstOrDefaultAsync(ct)
            : await db.DatasetTransformationConfigurations.IgnoreQueryFilters()
                .Where(x => x.DatasetId == run.DatasetId && x.WorkspaceId == run.WorkspaceId && x.IsActive)
                .Select(x => x.ConfigurationJson).FirstOrDefaultAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<SaveTransformationConfigurationRequest>(json, JsonOptions);
    }

    private static DataQualityIssue CreateIssue(DataQualityProfileRun run, Guid recordId, DatasetColumn column,
        QualityRuleFailure failure, string? value) => new()
    {
        ProfileRunId = run.Id, DatasetId = run.DatasetId, WorkspaceId = run.WorkspaceId, ImportId = run.ImportId,
        DatasetRecordId = recordId, DatasetColumnId = column.Id, ColumnName = column.Name,
        IssueType = failure.IssueType, ValidationRule = failure.Rule, Description = failure.Message,
        InvalidValue = value?.Length > 1000 ? value[..1000] : value, CreatedAtUtc = DateTime.UtcNow
    };

    private static string CanonicalValue(string value, string dataType) => dataType switch
    {
        DatasetColumnTypes.Integer when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) =>
            integer.ToString(CultureInfo.InvariantCulture),
        DatasetColumnTypes.Decimal when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) =>
            number.ToString(CultureInfo.InvariantCulture),
        DatasetColumnTypes.Boolean when value is "1" or "0" => value == "1" ? "TRUE" : "FALSE",
        DatasetColumnTypes.Boolean when bool.TryParse(value, out var boolean) => boolean ? "TRUE" : "FALSE",
        DatasetColumnTypes.DateTime when DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date) =>
            date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        _ => value.Trim().ToUpperInvariant()
    };

    private static void CollectTypedValue(DatasetRecordValue? value, string dataType,
        ICollection<decimal> numericValues, ICollection<DateTime> dateValues)
    {
        if (value is null) return;
        if (dataType == DatasetColumnTypes.Integer && value.IntegerValue.HasValue)
            numericValues.Add(value.IntegerValue.Value);
        else if (dataType == DatasetColumnTypes.Decimal && value.DecimalValue.HasValue)
            numericValues.Add(value.DecimalValue.Value);
        else if (dataType == DatasetColumnTypes.DateTime && value.DateTimeValue.HasValue)
            dateValues.Add(value.DateTimeValue.Value);
    }

    private static (string? Minimum, string? Maximum, decimal? Average) Statistics(
        string dataType, IReadOnlyCollection<decimal> numericValues, IReadOnlyCollection<DateTime> dateValues)
    {
        if (dataType is DatasetColumnTypes.Integer or DatasetColumnTypes.Decimal && numericValues.Count > 0)
            return (numericValues.Min().ToString(CultureInfo.InvariantCulture),
                numericValues.Max().ToString(CultureInfo.InvariantCulture),
                Math.Round(numericValues.Average(), 10, MidpointRounding.AwayFromZero));
        if (dataType == DatasetColumnTypes.DateTime && dateValues.Count > 0)
            return (dateValues.Min().ToString("O", CultureInfo.InvariantCulture),
                dateValues.Max().ToString("O", CultureInfo.InvariantCulture), null);
        return (null, null, null);
    }

    private static bool IsTransient(Exception ex) =>
        ex is TimeoutException or DbUpdateException || ex.InnerException is TimeoutException;

    private async Task MarkCancelledAsync(Guid profileRunId)
    {
        db.ChangeTracker.Clear();
        await db.DataQualityProfileRuns.IgnoreQueryFilters().Where(x => x.Id == profileRunId &&
            x.Status != QualityProfileStatuses.Completed && x.Status != QualityProfileStatuses.Failed)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, QualityProfileStatuses.Cancelled)
                .SetProperty(x => x.CancellationRequested, true)
                .SetProperty(x => x.CompletedAtUtc, DateTime.UtcNow), CancellationToken.None);
        var run = await db.DataQualityProfileRuns.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == profileRunId, CancellationToken.None);
        if (run is not null)
            await audit.WriteAsync("Profiling Cancelled", "DataQualityProfileRun", profileRunId.ToString(),
                $"DatasetId={run.DatasetId}", userId: run.RequestedByUserId, workspaceId: run.WorkspaceId,
                cancellationToken: CancellationToken.None);
    }

    private async Task MarkFailedAsync(Guid profileRunId, Exception ex)
    {
        db.ChangeTracker.Clear();
        var message = ex.GetBaseException().Message;
        if (message.Length > 4000) message = message[..4000];
        await db.DataQualityProfileRuns.IgnoreQueryFilters().Where(x => x.Id == profileRunId &&
            x.Status != QualityProfileStatuses.Completed && x.Status != QualityProfileStatuses.Cancelled)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, QualityProfileStatuses.Failed)
                .SetProperty(x => x.FailureMessage, message)
                .SetProperty(x => x.CompletedAtUtc, DateTime.UtcNow), CancellationToken.None);
        var run = await db.DataQualityProfileRuns.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == profileRunId, CancellationToken.None);
        if (run is not null)
            await audit.WriteAsync("Profiling Failed", "DataQualityProfileRun", profileRunId.ToString(), message,
                userId: run.RequestedByUserId, workspaceId: run.WorkspaceId, cancellationToken: CancellationToken.None);
    }
}

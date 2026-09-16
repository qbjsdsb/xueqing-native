namespace Xueqing.Windows.Core.Sync;

public enum OutboxQueueStatus
{
    Pending,
    Retry,
    InFlight,
    Acknowledged,
    DeadLetter,
}

public enum OutboxEnqueueDisposition
{
    Inserted,
    AlreadyPresent,
}

public sealed record OutboxCommandIntent(
    Guid OperationId,
    string CommandType,
    string? AggregateId,
    string ScopeKey,
    long? ExpectedVersion,
    string? FreshnessToken,
    string PayloadJson,
    DateTimeOffset CreatedAtClientUtc);

public sealed record OutboxRecord(
    long LocalSequence,
    OutboxCommandIntent Intent,
    int AttemptCount,
    OutboxQueueStatus QueueStatus,
    string? LastErrorClass,
    DateTimeOffset? NextAttemptAtUtc,
    Guid? LeaseId,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    string? ServerReceiptJson);

public sealed record OutboxEnqueueResult(
    OutboxEnqueueDisposition Disposition,
    OutboxRecord Record);

public interface IOutboxStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<OutboxEnqueueResult> EnqueueAsync(
        OutboxCommandIntent intent,
        CancellationToken cancellationToken = default);

    Task<OutboxRecord?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<long> CountAsync(CancellationToken cancellationToken = default);

    Task<OutboxRecord?> TryClaimNextReadyAsync(
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<OutboxRecord> ScheduleRetryAsync(
        Guid operationId,
        Guid leaseId,
        string errorClass,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken = default);

    Task<OutboxRecord> MarkAcknowledgedAsync(
        Guid operationId,
        Guid leaseId,
        DateTimeOffset acknowledgedAtUtc,
        string? serverReceiptJson,
        CancellationToken cancellationToken = default);

    Task<OutboxRecord> MoveToDeadLetterAsync(
        Guid operationId,
        Guid leaseId,
        string errorClass,
        CancellationToken cancellationToken = default);
}

public sealed class OutboxOperationIdCollisionException(Guid operationId)
    : InvalidOperationException($"Operation id '{operationId:D}' is already bound to a different durable intent.");

public sealed class OutboxLeaseLostException(Guid operationId, Guid leaseId)
    : InvalidOperationException($"Lease '{leaseId:D}' no longer owns operation '{operationId:D}'.");

public sealed class UnsupportedLocalSchemaVersionException(long version)
    : InvalidOperationException($"Unsupported local SQLite schema version: {version}.");

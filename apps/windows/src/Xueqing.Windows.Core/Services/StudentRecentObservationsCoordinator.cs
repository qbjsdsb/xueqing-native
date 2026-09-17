using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public enum StudentRecentObservationsViewStatus
{
    Idle,
    Loading,
    Empty,
    Data,
    AuthenticationRequired,
    AccessDenied,
    TransientFailure,
    ProtocolFailure,
}

public sealed record StudentRecentObservationsViewState(
    long Generation,
    StudentObservationScope? Scope,
    StudentRecentObservationsViewStatus Status,
    StudentRecentObservationsSnapshot? Snapshot,
    string? FailureCode)
{
    public static StudentRecentObservationsViewState Initial { get; } =
        new(0, null, StudentRecentObservationsViewStatus.Idle, null, null);
}

public sealed class StudentRecentObservationsCoordinator
{
    private readonly IStudentRecentObservationsReader _reader;
    private readonly object _gate = new();
    private long _generation;
    private StudentRecentObservationsViewState _current = StudentRecentObservationsViewState.Initial;

    public StudentRecentObservationsCoordinator(IStudentRecentObservationsReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public event Action<StudentRecentObservationsViewState>? StateChanged;

    public StudentRecentObservationsViewState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public async Task<StudentRecentObservationsViewState> LoadAsync(
        StudentObservationScope scope,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var loading = Begin(scope);
        Publish(loading);

        var result = await _reader.ReadAsync(scope, expectedActorAppUserId, cancellationToken).ConfigureAwait(false);
        var completed = ToViewState(loading.Generation, scope, result);

        lock (_gate)
        {
            if (loading.Generation != _generation)
            {
                return _current;
            }

            _current = completed;
        }

        Publish(completed);
        return completed;
    }

    public StudentRecentObservationsViewState Reset()
    {
        StudentRecentObservationsViewState reset;
        lock (_gate)
        {
            _generation++;
            reset = new StudentRecentObservationsViewState(
                _generation,
                null,
                StudentRecentObservationsViewStatus.Idle,
                null,
                null);
            _current = reset;
        }

        Publish(reset);
        return reset;
    }

    private StudentRecentObservationsViewState Begin(StudentObservationScope scope)
    {
        lock (_gate)
        {
            _generation++;
            _current = new StudentRecentObservationsViewState(
                _generation,
                scope,
                StudentRecentObservationsViewStatus.Loading,
                null,
                null);
            return _current;
        }
    }

    private static StudentRecentObservationsViewState ToViewState(
        long generation,
        StudentObservationScope scope,
        StudentRecentObservationsReadResult result)
    {
        if (result.IsSuccess && result.Snapshot is not null)
        {
            var successStatus = result.Snapshot.Observations.Count == 0
                ? StudentRecentObservationsViewStatus.Empty
                : StudentRecentObservationsViewStatus.Data;
            return new StudentRecentObservationsViewState(
                generation,
                scope,
                successStatus,
                result.Snapshot,
                null);
        }

        var failure = result.Failure ?? new StudentRecentObservationsFailure(
            StudentRecentObservationsFailureKind.InvalidResponse,
            "XQ_CLIENT_RESULT_INVALID");
        var failureStatus = failure.Kind switch
        {
            StudentRecentObservationsFailureKind.AuthenticationRequired => StudentRecentObservationsViewStatus.AuthenticationRequired,
            StudentRecentObservationsFailureKind.AccessDenied => StudentRecentObservationsViewStatus.AccessDenied,
            StudentRecentObservationsFailureKind.Transient => StudentRecentObservationsViewStatus.TransientFailure,
            _ => StudentRecentObservationsViewStatus.ProtocolFailure,
        };
        return new StudentRecentObservationsViewState(
            generation,
            scope,
            failureStatus,
            null,
            failure.Code);
    }

    private void Publish(StudentRecentObservationsViewState state) => StateChanged?.Invoke(state);
}

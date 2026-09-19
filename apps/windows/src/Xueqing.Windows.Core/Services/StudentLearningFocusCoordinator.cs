using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public enum StudentLearningFocusViewStatus
{
    Idle,
    Loading,
    Empty,
    Data,
    AuthenticationRequired,
    AccessDenied,
    ServerInvariant,
    TransientFailure,
    ProtocolFailure,
}

public sealed record StudentLearningFocusViewState(
    long Generation,
    StudentLearningScope? Scope,
    StudentLearningFocusViewStatus Status,
    StudentLearningFocusSnapshot? Snapshot,
    string? FailureCode)
{
    public static StudentLearningFocusViewState Initial { get; } =
        new(0, null, StudentLearningFocusViewStatus.Idle, null, null);
}

public sealed class StudentLearningFocusCoordinator
{
    private readonly IStudentLearningFocusReader _reader;
    private readonly object _gate = new();
    private long _generation;
    private StudentLearningFocusViewState _current = StudentLearningFocusViewState.Initial;

    public StudentLearningFocusCoordinator(IStudentLearningFocusReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public event Action<StudentLearningFocusViewState>? StateChanged;

    public StudentLearningFocusViewState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public async Task<StudentLearningFocusViewState> LoadAsync(
        StudentLearningScope scope,
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

    public StudentLearningFocusViewState Reset()
    {
        StudentLearningFocusViewState reset;
        lock (_gate)
        {
            _generation++;
            reset = new StudentLearningFocusViewState(
                _generation,
                null,
                StudentLearningFocusViewStatus.Idle,
                null,
                null);
            _current = reset;
        }

        Publish(reset);
        return reset;
    }

    private StudentLearningFocusViewState Begin(StudentLearningScope scope)
    {
        lock (_gate)
        {
            _generation++;
            _current = new StudentLearningFocusViewState(
                _generation,
                scope,
                StudentLearningFocusViewStatus.Loading,
                null,
                null);
            return _current;
        }
    }

    private static StudentLearningFocusViewState ToViewState(
        long generation,
        StudentLearningScope scope,
        StudentLearningFocusReadResult result)
    {
        if (result.IsSuccess && result.Snapshot is not null)
        {
            return new StudentLearningFocusViewState(
                generation,
                scope,
                result.Snapshot.Cases.Count == 0
                    ? StudentLearningFocusViewStatus.Empty
                    : StudentLearningFocusViewStatus.Data,
                result.Snapshot,
                null);
        }

        var failure = result.Failure ?? new LearningReadFailure(
            LearningReadFailureKind.InvalidResponse,
            "XQ_CLIENT_RESULT_INVALID");

        var status = failure.Kind switch
        {
            LearningReadFailureKind.AuthenticationRequired => StudentLearningFocusViewStatus.AuthenticationRequired,
            LearningReadFailureKind.AccessDenied => StudentLearningFocusViewStatus.AccessDenied,
            LearningReadFailureKind.ServerInvariant => StudentLearningFocusViewStatus.ServerInvariant,
            LearningReadFailureKind.Transient => StudentLearningFocusViewStatus.TransientFailure,
            _ => StudentLearningFocusViewStatus.ProtocolFailure,
        };

        return new StudentLearningFocusViewState(
            generation,
            scope,
            status,
            null,
            failure.Code);
    }

    private void Publish(StudentLearningFocusViewState state) => StateChanged?.Invoke(state);
}

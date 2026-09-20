using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public enum StudentLearningCasesViewStatus
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

public sealed record StudentLearningCasesViewState(
    long Generation,
    StudentLearningScope? Scope,
    StudentLearningCasesViewStatus Status,
    StudentLearningCasesSnapshot? Snapshot,
    string? FailureCode)
{
    public static StudentLearningCasesViewState Initial { get; } =
        new(0, null, StudentLearningCasesViewStatus.Idle, null, null);
}

public sealed class StudentLearningCasesCoordinator
{
    private readonly IStudentLearningCasesReader _reader;
    private readonly object _gate = new();
    private long _generation;
    private StudentLearningCasesViewState _current = StudentLearningCasesViewState.Initial;

    public StudentLearningCasesCoordinator(IStudentLearningCasesReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public event Action<StudentLearningCasesViewState>? StateChanged;

    public StudentLearningCasesViewState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public async Task<StudentLearningCasesViewState> LoadAsync(
        StudentLearningScope scope,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var loading = Begin(scope);
        Publish(loading);

        var result = await _reader.ReadAsync(
            scope,
            expectedActorAppUserId,
            cancellationToken).ConfigureAwait(false);
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

    public StudentLearningCasesViewState Reset()
    {
        StudentLearningCasesViewState reset;
        lock (_gate)
        {
            _generation++;
            reset = new StudentLearningCasesViewState(
                _generation,
                null,
                StudentLearningCasesViewStatus.Idle,
                null,
                null);
            _current = reset;
        }

        Publish(reset);
        return reset;
    }

    private StudentLearningCasesViewState Begin(StudentLearningScope scope)
    {
        lock (_gate)
        {
            _generation++;
            _current = new StudentLearningCasesViewState(
                _generation,
                scope,
                StudentLearningCasesViewStatus.Loading,
                null,
                null);
            return _current;
        }
    }

    private static StudentLearningCasesViewState ToViewState(
        long generation,
        StudentLearningScope scope,
        StudentLearningCasesReadResult result)
    {
        if (result.IsSuccess && result.Snapshot is not null)
        {
            return new StudentLearningCasesViewState(
                generation,
                scope,
                result.Snapshot.Cases.Count == 0
                    ? StudentLearningCasesViewStatus.Empty
                    : StudentLearningCasesViewStatus.Data,
                result.Snapshot,
                null);
        }

        var failure = result.Failure ?? new LearningReadFailure(
            LearningReadFailureKind.InvalidResponse,
            "XQ_CLIENT_RESULT_INVALID");

        var status = failure.Kind switch
        {
            LearningReadFailureKind.AuthenticationRequired => StudentLearningCasesViewStatus.AuthenticationRequired,
            LearningReadFailureKind.AccessDenied => StudentLearningCasesViewStatus.AccessDenied,
            LearningReadFailureKind.ServerInvariant => StudentLearningCasesViewStatus.ServerInvariant,
            LearningReadFailureKind.Transient => StudentLearningCasesViewStatus.TransientFailure,
            _ => StudentLearningCasesViewStatus.ProtocolFailure,
        };

        return new StudentLearningCasesViewState(
            generation,
            scope,
            status,
            null,
            failure.Code);
    }

    private void Publish(StudentLearningCasesViewState state) => StateChanged?.Invoke(state);
}

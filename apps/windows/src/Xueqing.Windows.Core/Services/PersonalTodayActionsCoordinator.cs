using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public enum PersonalTodayActionsViewStatus
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

public sealed record PersonalTodayActionsViewState(
    long Generation,
    PersonalTodayActionsViewStatus Status,
    PersonalTodayActionsSnapshot? Snapshot,
    string? FailureCode)
{
    public static PersonalTodayActionsViewState Initial { get; } =
        new(0, PersonalTodayActionsViewStatus.Idle, null, null);
}

public sealed class PersonalTodayActionsCoordinator
{
    private readonly IPersonalTodayActionsReader _reader;
    private readonly object _gate = new();
    private long _generation;
    private PersonalTodayActionsViewState _current = PersonalTodayActionsViewState.Initial;

    public PersonalTodayActionsCoordinator(IPersonalTodayActionsReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public event Action<PersonalTodayActionsViewState>? StateChanged;

    public PersonalTodayActionsViewState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public async Task<PersonalTodayActionsViewState> LoadAsync(
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        var loading = Begin();
        Publish(loading);

        var result = await _reader.ReadAsync(expectedActorAppUserId, cancellationToken).ConfigureAwait(false);
        var completed = ToViewState(loading.Generation, result);

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

    public PersonalTodayActionsViewState Reset()
    {
        PersonalTodayActionsViewState reset;
        lock (_gate)
        {
            _generation++;
            reset = new PersonalTodayActionsViewState(
                _generation,
                PersonalTodayActionsViewStatus.Idle,
                null,
                null);
            _current = reset;
        }

        Publish(reset);
        return reset;
    }

    private PersonalTodayActionsViewState Begin()
    {
        lock (_gate)
        {
            _generation++;
            _current = new PersonalTodayActionsViewState(
                _generation,
                PersonalTodayActionsViewStatus.Loading,
                null,
                null);
            return _current;
        }
    }

    private static PersonalTodayActionsViewState ToViewState(
        long generation,
        PersonalTodayActionsReadResult result)
    {
        if (result.IsSuccess && result.Snapshot is not null)
        {
            return new PersonalTodayActionsViewState(
                generation,
                result.Snapshot.Actions.Count == 0
                    ? PersonalTodayActionsViewStatus.Empty
                    : PersonalTodayActionsViewStatus.Data,
                result.Snapshot,
                null);
        }

        var failure = result.Failure ?? new LearningReadFailure(
            LearningReadFailureKind.InvalidResponse,
            "XQ_CLIENT_RESULT_INVALID");

        var status = failure.Kind switch
        {
            LearningReadFailureKind.AuthenticationRequired => PersonalTodayActionsViewStatus.AuthenticationRequired,
            LearningReadFailureKind.AccessDenied => PersonalTodayActionsViewStatus.AccessDenied,
            LearningReadFailureKind.ServerInvariant => PersonalTodayActionsViewStatus.ServerInvariant,
            LearningReadFailureKind.Transient => PersonalTodayActionsViewStatus.TransientFailure,
            _ => PersonalTodayActionsViewStatus.ProtocolFailure,
        };

        return new PersonalTodayActionsViewState(
            generation,
            status,
            null,
            failure.Code);
    }

    private void Publish(PersonalTodayActionsViewState state) => StateChanged?.Invoke(state);
}

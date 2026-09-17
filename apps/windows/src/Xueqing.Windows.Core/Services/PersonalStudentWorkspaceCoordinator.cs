using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public enum PersonalStudentWorkspaceStatus
{
    Idle,
    Loading,
    Ready,
    AuthenticationRequired,
    AccessDenied,
    TransientFailure,
    ProtocolFailure,
}

public sealed record PersonalStudentWorkspaceState(
    long Generation,
    PersonalStudentWorkspaceStatus Status,
    PersonalBootstrapSnapshot? Bootstrap,
    string? FailureCode)
{
    public static PersonalStudentWorkspaceState Initial { get; } =
        new(0, PersonalStudentWorkspaceStatus.Idle, null, null);
}

public sealed record PersonalStudentWorkspaceItem(
    Guid OrganizationId,
    Guid StudentId,
    string DisplayName,
    IReadOnlyList<PersonalTeachingContext> TeachingContexts);

public sealed class PersonalStudentWorkspaceCoordinator
{
    private readonly IPersonalBootstrapReader _bootstrapReader;
    private readonly StudentRecentObservationsCoordinator _recentObservations;
    private readonly object _gate = new();
    private long _generation;
    private PersonalStudentWorkspaceState _current = PersonalStudentWorkspaceState.Initial;

    public PersonalStudentWorkspaceCoordinator(
        IPersonalBootstrapReader bootstrapReader,
        StudentRecentObservationsCoordinator recentObservations)
    {
        _bootstrapReader = bootstrapReader ?? throw new ArgumentNullException(nameof(bootstrapReader));
        _recentObservations = recentObservations ?? throw new ArgumentNullException(nameof(recentObservations));
    }

    public event Action<PersonalStudentWorkspaceState>? StateChanged;

    public PersonalStudentWorkspaceState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public StudentRecentObservationsViewState RecentObservations => _recentObservations.Current;

    public IReadOnlyList<PersonalStudentWorkspaceItem> Students
    {
        get
        {
            var snapshot = Current.Bootstrap;
            if (snapshot is null)
            {
                return Array.Empty<PersonalStudentWorkspaceItem>();
            }

            return snapshot.TeachingContexts
                .GroupBy(context => (context.OrganizationId, context.StudentId))
                .OrderBy(group => group.Key.OrganizationId)
                .ThenBy(group => group.First().StudentDisplayName, StringComparer.Ordinal)
                .ThenBy(group => group.Key.StudentId)
                .Select(group => new PersonalStudentWorkspaceItem(
                    group.Key.OrganizationId,
                    group.Key.StudentId,
                    group.First().StudentDisplayName,
                    group.OrderBy(context => context.SubjectKey, StringComparer.Ordinal)
                        .ThenBy(context => context.SubjectProfileId)
                        .ToArray()))
                .ToArray();
        }
    }

    public async Task<PersonalStudentWorkspaceState> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var loading = BeginRefresh();
        Publish(loading);

        var result = await _bootstrapReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var completed = ToState(loading.Generation, result);

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

    public async Task<StudentRecentObservationsViewState> LoadRecentAsync(
        PersonalTeachingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        PersonalBootstrapSnapshot? bootstrap;
        lock (_gate)
        {
            bootstrap = _current.Status == PersonalStudentWorkspaceStatus.Ready
                ? _current.Bootstrap
                : null;
        }

        if (bootstrap is null || !ContainsContext(bootstrap, context))
        {
            return _recentObservations.Reset();
        }

        return await _recentObservations.LoadAsync(
            context.ObservationScope,
            bootstrap.ActorAppUserId,
            cancellationToken).ConfigureAwait(false);
    }

    public PersonalStudentWorkspaceState Reset()
    {
        PersonalStudentWorkspaceState reset;
        lock (_gate)
        {
            _generation++;
            reset = new PersonalStudentWorkspaceState(
                _generation,
                PersonalStudentWorkspaceStatus.Idle,
                null,
                null);
            _current = reset;
        }

        _recentObservations.Reset();
        Publish(reset);
        return reset;
    }

    private PersonalStudentWorkspaceState BeginRefresh()
    {
        PersonalStudentWorkspaceState loading;
        lock (_gate)
        {
            _generation++;
            loading = new PersonalStudentWorkspaceState(
                _generation,
                PersonalStudentWorkspaceStatus.Loading,
                null,
                null);
            _current = loading;
        }

        // Authority is being re-evaluated. Do not keep facts from the previous
        // account/scope visible while a new bootstrap is in flight.
        _recentObservations.Reset();
        return loading;
    }

    private static PersonalStudentWorkspaceState ToState(
        long generation,
        PersonalBootstrapReadResult result)
    {
        if (result.IsSuccess && result.Snapshot is not null)
        {
            return new PersonalStudentWorkspaceState(
                generation,
                PersonalStudentWorkspaceStatus.Ready,
                result.Snapshot,
                null);
        }

        var failure = result.Failure ?? new PersonalBootstrapFailure(
            PersonalBootstrapFailureKind.InvalidResponse,
            "XQ_CLIENT_BOOTSTRAP_RESULT_INVALID");
        var status = failure.Kind switch
        {
            PersonalBootstrapFailureKind.AuthenticationRequired => PersonalStudentWorkspaceStatus.AuthenticationRequired,
            PersonalBootstrapFailureKind.AccessDenied => PersonalStudentWorkspaceStatus.AccessDenied,
            PersonalBootstrapFailureKind.Transient => PersonalStudentWorkspaceStatus.TransientFailure,
            _ => PersonalStudentWorkspaceStatus.ProtocolFailure,
        };
        return new PersonalStudentWorkspaceState(generation, status, null, failure.Code);
    }

    private static bool ContainsContext(PersonalBootstrapSnapshot bootstrap, PersonalTeachingContext candidate) =>
        bootstrap.TeachingContexts.Any(context =>
            context.OrganizationId == candidate.OrganizationId &&
            context.StudentId == candidate.StudentId &&
            context.SubjectProfileId == candidate.SubjectProfileId &&
            context.AssignmentId == candidate.AssignmentId);

    private void Publish(PersonalStudentWorkspaceState state) => StateChanged?.Invoke(state);
}

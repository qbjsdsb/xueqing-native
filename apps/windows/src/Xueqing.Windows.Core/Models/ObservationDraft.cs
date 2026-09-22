namespace Xueqing.Windows.Core.Models;

public sealed record ObservationDraftScope(
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid AssignmentId);

public sealed record ObservationDraftSnapshot(
    long Epoch,
    string Text,
    DateTimeOffset UpdatedAt);

public sealed record ObservationDraftOpenResult(
    long Epoch,
    ObservationDraftSnapshot? Recovered);

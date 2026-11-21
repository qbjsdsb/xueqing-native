using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Core.Services;

public sealed record StudentPaperLongitudinalReadResult(
    bool IsSuccess,
    StudentPaperLongitudinalSnapshot? Snapshot,
    LearningReadFailure? Failure)
{
    public static StudentPaperLongitudinalReadResult Success(StudentPaperLongitudinalSnapshot snapshot) =>
        new(true, snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null);

    public static StudentPaperLongitudinalReadResult Failed(LearningReadFailureKind kind, string code) =>
        new(false, null, new LearningReadFailure(kind, code));
}

public sealed record SaveStudyPlanDraftResult(
    bool IsSuccess,
    SaveStudyPlanDraftReceipt? Receipt,
    LearningReadFailure? Failure)
{
    public static SaveStudyPlanDraftResult Success(SaveStudyPlanDraftReceipt receipt) =>
        new(true, receipt ?? throw new ArgumentNullException(nameof(receipt)), null);

    public static SaveStudyPlanDraftResult Failed(LearningReadFailureKind kind, string code) =>
        new(false, null, new LearningReadFailure(kind, code));
}

public sealed record AttachmentDownloadResult(
    bool IsSuccess,
    DownloadedAttachmentImage? Image,
    LearningReadFailure? Failure)
{
    public static AttachmentDownloadResult Success(DownloadedAttachmentImage image) =>
        new(true, image ?? throw new ArgumentNullException(nameof(image)), null);

    public static AttachmentDownloadResult Failed(LearningReadFailureKind kind, string code) =>
        new(false, null, new LearningReadFailure(kind, code));
}

public interface IStudentPaperLongitudinalReader
{
    Task<StudentPaperLongitudinalReadResult> ReadAsync(
        StudentLearningScope scope,
        Guid expectedActorAppUserId,
        int windowDays = 90,
        int limit = 30,
        CancellationToken cancellationToken = default);
}

public interface IStudyPlanDraftWriter
{
    Task<SaveStudyPlanDraftResult> SaveDraftAsync(
        SaveStudyPlanDraftRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPaperAttachmentDownloader
{
    Task<AttachmentDownloadResult> DownloadImageAsync(
        string bucketId,
        string objectName,
        CancellationToken cancellationToken = default);
}

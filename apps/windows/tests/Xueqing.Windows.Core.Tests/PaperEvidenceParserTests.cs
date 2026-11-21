using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class PaperEvidenceParserTests
{
    [TestMethod]
    public void ParseLongitudinal_ParsesAllMetricsPagesMistakesAndStudyPlanDrafts()
    {
        var actorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var orgId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var studentId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var subjectId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var assignmentId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var paperId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        var attId = Guid.Parse("70000000-0000-0000-0000-000000000001");
        var mistakeId = Guid.Parse("80000000-0000-0000-0000-000000000001");
        var draftId = Guid.Parse("90000000-0000-0000-0000-000000000001");

        var scope = new StudentLearningScope(orgId, studentId, subjectId);

        var json = $$"""
        {
          "contract": "student_paper_longitudinal_v1",
          "generated_at_server": "2026-11-22T03:00:00Z",
          "actor_app_user_id": "{{actorId:D}}",
          "organization_id": "{{orgId:D}}",
          "organization_name": "虚构机构甲",
          "organization_time_zone": "Asia/Shanghai",
          "organization_business_date": "2026-11-22",
          "student_id": "{{studentId:D}}",
          "student_display_name": "虚构学生甲",
          "subject_profile_id": "{{subjectId:D}}",
          "subject_key": "chinese",
          "assignment_id": "{{assignmentId:D}}",
          "window_days": 90,
          "window_start_date": "2026-08-25",
          "summary": {
            "total_papers": 3,
            "confirmed_papers": 2,
            "draft_ready_papers": 1,
            "awaiting_extraction_papers": 0,
            "total_mistakes_confirmed": 3,
            "total_mistakes_all": 4,
            "teacher_marked_avg_score_ratio": 0.8500,
            "late_section_mistake_ratio": 0.7500
          },
          "weekly_cadence": [
            {
              "week_start": "2026-11-16",
              "paper_count": 2,
              "confirmed_count": 1,
              "mistake_count": 2,
              "homework_count": 1,
              "quiz_count": 1,
              "unit_test_count": 0,
              "exam_count": 0
            }
          ],
          "root_cause_breakdown": [
            {
              "error_root_cause": "time_pressure",
              "recent_30d_count": 1,
              "prior_window_count": 1,
              "total_count": 2
            }
          ],
          "recurring_topics": [
            {
              "topic_tag": "因果关系",
              "occurrence_count": 3,
              "paper_count": 2,
              "first_seen_date": "2026-10-13",
              "last_seen_date": "2026-11-17",
              "linked_case_ids": []
            }
          ],
          "papers": [
            {
              "paper_submission_id": "{{paperId:D}}",
              "source_observation_id": null,
              "paper_date": "2026-11-17",
              "paper_kind": "unit_test",
              "title": "第二单元测验",
              "grading_source": "teacher_marked",
              "confirmation_state": "human_confirmed",
              "question_count": 10,
              "mistake_count": 1,
              "score_earned": 90,
              "score_total": 100,
              "duration_minutes": 40,
              "extraction_summary": "仅末尾大题失分",
              "version": 2,
              "created_at_server": "2026-11-17T08:00:00Z",
              "confirmed_at_server": "2026-11-17T09:00:00Z",
              "pages": [
                {
                  "page_index": 1,
                  "attachment_id": "{{attId:D}}",
                  "header_redacted": true,
                  "bucket_id": "teaching-attachments-v1",
                  "object_name": "v1/org/{{orgId:D}}/student/{{studentId:D}}/profile/{{subjectId:D}}/observation/{{paperId:D}}/attachment/{{attId:D}}",
                  "content_type": "image/jpeg",
                  "byte_size": 2048
                }
              ],
              "mistakes": [
                {
                  "mistake_id": "{{mistakeId:D}}",
                  "page_index": 1,
                  "question_label": "Q10",
                  "topic_tags": ["因果关系"],
                  "error_root_cause": "time_pressure",
                  "paper_section_position": "late",
                  "points_lost": 10,
                  "corrected_on_paper": false,
                  "summary": "末尾归纳题未写完",
                  "provenance": "human_confirmed",
                  "linked_case_id": null
                }
              ]
            }
          ],
          "recent_study_plan_drafts": [
            {
              "draft_id": "{{draftId:D}}",
              "title": "近90天语文复盘计划草案",
              "brief_markdown": "## 趋势\n后半段失分偏高",
              "evidence_refs": [],
              "proposed_actions": [],
              "window_days": 90,
              "paper_count_analyzed": 3,
              "status": "draft",
              "version": 1,
              "created_at_server": "2026-11-22T02:00:00Z",
              "updated_at_server": "2026-11-22T02:00:00Z"
            }
          ]
        }
        """;

        var snapshot = PaperEvidenceJsonParser.ParseLongitudinal(json, scope, actorId);
        Assert.AreEqual(3, snapshot.Summary.TotalPapers);
        Assert.AreEqual(0.8500m, snapshot.Summary.TeacherMarkedAvgScoreRatio);
        Assert.AreEqual(0.7500m, snapshot.Summary.LateSectionMistakeRatio);
        Assert.AreEqual(1, snapshot.WeeklyCadence.Count);
        Assert.AreEqual(PaperErrorRootCause.TimePressure, snapshot.RootCauseBreakdown[0].ErrorRootCause);
        Assert.AreEqual("因果关系", snapshot.RecurringTopics[0].TopicTag);
        Assert.AreEqual(1, snapshot.Papers.Count);
        Assert.AreEqual(1, snapshot.Papers[0].Pages.Count);
        Assert.IsTrue(snapshot.Papers[0].Pages[0].HeaderRedacted);
        Assert.AreEqual(1, snapshot.Papers[0].Mistakes.Count);
        Assert.AreEqual(1, snapshot.RecentStudyPlanDrafts.Count);
    }

    [TestMethod]
    public void ParseLongitudinal_RejectsScopeMismatch()
    {
        var actorId = Guid.NewGuid();
        var scope = new StudentLearningScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var mismatchedJson = $$"""
        {
          "contract": "student_paper_longitudinal_v1",
          "generated_at_server": "2026-11-22T03:00:00Z",
          "actor_app_user_id": "{{actorId:D}}",
          "organization_id": "{{Guid.NewGuid():D}}",
          "organization_name": "Other",
          "organization_time_zone": "Asia/Shanghai",
          "organization_business_date": "2026-11-22",
          "student_id": "{{scope.StudentId:D}}",
          "student_display_name": "Student",
          "subject_profile_id": "{{scope.SubjectProfileId:D}}",
          "subject_key": "math",
          "assignment_id": "{{Guid.NewGuid():D}}",
          "window_days": 90,
          "window_start_date": "2026-08-25",
          "summary": {
            "total_papers": 0, "confirmed_papers": 0, "draft_ready_papers": 0,
            "awaiting_extraction_papers": 0, "total_mistakes_confirmed": 0,
            "total_mistakes_all": 0, "teacher_marked_avg_score_ratio": null,
            "late_section_mistake_ratio": null
          },
          "weekly_cadence": [],
          "root_cause_breakdown": [],
          "recurring_topics": [],
          "papers": [],
          "recent_study_plan_drafts": []
        }
        """;

        Assert.ThrowsExactly<InvalidDataException>(() =>
            PaperEvidenceJsonParser.ParseLongitudinal(mismatchedJson, scope, actorId));
    }
}

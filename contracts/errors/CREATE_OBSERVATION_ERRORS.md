# CreateObservation v1 stable error codes

The PostgreSQL RPC raises SQLSTATE `P0001` with one of these stable Xueqing messages. Provider adapters map these strings into provider-neutral domain errors; ViewModels must not parse Supabase-specific exception classes directly.

| Code | Meaning |
| --- | --- |
| `XQ_OPERATION_ID_REQUIRED` | Missing stable operation id. |
| `XQ_TEACHING_CONTEXT_REQUIRED` | Organization / Student / profile / assignment identity is incomplete. |
| `XQ_INVALID_OBSERVATION_TEXT` | Raw text is empty/whitespace or exceeds the v1 limit. |
| `XQ_INVALID_CAPTURE_METADATA` | Client capture metadata is not a JSON object. |
| `XQ_AUTH_REQUIRED` | No authenticated provider subject is available. |
| `XQ_ACTOR_NOT_FOUND` | Authenticated subject is not mapped to an AppUser. |
| `XQ_ACTOR_DISABLED` | AppUser exists but is disabled. |
| `XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD` | Existing operation id belongs to a different actor/payload/command. |
| `XQ_MEMBERSHIP_REQUIRED` | Actor has no Membership in the target organization. |
| `XQ_MEMBERSHIP_DISABLED` | Membership exists but is disabled. |
| `XQ_TEACHING_CAPABILITY_REQUIRED` | Membership cannot create teaching facts. |
| `XQ_STUDENT_NOT_IN_ORG` | Student is inactive, missing, or not in the target organization. |
| `XQ_SUBJECT_PROFILE_REQUIRED` | Active StudentSubjectProfile does not match organization + Student. |
| `XQ_TEACHER_ASSIGNMENT_REQUIRED` | Active StudentTeacherAssignment does not match actor + organization + Student + subject profile. |

A provider/network failure is **not** one of these deterministic domain rejections. Unknown-result network failures keep the same `operation_id` for later receipt resolution/retry.

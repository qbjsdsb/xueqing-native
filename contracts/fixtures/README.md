# Cross-client Contract Fixtures

Android and Windows share semantics, not generated client implementations.

V1 contract verification should use versioned fictional JSON fixtures (and lightweight schema where useful) for projections, commands and stable error responses. Both C# and Kotlin tests must deserialize the same fixtures and assert equivalent domain meaning.

Candidate fixtures include `personal_bootstrap_v1`, `student_detail_v1`, `organization_supervision_v1`, `quick_capture_request_v1`, `quick_capture_committed_v1`, `version_conflict_v1` and `session_revoked_v1`.

Do not put real student data here. Do not let database/OpenAPI shape automatically become the product contract.

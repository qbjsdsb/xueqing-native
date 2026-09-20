# Domain Error Contracts

Stable domain errors must be understandable on both Android and Windows.

Expected families include:

- unauthenticated / session_revoked;
- membership_inactive / organization_inactive;
- assignment_required / teaching_scope_required;
- entity_state_invalid;
- version_conflict / stale_plan;
- operation_unknown / operation_committed;
- offline_lease_expired;
- validation_failed;
- invalid_case_transition / invalid_case_target_state;
- case_not_stable / case_not_closed / case_closed;
- primary_action_required / closed_case_has_pending_action.

Provider exception strings must not leak through as product contracts.
## Observation Attachment families

- attachment_context_required;
- observation_attachment_parent_required;
- attachment_object_required;
- attachment_content_type_invalid;
- attachment_storage_metadata_invalid;
- attachment_size_invalid;
- attachment_already_committed.

Storage upload denial remains an authorization/RLS outcome. Uploaded bytes without a committed application metadata row are not domain-visible.

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
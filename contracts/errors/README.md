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
- validation_failed.

Provider exception strings must not leak through as product contracts.
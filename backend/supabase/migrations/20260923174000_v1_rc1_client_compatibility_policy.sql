-- V1 RC1 client compatibility policy.
--
-- This migration binds the frozen release identities from release/version.properties
-- to the authoritative compatibility endpoint. Version ordering remains explicit:
-- no semantic-version parsing is introduced into PostgreSQL.

insert into public.client_compatibility_policies (
    platform,
    policy_revision,
    supported_app_versions,
    security_blocked_app_versions,
    minimum_supported_app_version,
    recommended_app_version,
    minimum_supported_contract_version,
    server_contract_version,
    update_uri,
    updated_at
) values
    (
        'android',
        'v1-rc1',
        array['1.0.0-rc.1'],
        '{}'::text[],
        '1.0.0-rc.1',
        '1.0.0-rc.1',
        1,
        1,
        'https://github.com/qbjsdsb/xueqing-native/releases',
        pg_catalog.clock_timestamp()
    ),
    (
        'windows',
        'v1-rc1',
        array['1.0.0.1'],
        '{}'::text[],
        '1.0.0.1',
        '1.0.0.1',
        1,
        1,
        'https://github.com/qbjsdsb/xueqing-native/releases',
        pg_catalog.clock_timestamp()
    )
on conflict (platform) do update
set
    policy_revision = excluded.policy_revision,
    supported_app_versions = excluded.supported_app_versions,
    security_blocked_app_versions = excluded.security_blocked_app_versions,
    minimum_supported_app_version = excluded.minimum_supported_app_version,
    recommended_app_version = excluded.recommended_app_version,
    minimum_supported_contract_version = excluded.minimum_supported_contract_version,
    server_contract_version = excluded.server_contract_version,
    update_uri = excluded.update_uri,
    updated_at = excluded.updated_at;

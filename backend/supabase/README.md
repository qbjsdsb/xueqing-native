# Backend / Supabase

Development/reference backend for Xueqing Native.

The product is PostgreSQL-first and provider-neutral. Supabase is the default development path because it provides PostgreSQL, Auth, RLS, Storage and Functions with a strong local-development workflow. Production provider/region is not frozen until compatibility, real-network, old-token, Storage and restore gates pass.

When implementation starts, this directory will contain:

```text
migrations/
functions/
tests/
seed.sql
config.toml
```

Rules:

- migrations are schema truth;
- only fictional seed data;
- RLS/GRANT tests prove authorization independently of UI;
- high-risk domain state changes use transactional database commands/RPC;
- service/admin secrets never enter clients or Git;
- Realtime must not be required for correctness;
- backup plans must include both DB and Storage objects.
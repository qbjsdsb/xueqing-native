# Time Semantics

## Business time

Server projections are authoritative for organization `business_date` and `organization_time_zone`. Today/overdue/due-date domain interpretation must not depend on the device's current timezone.

Formal event timestamps use server/UTC semantics as defined by the domain.

## Device runtime time

Device clocks may drive debounce, animation, retry/backoff, local performance measurements and other runtime mechanics. Inject testable clock/time providers rather than scattering direct `now()` calls.

## Offline Access Lease

Lease design must not rely only on a mutable wall clock. The security Spike must cover server-issued validity, recent trusted server time, monotonic elapsed-time evidence where available, wall-clock rollback, restart and restoration of old device backups/snapshots. Restored sensitive cache must require authorization revalidation before exposure when trust cannot be established.

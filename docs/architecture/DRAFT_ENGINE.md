# Draft Engine Contract

## Purpose

Draft persistence protects teacher work across navigation, backgrounding, process death, network failure and attachment operations.

## Invariants inherited from legacy Xueqing

- drafts are scope-isolated;
- writes for one draft scope are serialized;
- text autosave may debounce during active editing;
- lifecycle/background transitions request an immediate flush;
- attachment staging is generation-based/two-phase so an incomplete new generation never destroys the last complete draft;
- formal commit/discard creates a barrier that prevents late timers from resurrecting stale drafts;
- retry keeps the same formal intent/operation identity where applicable.

## Phase 1 Android durability evidence

The first native Android candidate deliberately proves only durable text-draft behavior before Outbox/cloud semantics are introduced:

- the Room scope key covers environment, application-owned user, organization, Student, subject and capture context;
- the persisted scope identifier is a deterministic SHA-256 key rather than a readable concatenation of those identifiers;
- `drafts` stores the current text snapshot while `draft_scope_state` owns a monotonically advancing epoch;
- every queued ViewModel write is bound both to the UI revision and to the draft session/epoch that existed when that write was scheduled;
- explicit discard deletes the current draft and advances the epoch transactionally, so delayed work from the previous epoch cannot adopt the newly opened session and resurrect discarded text;
- `onStop` requests an immediate revision-bound flush without bypassing a later edit or discard;
- the committed Room schema is checked against generated schema output in CI;
- API 36 device tests cover scope isolation, stale-save rejection after discard, Activity recreation, background/foreground transitions and UI-level explicit discard recovery;
- the device workflow separately seeds a known draft, keeps the installed target app/data intact, performs a real `force-stop`, relaunches the app and verifies the recovered text through the UI.

This evidence is a durability gate, not a claim that Android local storage is production-secure or that a record has been submitted to the server.

## Known limitations after the durability gate

- the Quick Capture prototype still uses a deterministic fictional scope rather than production auth/organization/Student selection;
- Room content is not yet protected by the dedicated Android local-data encryption/Keystore gate;
- there is no Outbox, WorkManager retry, server command, authoritative receipt or conflict handling yet;
- attachment/photo staging is not implemented by this slice;
- `完成记录` remains a local prototype action and must not be represented as remote or authoritative save success.

## IME awareness

Chinese IME composition is not the same as committed text. Windows and Android Spike tests must cover Pinyin/composition events, Enter/Esc/global shortcuts and autosave/validation behavior. Do not treat every intermediate composition update as a final semantic submission.

## Storage

Draft content belongs to Durable Intent, not disposable Projection Cache. Production drafts require the same local privacy/encryption/backup-exclusion policy as other sensitive local educational data.

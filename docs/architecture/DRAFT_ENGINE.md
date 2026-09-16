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

## IME awareness

Chinese IME composition is not the same as committed text. Windows and Android Spike tests must cover Pinyin/composition events, Enter/Esc/global shortcuts and autosave/validation behavior. Do not treat every intermediate composition update as a final semantic submission.

## Storage

Draft content belongs to Durable Intent, not disposable Projection Cache. Production drafts require the same local privacy/encryption/backup-exclusion policy as other sensitive local educational data.

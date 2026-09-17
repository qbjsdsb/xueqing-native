package com.xueqing.app.durability

class DraftStore(
    private val dao: DraftDao,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    data class Session(
        val scope: DraftScope,
        val epoch: Long,
        val recovered: DraftSnapshot?,
    )

    suspend fun open(scope: DraftScope): Session {
        val (epoch, entity) = dao.open(scope.storageKey)
        return Session(
            scope = scope,
            epoch = epoch,
            recovered = entity?.toSnapshot(),
        )
    }

    suspend fun save(session: Session, text: String): Boolean =
        dao.save(
            scopeKey = session.scope.storageKey,
            expectedEpoch = session.epoch,
            text = text,
            updatedAtEpochMillis = clock(),
        )

    suspend fun load(scope: DraftScope): DraftSnapshot? =
        dao.open(scope.storageKey).second?.toSnapshot()

    suspend fun discard(session: Session): Long? =
        dao.discard(
            scopeKey = session.scope.storageKey,
            expectedEpoch = session.epoch,
        )

    private fun DraftEntity.toSnapshot() = DraftSnapshot(
        epoch = epoch,
        text = text,
        updatedAtEpochMillis = updatedAtEpochMillis,
    )
}

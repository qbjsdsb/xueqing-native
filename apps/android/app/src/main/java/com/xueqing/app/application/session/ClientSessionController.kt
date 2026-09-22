package com.xueqing.app.application.session

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

enum class ClientSessionStage {
    Restoring,
    SignedOut,
    ReconnectRequired,
    Authenticated,
    Busy,
    ConfigurationUnavailable,
}

data class ClientSessionState(
    val stage: ClientSessionStage,
    val message: String = "",
)

interface ClientSessionController {
    val state: StateFlow<ClientSessionState>

    suspend fun signIn(email: String, password: String)

    suspend fun retryRestore()

    suspend fun clearLocalSession()

    suspend fun signOut()
}

class UnavailableClientSessionController(
    message: String = "当前客户端配置不可用，无法建立安全登录会话。",
) : ClientSessionController {
    private val mutableState = MutableStateFlow(
        ClientSessionState(
            ClientSessionStage.ConfigurationUnavailable,
            message,
        ),
    )

    override val state: StateFlow<ClientSessionState> = mutableState.asStateFlow()

    override suspend fun signIn(email: String, password: String) = Unit

    override suspend fun retryRestore() = Unit

    override suspend fun clearLocalSession() = Unit

    override suspend fun signOut() = Unit
}

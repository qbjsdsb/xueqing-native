package com.xueqing.app.presentation

import android.net.Uri
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp

@Composable
internal fun QuickCaptureScreen(
    state: QuickCaptureUiState,
    onTextChanged: (String) -> Unit,
    onClose: () -> Unit,
    onChooseStudent: () -> Unit,
    onPhotoSelected: (Uri) -> Unit,
    onRemovePhoto: () -> Unit,
    onDiscard: () -> Unit,
    onSubmit: () -> Unit,
) {
    BackHandler(onBack = onClose)

    val photoPicker = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.PickVisualMedia(),
    ) { uri ->
        uri?.let(onPhotoSelected)
    }

    val contextReady = state.teachingContextStatus == TeachingContextStatus.Ready
    val draftLoaded = state.draftStatus != LocalDraftStatus.Loading
    val draftWritable = state.draftStatus != LocalDraftStatus.PersistenceFailed
    val attachmentBusy = state.attachmentStatus == AttachmentDraftStatus.Protecting

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp, vertical = 16.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            TextButton(onClick = onClose) {
                Text("返回")
            }
            TextButton(
                enabled = contextReady && draftLoaded && !attachmentBusy,
                onClick = onDiscard,
            ) {
                Text("丢弃草稿")
            }
        }

        Spacer(Modifier.height(16.dp))
        Text(
            text = "快速记录",
            style = MaterialTheme.typography.headlineSmall,
        )
        Text(
            text = when (state.teachingContextStatus) {
                TeachingContextStatus.Loading -> "正在读取教学上下文…"
                TeachingContextStatus.Ready -> "${state.studentDisplayName} · ${state.subjectLabel}"
                TeachingContextStatus.AuthenticationRequired -> "需要登录后才能记录"
                TeachingContextStatus.Unavailable -> "当前没有可用的任课关系"
                TeachingContextStatus.SelectionRequired -> "请先选择明确的学生和学科后再记录"
            },
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier
                .padding(top = 4.dp)
                .testTag("quick-capture-teaching-context"),
        )

        if (state.teachingContextStatus == TeachingContextStatus.SelectionRequired) {
            Text(
                text = "为了避免把课堂记录写到错误的学生名下，这次记录需要先从学生列表进入。",
                style = MaterialTheme.typography.bodyMedium,
                modifier = Modifier.padding(top = 28.dp),
            )
            TextButton(
                onClick = onChooseStudent,
                modifier = Modifier.padding(top = 8.dp),
            ) {
                Text("去选择学生")
            }
        } else if (!contextReady) {
            Text(
                text = "当前输入框会在教学上下文准备好后开放。",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 28.dp),
            )
        } else {
            Spacer(Modifier.height(24.dp))
            Text(
                text = "你刚才观察到了什么？",
                style = MaterialTheme.typography.titleMedium,
            )
            OutlinedTextField(
                value = state.text,
                onValueChange = onTextChanged,
                enabled = draftLoaded,
                modifier = Modifier
                    .fillMaxWidth()
                    .heightIn(min = 220.dp)
                    .padding(top = 10.dp)
                    .testTag("quick-capture-input"),
                placeholder = { Text("例如：概括题仍然容易照抄原句，不能主动压缩信息。") },
            )

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 16.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                OutlinedButton(
                    onClick = {
                        photoPicker.launch(
                            PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly),
                        )
                    },
                    enabled = draftLoaded &&
                        draftWritable &&
                        !attachmentBusy &&
                        state.attachmentCount == 0,
                    modifier = Modifier.testTag("quick-capture-add-photo"),
                ) {
                    Text("添加照片")
                }

                if (state.attachmentCount > 0) {
                    TextButton(
                        onClick = onRemovePhoto,
                        enabled = !attachmentBusy,
                        modifier = Modifier.testTag("quick-capture-remove-photo"),
                    ) {
                        Text("移除照片")
                    }
                }
            }

            attachmentMessage(state)?.let { message ->
                Text(
                    text = message,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier
                        .padding(top = 8.dp)
                        .testTag("quick-capture-attachment-status"),
                )
            }

            Text(
                text = when (state.draftStatus) {
                    LocalDraftStatus.Loading -> "正在读取本机草稿…"
                    LocalDraftStatus.Saving -> "正在保护草稿…"
                    LocalDraftStatus.SafeOnDevice -> if (state.recoveredFromDisk) {
                        "已恢复本机草稿"
                    } else {
                        "草稿已保存在本机"
                    }
                    LocalDraftStatus.PersistenceFailed -> "本机草稿保存失败，当前文字仍保留在界面中"
                },
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier
                    .padding(top = 12.dp)
                    .testTag("quick-capture-draft-status"),
            )

            submissionMessage(state)?.let { message ->
                Text(
                    text = message,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier
                        .padding(top = 8.dp)
                        .testTag("quick-capture-submission-status"),
                )
            }

            Button(
                onClick = onSubmit,
                enabled = draftLoaded && draftWritable && !attachmentBusy && state.text.isNotBlank(),
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 28.dp),
            ) {
                Text("提交记录")
            }
            Text(
                text = "提交后会先安全写入本机队列；服务端仍会重新检查当前任课权限。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 8.dp, bottom = 16.dp),
            )
        }
    }
}

private fun submissionMessage(state: QuickCaptureUiState): String? = when (state.submissionStatus) {
    ObservationSubmissionStatus.None -> null
    ObservationSubmissionStatus.WaitingToSync -> "待同步"
    ObservationSubmissionStatus.Syncing -> "正在同步"
    ObservationSubmissionStatus.WaitingToRetry -> "待重试 · 记录已安全保存在本机"
    ObservationSubmissionStatus.AuthenticationRequired -> "登录状态已失效 · 记录仍保存在本机"
    ObservationSubmissionStatus.Accepted -> "服务端已接受"
    ObservationSubmissionStatus.Rejected -> when (state.submissionErrorCode) {
        "XQ_TEACHER_ASSIGNMENT_REQUIRED" -> "服务器拒绝 · 当前任课关系已变化，原记录仍保存在本机"
        else -> "服务器拒绝 · 原记录仍保存在本机"
    }
}


private fun attachmentMessage(state: QuickCaptureUiState): String? = when (state.attachmentStatus) {
    AttachmentDraftStatus.None -> null
    AttachmentDraftStatus.Protecting -> "正在保护照片…"
    AttachmentDraftStatus.SafeOnDevice -> "照片已安全保存在本机"
    AttachmentDraftStatus.Failed -> when (state.attachmentErrorCode) {
        "too_large" -> "照片处理失败 · 文件过大，请换一张"
        "local_key_unavailable" -> "照片保护不可用 · 文字草稿不受影响"
        "source_unavailable" -> "照片读取失败 · 文字草稿不受影响"
        else -> "照片处理失败 · 文字草稿不受影响"
    }
}

package com.xueqing.app.presentation

import android.net.Uri
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.xueqing.app.application.paper.PaperKind

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
    onHeaderRedactionToggled: (Boolean) -> Unit = {},
    onPaperCaptureModeToggled: (Boolean) -> Unit = {},
    onPaperKindSelected: (PaperKind) -> Unit = {},
    onPaperDateChanged: (String) -> Unit = {},
    onPaperTitleChanged: (String) -> Unit = {},
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
    val canSubmit = draftLoaded &&
        draftWritable &&
        !attachmentBusy &&
        (state.text.isNotBlank() || (state.paperCaptureMode && state.attachmentCount > 0))

    Box(
        modifier = Modifier
            .fillMaxSize()
            .safeDrawingPadding()
            .imePadding(),
        contentAlignment = Alignment.TopCenter,
    ) {
        Column(
            modifier = Modifier
                .widthIn(max = 680.dp)
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 20.dp, vertical = 12.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
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

            Spacer(Modifier.height(4.dp))
            Text(
                text = if (state.paperCaptureMode) "拍卷子" else "快速记录",
                style = MaterialTheme.typography.headlineSmall,
            )
            Text(
                text = when (state.teachingContextStatus) {
                    TeachingContextStatus.Loading -> "正在读取…"
                    TeachingContextStatus.Ready -> "${state.studentDisplayName} · ${state.subjectLabel}"
                    TeachingContextStatus.AuthenticationRequired -> "请先登录"
                    TeachingContextStatus.Unavailable -> "当前没有可用的任课关系"
                    TeachingContextStatus.SelectionRequired -> "请先选择学生和学科"
                },
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier
                    .padding(top = 2.dp)
                    .testTag("quick-capture-teaching-context"),
            )

            if (state.teachingContextStatus == TeachingContextStatus.SelectionRequired) {
                Text(
                    text = "请先从学生列表进入对应学科后再记录。",
                    style = MaterialTheme.typography.bodyMedium,
                    modifier = Modifier.padding(top = 24.dp),
                )
                TextButton(
                    onClick = onChooseStudent,
                    modifier = Modifier.padding(top = 8.dp),
                ) {
                    Text("去选择学生")
                }
            } else if (!contextReady) {
                Text(
                    text = "请等待上下文准备完成。",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 24.dp),
                )
            } else {
                Spacer(Modifier.height(10.dp))

                // Apple Notes-style borderless paper writing canvas
                TextField(
                    value = state.text,
                    onValueChange = onTextChanged,
                    enabled = draftLoaded,
                    textStyle = MaterialTheme.typography.bodyLarge,
                    colors = TextFieldDefaults.colors(
                        focusedContainerColor = Color.Transparent,
                        unfocusedContainerColor = Color.Transparent,
                        disabledContainerColor = Color.Transparent,
                        focusedIndicatorColor = Color.Transparent,
                        unfocusedIndicatorColor = Color.Transparent,
                        disabledIndicatorColor = Color.Transparent,
                    ),
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(min = if (state.paperCaptureMode) 96.dp else 148.dp)
                        .testTag("quick-capture-input"),
                    placeholder = {
                        Text(
                            text = if (state.paperCaptureMode) {
                                "补充备注（可选，如“后半段大题时间紧”）…"
                            } else {
                                "记下刚才的观察…"
                            },
                            style = MaterialTheme.typography.bodyLarge,
                            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.72f),
                        )
                    },
                )

                HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)

                // Compact single-row Notes-style toolbar: Add photo · Redact header · Archive as paper
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .horizontalScroll(rememberScrollState())
                        .padding(vertical = 10.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.CenterVertically,
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
                            state.attachmentCount < state.maxAttachmentCount,
                        modifier = Modifier.testTag("quick-capture-add-photo"),
                    ) {
                        Text(
                            if (state.attachmentCount == 0) {
                                "添加照片"
                            } else {
                                "加页 (${state.attachmentCount}/${state.maxAttachmentCount})"
                            },
                        )
                    }

                    if (state.attachmentCount > 0) {
                        TextButton(
                            onClick = onRemovePhoto,
                            enabled = !attachmentBusy,
                            modifier = Modifier.testTag("quick-capture-remove-photo"),
                        ) {
                            Text("移除")
                        }
                    }

                    FilterChip(
                        selected = state.headerRedactionEnabled,
                        onClick = { onHeaderRedactionToggled(!state.headerRedactionEnabled) },
                        enabled = !attachmentBusy,
                        label = { Text("遮挡姓名") },
                        modifier = Modifier
                            .semantics { contentDescription = "自动遮挡卷头姓名区" }
                            .testTag("quick-capture-header-redaction-toggle"),
                    )

                    FilterChip(
                        selected = state.paperCaptureMode,
                        onClick = { onPaperCaptureModeToggled(!state.paperCaptureMode) },
                        enabled = draftLoaded && !attachmentBusy,
                        label = { Text("按卷归档") },
                        modifier = Modifier
                            .semantics { contentDescription = "按试卷或作业归档" }
                            .testTag("quick-capture-paper-mode-toggle"),
                    )
                }

                if (state.paperCaptureMode) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(bottom = 10.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .horizontalScroll(rememberScrollState()),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                        ) {
                            PaperKind.entries.forEach { kind ->
                                FilterChip(
                                    selected = state.paperKind == kind,
                                    onClick = { onPaperKindSelected(kind) },
                                    label = { Text(kind.displayLabel) },
                                    modifier = Modifier.testTag("quick-capture-paper-kind-${kind.wireValue}"),
                                )
                            }
                        }

                        OutlinedTextField(
                            value = state.paperDate,
                            onValueChange = onPaperDateChanged,
                            enabled = draftLoaded,
                            singleLine = true,
                            modifier = Modifier
                                .fillMaxWidth()
                                .testTag("quick-capture-paper-date"),
                            label = { Text("日期") },
                            placeholder = { Text("YYYY-MM-DD") },
                        )

                        OutlinedTextField(
                            value = state.paperTitle,
                            onValueChange = onPaperTitleChanged,
                            enabled = draftLoaded,
                            singleLine = true,
                            modifier = Modifier
                                .fillMaxWidth()
                                .testTag("quick-capture-paper-title"),
                            label = { Text("标题（可选）") },
                            placeholder = { Text("如：第三单元周测") },
                        )
                    }
                }

                // Quiet status lines (no heavy box)
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 4.dp),
                    verticalArrangement = Arrangement.spacedBy(3.dp),
                ) {
                    attachmentMessage(state)?.let { message ->
                        Text(
                            text = message,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier
                                .semantics { liveRegion = LiveRegionMode.Polite }
                                .testTag("quick-capture-attachment-status"),
                        )
                    }

                    Text(
                        text = when (state.draftStatus) {
                            LocalDraftStatus.Loading -> "正在读取草稿…"
                            LocalDraftStatus.Saving -> "保存中…"
                            LocalDraftStatus.SafeOnDevice -> if (state.recoveredFromDisk) {
                                "已恢复本机草稿"
                            } else {
                                "草稿已保存在本机"
                            }
                            LocalDraftStatus.PersistenceFailed -> "本机保存失败，当前文字仍保留"
                        },
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier
                            .semantics { liveRegion = LiveRegionMode.Polite }
                            .testTag("quick-capture-draft-status"),
                    )

                    submissionMessage(state)?.let { message ->
                        Text(
                            text = message,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier
                                .semantics { liveRegion = LiveRegionMode.Polite }
                                .testTag("quick-capture-submission-status"),
                        )
                    }
                }

                Button(
                    onClick = onSubmit,
                    enabled = canSubmit,
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 16.dp, bottom = 12.dp)
                        .testTag("quick-capture-submit"),
                ) {
                    Text("提交记录")
                }
            }
        }
    }
}

private fun submissionMessage(state: QuickCaptureUiState): String? = when (state.submissionStatus) {
    ObservationSubmissionStatus.None -> null
    ObservationSubmissionStatus.WaitingToSync -> "待同步"
    ObservationSubmissionStatus.Syncing -> "同步中…"
    ObservationSubmissionStatus.WaitingToRetry -> "待重试 · 已保存在本机"
    ObservationSubmissionStatus.AuthenticationRequired -> "登录状态已失效 · 记录仍保存在本机"
    ObservationSubmissionStatus.Accepted -> "已提交"
    ObservationSubmissionStatus.Rejected -> when (state.submissionErrorCode) {
        "XQ_TEACHER_ASSIGNMENT_REQUIRED" -> "当前任课关系已变化，原记录仍保存在本机"
        else -> "提交被拒绝，原记录仍保存在本机"
    }
}

private fun attachmentMessage(state: QuickCaptureUiState): String? = when (state.attachmentStatus) {
    AttachmentDraftStatus.None -> null
    AttachmentDraftStatus.Protecting -> "正在保护照片…"
    AttachmentDraftStatus.SafeOnDevice -> when {
        state.attachmentCount <= 1 && state.redactedAttachmentCount == 0 ->
            "已添加 1 张照片"
        state.redactedAttachmentCount > 0 ->
            "已安全保存 ${state.attachmentCount} 页照片（${state.redactedAttachmentCount} 页已遮挡卷头姓名区）"
        else ->
            "已添加 ${state.attachmentCount} 页照片"
    }
    AttachmentDraftStatus.Failed -> when (state.attachmentErrorCode) {
        "too_large" -> "照片文件过大，请换一张"
        "local_key_unavailable" -> "照片保护不可用 · 文字草稿不受影响"
        "source_unavailable" -> "照片读取失败 · 文字草稿不受影响"
        else -> "照片处理失败 · 文字草稿不受影响"
    }
}

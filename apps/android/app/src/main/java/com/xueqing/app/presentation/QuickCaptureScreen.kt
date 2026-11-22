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
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
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

            Spacer(Modifier.height(6.dp))
            Text(
                text = if (state.paperCaptureMode) "拍卷子归档" else "快速记录",
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
                    .padding(top = 2.dp)
                    .testTag("quick-capture-teaching-context"),
            )

            if (state.teachingContextStatus == TeachingContextStatus.SelectionRequired) {
                Text(
                    text = "为了避免把课堂记录写到错误的学生名下，这次记录需要先从学生列表进入。",
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
                    text = "当前输入框会在教学上下文准备好后开放。",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 24.dp),
                )
            } else {
                Spacer(Modifier.height(14.dp))

                // Compact observation input (sized cleanly so the submit CTA fits above the fold on 360x800 phones)
                OutlinedTextField(
                    value = state.text,
                    onValueChange = onTextChanged,
                    enabled = draftLoaded,
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(min = if (state.paperCaptureMode) 96.dp else 132.dp)
                        .testTag("quick-capture-input"),
                    label = {
                        Text(if (state.paperCaptureMode) "备注（可选）" else "课堂观察")
                    },
                    placeholder = {
                        Text(
                            if (state.paperCaptureMode) {
                                "可选，如“后半段大题时间紧”"
                            } else {
                                "记下刚才的观察…"
                            },
                        )
                    },
                )

                // Editorial hairline-separated photo & paper options (zero heavy cards or industrial jargon)
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 14.dp),
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
                                "继续加页 (${state.attachmentCount}/${state.maxAttachmentCount})"
                            },
                        )
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

                HorizontalDivider(
                    modifier = Modifier.padding(top = 14.dp),
                    color = MaterialTheme.colorScheme.outlineVariant,
                )

                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 8.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = "遮挡卷头姓名",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.weight(1f).padding(end = 12.dp),
                    )
                    Switch(
                        checked = state.headerRedactionEnabled,
                        onCheckedChange = onHeaderRedactionToggled,
                        enabled = !attachmentBusy,
                        modifier = Modifier
                            .semantics { contentDescription = "自动遮挡卷头姓名区" }
                            .testTag("quick-capture-header-redaction-toggle"),
                    )
                }

                HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)

                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 8.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = "按试卷归档",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.weight(1f).padding(end = 12.dp),
                    )
                    Switch(
                        checked = state.paperCaptureMode,
                        onCheckedChange = onPaperCaptureModeToggled,
                        enabled = draftLoaded && !attachmentBusy,
                        modifier = Modifier
                            .semantics { contentDescription = "按试卷或作业归档" }
                            .testTag("quick-capture-paper-mode-toggle"),
                    )
                }

                if (state.paperCaptureMode) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(top = 8.dp, bottom = 4.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .horizontalScroll(rememberScrollState()),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                        ) {
                            PaperKind.entries.forEach { kind ->
                                val selected = state.paperKind == kind
                                if (selected) {
                                    Button(
                                        onClick = { onPaperKindSelected(kind) },
                                        modifier = Modifier.testTag("quick-capture-paper-kind-${kind.wireValue}"),
                                    ) {
                                        Text(kind.displayLabel)
                                    }
                                } else {
                                    OutlinedButton(
                                        onClick = { onPaperKindSelected(kind) },
                                        modifier = Modifier.testTag("quick-capture-paper-kind-${kind.wireValue}"),
                                    ) {
                                        Text(kind.displayLabel)
                                    }
                                }
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

                HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)

                // Quiet typographic status lines (no boxed gray container)
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 10.dp),
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
                                "已恢复上次草稿"
                            } else {
                                "已保存在本机"
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
    AttachmentDraftStatus.SafeOnDevice -> when {
        state.attachmentCount <= 1 && state.redactedAttachmentCount == 0 ->
            "照片已安全保存在本机"
        state.redactedAttachmentCount > 0 ->
            "已安全保存 ${state.attachmentCount} 页照片（${state.redactedAttachmentCount} 页已遮挡卷头姓名区）"
        else ->
            "已安全保存 ${state.attachmentCount} 页照片（已去除 EXIF）"
    }
    AttachmentDraftStatus.Failed -> when (state.attachmentErrorCode) {
        "too_large" -> "照片处理失败 · 文件过大，请换一张"
        "local_key_unavailable" -> "照片保护不可用 · 文字草稿不受影响"
        "source_unavailable" -> "照片读取失败 · 文字草稿不受影响"
        else -> "照片处理失败 · 文字草稿不受影响"
    }
}

package com.xueqing.app.presentation

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.weight
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
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
    onDiscard: () -> Unit,
) {
    BackHandler(onBack = onClose)

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
                enabled = state.status != LocalDraftStatus.Loading,
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
            text = "虚构学生林晨 · 语文",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(top = 4.dp),
        )

        Spacer(Modifier.height(24.dp))
        Text(
            text = "你刚才观察到了什么？",
            style = MaterialTheme.typography.titleMedium,
        )
        OutlinedTextField(
            value = state.text,
            onValueChange = onTextChanged,
            enabled = state.status != LocalDraftStatus.Loading,
            modifier = Modifier
                .fillMaxWidth()
                .height(220.dp)
                .padding(top = 10.dp)
                .testTag("quick-capture-input"),
            placeholder = { Text("例如：概括题仍然容易照抄原句，不能主动压缩信息。") },
        )

        Text(
            text = when (state.status) {
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

        Spacer(Modifier.weight(1f, fill = false))
        Button(
            onClick = onClose,
            enabled = state.status != LocalDraftStatus.Loading,
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 28.dp),
        ) {
            Text("完成记录")
        }
        Text(
            text = "此原型只证明本机草稿安全；“完成记录”不会提交到服务端。",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(top = 8.dp, bottom = 16.dp),
        )
    }
}

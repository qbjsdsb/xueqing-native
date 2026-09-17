package com.xueqing.app.presentation

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.unit.dp

internal enum class PrimaryDestination(
    val label: String,
    val bootstrapGlyph: String,
) {
    Today("今日", "今"),
    Students("学生", "生"),
    Learning("学情", "学"),
}

@Composable
internal fun XueqingApp(
    quickCaptureViewModel: QuickCaptureViewModel,
    startInQuickCapture: Boolean = false,
) {
    val colorScheme = if (isSystemInDarkTheme()) darkColorScheme() else lightColorScheme()
    val quickCaptureState by quickCaptureViewModel.uiState.collectAsState()

    MaterialTheme(colorScheme = colorScheme) {
        var selectedName by rememberSaveable { mutableStateOf(PrimaryDestination.Today.name) }
        var showingQuickCapture by rememberSaveable { mutableStateOf(startInQuickCapture) }
        val selected = PrimaryDestination.entries.firstOrNull { it.name == selectedName }
            ?: PrimaryDestination.Today

        if (showingQuickCapture) {
            QuickCaptureScreen(
                state = quickCaptureState,
                onTextChanged = quickCaptureViewModel::onTextChanged,
                onClose = { showingQuickCapture = false },
                onDiscard = quickCaptureViewModel::discard,
                onSubmit = quickCaptureViewModel::submit,
            )
        } else {
            Scaffold(
                bottomBar = {
                    NavigationBar {
                        PrimaryDestination.entries.forEach { destination ->
                            NavigationBarItem(
                                selected = selected == destination,
                                onClick = { selectedName = destination.name },
                                icon = {
                                    Text(
                                        text = destination.bootstrapGlyph,
                                        modifier = Modifier.clearAndSetSemantics { },
                                        style = MaterialTheme.typography.labelMedium,
                                    )
                                },
                                label = { Text(destination.label) },
                            )
                        }
                    }
                },
            ) { innerPadding ->
                when (selected) {
                    PrimaryDestination.Today -> TodayScreen(
                        innerPadding = innerPadding,
                        onQuickCapture = { showingQuickCapture = true },
                    )
                    PrimaryDestination.Students -> StudentsScreen(innerPadding)
                    PrimaryDestination.Learning -> LearningScreen(innerPadding)
                }
            }
        }
    }
}

@Composable
private fun TodayScreen(
    innerPadding: PaddingValues,
    onQuickCapture: () -> Unit,
) {
    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .padding(innerPadding),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp),
        verticalArrangement = Arrangement.spacedBy(0.dp),
    ) {
        item {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = "今日",
                    style = MaterialTheme.typography.headlineSmall,
                )
                TextButton(onClick = onQuickCapture) {
                    Text("记录")
                }
            }
        }
        item {
            Text(
                text = "个人教学行动",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(bottom = 24.dp),
            )
        }
        item { SectionTitle("已逾期") }
        item { ActionRow("林晨", "作文迁移检查", "昨天到期") }
        item { ActionRow("王宇", "文言实词复查", "9 月 15 日") }
        item {
            SectionTitle(
                text = "今天",
                modifier = Modifier.padding(top = 24.dp),
            )
        }
        item { ActionRow("李欣", "检查病句辨析", "今天") }
        item {
            SectionTitle(
                text = "待安排",
                modifier = Modifier.padding(top = 24.dp),
            )
        }
        item { ActionRow("陈佳", "古诗文背诵抽查", "未安排日期") }
        item {
            Text(
                text = "未来 8",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 28.dp, bottom = 12.dp),
            )
        }
    }
}

@Composable
private fun StudentsScreen(innerPadding: PaddingValues) {
    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .padding(innerPadding),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp),
    ) {
        item {
            Text(
                text = "学生",
                style = MaterialTheme.typography.headlineSmall,
                modifier = Modifier.padding(bottom = 16.dp),
            )
        }
        item { StudentRow("林晨", "九年级 · 语文", "待验证 1") }
        item { StudentRow("王宇", "八年级 · 语文", "跟进中 2") }
        item { StudentRow("李欣", "七年级 · 语文", "") }
        item { StudentRow("陈佳", "九年级 · 语文", "下一步 · 周四") }
    }
}

@Composable
private fun LearningScreen(innerPadding: PaddingValues) {
    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .padding(innerPadding),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp),
    ) {
        item {
            Text(
                text = "学情",
                style = MaterialTheme.typography.headlineSmall,
                modifier = Modifier.padding(bottom = 24.dp),
            )
        }
        item { SectionTitle("当前") }
        item { LearningRow("林晨", "作文论据仍然空泛", "待验证 · 昨天") }
        item { LearningRow("王宇", "文言实词辨析不稳定", "下一步 · 周四") }
        item { LearningRow("陈佳", "阅读概括遗漏限制条件", "正在跟进") }
    }
}

@Composable
private fun SectionTitle(
    text: String,
    modifier: Modifier = Modifier,
) {
    Text(
        text = text,
        style = MaterialTheme.typography.titleMedium,
        modifier = modifier.padding(bottom = 8.dp),
    )
}

@Composable
private fun ActionRow(
    student: String,
    title: String,
    metadata: String,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 12.dp),
    ) {
        Text(text = student, style = MaterialTheme.typography.labelLarge)
        Text(
            text = title,
            style = MaterialTheme.typography.bodyLarge,
            modifier = Modifier.padding(top = 2.dp),
        )
        Text(
            text = metadata,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(top = 4.dp),
        )
    }
    HorizontalDivider()
}

@Composable
private fun StudentRow(
    name: String,
    context: String,
    state: String,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(text = name, style = MaterialTheme.typography.titleMedium)
            Text(
                text = context,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 2.dp),
            )
        }
        if (state.isNotEmpty()) {
            Spacer(Modifier.width(16.dp))
            Text(
                text = state,
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
    HorizontalDivider()
}

@Composable
private fun LearningRow(
    student: String,
    title: String,
    state: String,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 14.dp),
    ) {
        Text(text = "$student · $title", style = MaterialTheme.typography.bodyLarge)
        Text(
            text = state,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(top = 4.dp),
        )
    }
    HorizontalDivider()
}

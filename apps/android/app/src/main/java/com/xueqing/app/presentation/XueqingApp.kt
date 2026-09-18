package com.xueqing.app.presentation

import com.xueqing.app.application.bootstrap.PersonalTeachingContext

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.lazy.items
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
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
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
    studentDirectoryViewModel: StudentDirectoryViewModel,
    startInQuickCapture: Boolean = false,
) {
    val colorScheme = if (isSystemInDarkTheme()) darkColorScheme() else lightColorScheme()
    val quickCaptureState by quickCaptureViewModel.uiState.collectAsState()
    val directoryState by studentDirectoryViewModel.uiState.collectAsState()

    MaterialTheme(colorScheme = colorScheme) {
        var selectedName by rememberSaveable { mutableStateOf(PrimaryDestination.Today.name) }
        var selectedStudentKey by rememberSaveable { mutableStateOf<String?>(null) }
        var showingQuickCapture by rememberSaveable { mutableStateOf(startInQuickCapture) }
        val selected = PrimaryDestination.entries.firstOrNull { it.name == selectedName }
            ?: PrimaryDestination.Today
        val selectedStudent = directoryState.students.firstOrNull { it.key == selectedStudentKey }

        if (showingQuickCapture) {
            QuickCaptureScreen(
                state = quickCaptureState,
                onTextChanged = quickCaptureViewModel::onTextChanged,
                onClose = {
                    quickCaptureViewModel.flushNow()
                    showingQuickCapture = false
                },
                onChooseStudent = {
                    showingQuickCapture = false
                    selectedName = PrimaryDestination.Students.name
                    selectedStudentKey = null
                },
                onDiscard = quickCaptureViewModel::discard,
                onSubmit = quickCaptureViewModel::submit,
            )
        } else if (selectedStudent != null) {
            StudentDetailScreen(
                student = selectedStudent,
                onBack = { selectedStudentKey = null },
                onQuickCapture = { context ->
                    quickCaptureViewModel.selectTeachingContext(context)
                    showingQuickCapture = true
                },
            )
        } else {
            Scaffold(
                bottomBar = {
                    NavigationBar {
                        PrimaryDestination.entries.forEach { destination ->
                            NavigationBarItem(
                                selected = selected == destination,
                                onClick = {
                                    selectedName = destination.name
                                    selectedStudentKey = null
                                },
                                icon = {
                                    Text(
                                        text = destination.bootstrapGlyph,
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
                        students = directoryState.students,
                        directoryStatus = directoryState.status,
                        onQuickCapture = {
                            quickCaptureViewModel.prepareForUnscopedCapture()
                            showingQuickCapture = true
                        },
                        onOpenStudent = { key ->
                            selectedName = PrimaryDestination.Students.name
                            selectedStudentKey = key
                        },
                    )

                    PrimaryDestination.Students -> StudentsScreen(
                        innerPadding = innerPadding,
                        state = directoryState,
                        onOpenStudent = { selectedStudentKey = it },
                    )

                    PrimaryDestination.Learning -> LearningScreen(
                        innerPadding = innerPadding,
                        onOpenStudents = {
                            selectedName = PrimaryDestination.Students.name
                        },
                    )
                }
            }
        }
    }
}

@Composable
private fun TodayScreen(
    innerPadding: PaddingValues,
    students: List<StudentDirectoryItem>,
    directoryStatus: StudentDirectoryStatus,
    onQuickCapture: () -> Unit,
    onOpenStudent: (String) -> Unit,
) {
    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .padding(innerPadding),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp),
    ) {
        item {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text("今日", style = MaterialTheme.typography.headlineSmall)
                    Text(
                        text = "先把课堂中的判断记下来，再在学情里继续整理。",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(top = 6.dp),
                    )
                }
                TextButton(onClick = onQuickCapture) {
                    Text("记录")
                }
            }
        }
        item {
            SectionTitle(
                text = "我的学生",
                modifier = Modifier.padding(top = 28.dp),
            )
        }
        when (directoryStatus) {
            StudentDirectoryStatus.Loading -> item {
                Text(
                    "正在读取学生…",
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(vertical = 16.dp),
                )
            }

            StudentDirectoryStatus.AuthenticationRequired -> item {
                EmptyMessage("请登录后查看学生。")
            }

            StudentDirectoryStatus.Unavailable -> item {
                EmptyMessage("暂时无法读取学生列表，请稍后再试。")
            }

            StudentDirectoryStatus.Ready -> {
                if (students.isEmpty()) {
                    item { EmptyMessage("还没有可用的教学学生。") }
                } else {
                    items(
                        items = students.take(6),
                        key = { it.key },
                    ) { student ->
                        StudentRow(
                            student = student,
                            onClick = { onOpenStudent(student.key) },
                            testTag = "today-student-row-${student.key}",
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun StudentsScreen(
    innerPadding: PaddingValues,
    state: StudentDirectoryUiState,
    onOpenStudent: (String) -> Unit,
) {
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
                modifier = Modifier.padding(bottom = 8.dp),
            )
            Text(
                text = "按当前个人教学范围查看学生与学科档案。",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(bottom = 20.dp),
            )
        }
        when (state.status) {
            StudentDirectoryStatus.Loading -> item {
                Text(
                    "正在读取学生…",
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(vertical = 16.dp),
                )
            }

            StudentDirectoryStatus.AuthenticationRequired -> item {
                EmptyMessage("请登录后查看学生。")
            }

            StudentDirectoryStatus.Unavailable -> item {
                EmptyMessage("暂时无法读取学生列表，请稍后再试。")
            }

            StudentDirectoryStatus.Ready -> {
                if (state.students.isEmpty()) {
                    item { EmptyMessage("还没有可用的教学学生。") }
                } else {
                    items(state.students, key = { it.key }) { student ->
                        StudentRow(
                            student = student,
                            onClick = { onOpenStudent(student.key) },
                            testTag = "student-row-${student.key}",
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun StudentDetailScreen(
    student: StudentDirectoryItem,
    onBack: () -> Unit,
    onQuickCapture: (PersonalTeachingContext) -> Unit,
) {
    BackHandler(onBack = onBack)

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp),
    ) {
        item {
            TextButton(onClick = onBack) {
                Text("返回学生")
            }
            Text(
                text = "学生详情",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 8.dp),
            )
            Text(
                text = student.studentDisplayName,
                style = MaterialTheme.typography.headlineSmall,
                modifier = Modifier.padding(top = 4.dp),
            )
            Text(
                text = student.organizationName,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp, bottom = 28.dp),
            )
        }
        item { SectionTitle("学科档案") }
        items(
            items = student.contexts,
            key = { it.subjectProfileId.toString() },
        ) { context ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable(
                        role = Role.Button,
                        onClick = { onQuickCapture(context) },
                    )
                    .testTag("student-subject-record-${context.subjectProfileId}")
                    .padding(vertical = 16.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = subjectLabelForKey(context.subjectKey),
                        style = MaterialTheme.typography.titleMedium,
                    )
                    Text(
                        text = "进入快速记录",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(top = 4.dp),
                    )
                }
                Text(
                    text = "记录",
                    style = MaterialTheme.typography.labelLarge,
                    color = MaterialTheme.colorScheme.primary,
                )
            }
            HorizontalDivider()
        }
        item {
            Text(
                text = "记录提交时仍会由服务端重新检查当前任课关系。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 20.dp),
            )
        }
    }
}

@Composable
private fun LearningScreen(
    innerPadding: PaddingValues,
    onOpenStudents: () -> Unit,
) {
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
            )
            Text(
                text = "这里会承载后续的记录整理与判断闭环。",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 8.dp),
            )
        }
        item {
            Text(
                text = "先从学生开始，进入具体学科档案后记录课堂观察。",
                style = MaterialTheme.typography.bodyLarge,
                modifier = Modifier.padding(top = 28.dp),
            )
            TextButton(
                onClick = onOpenStudents,
                modifier = Modifier.padding(top = 8.dp),
            ) {
                Text("查看学生")
            }
        }
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
private fun EmptyMessage(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.bodyLarge,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(vertical = 16.dp),
    )
}

@Composable
private fun StudentRow(
    student: StudentDirectoryItem,
    onClick: () -> Unit,
    testTag: String,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(
                role = Role.Button,
                onClick = onClick,
            )
            .testTag(testTag)
            .padding(vertical = 14.dp),
        verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(text = student.studentDisplayName, style = MaterialTheme.typography.titleMedium)
            Text(
                text = "${student.organizationName} · ${student.subjectSummary}",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp),
            )
        }
        Text(
            text = "查看",
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.primary,
        )
    }
    HorizontalDivider()
}

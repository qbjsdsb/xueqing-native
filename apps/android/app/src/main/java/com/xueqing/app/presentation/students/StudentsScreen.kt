package com.xueqing.app.presentation.students

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import com.xueqing.app.presentation.FocusLearningUiState
import com.xueqing.app.presentation.StudentDirectoryItem
import com.xueqing.app.presentation.StudentDirectoryStatus
import com.xueqing.app.presentation.StudentDirectoryUiState
import com.xueqing.app.presentation.design.EmptyMessage
import com.xueqing.app.presentation.design.SectionTitle
import com.xueqing.app.presentation.design.StatusMessage
import com.xueqing.app.presentation.learning.CurrentFocusInline
import com.xueqing.app.presentation.subjectLabelForKey
import java.util.UUID

@Composable
internal fun StudentsScreen(
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
                StatusMessage("正在读取学生…")
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
                            testTag = "student-row-" + student.key,
                        )
                    }
                }
            }
        }
    }
}

@Composable
internal fun StudentDetailScreen(
    student: StudentDirectoryItem,
    actorAppUserId: UUID?,
    focusState: FocusLearningUiState,
    onBack: () -> Unit,
    onLoadFocus: (PersonalTeachingContext) -> Unit,
    onQuickCapture: (PersonalTeachingContext) -> Unit,
) {
    BackHandler(onBack = onBack)
    var selectedFocusProfileId by rememberSaveable(student.key) {
        mutableStateOf<String?>(null)
    }

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
            Column {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = subjectLabelForKey(context.subjectKey),
                            style = MaterialTheme.typography.titleMedium,
                        )
                        Text(
                            text = "当前关注与课堂记录",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(top = 4.dp),
                        )
                    }
                    TextButton(
                        enabled = actorAppUserId != null,
                        onClick = {
                            selectedFocusProfileId = context.subjectProfileId.toString()
                            onLoadFocus(context)
                        },
                        modifier = Modifier.testTag(
                            "student-subject-focus-" + context.subjectProfileId,
                        ),
                    ) {
                        Text("关注")
                    }
                    TextButton(
                        onClick = { onQuickCapture(context) },
                        modifier = Modifier.testTag(
                            "student-subject-record-" + context.subjectProfileId,
                        ),
                    ) {
                        Text("记录")
                    }
                }

                if (selectedFocusProfileId == context.subjectProfileId.toString()) {
                    CurrentFocusInline(
                        state = focusState,
                        context = context,
                    )
                }

                HorizontalDivider()
            }
        }

        item {
            Text(
                text = "记录提交与当前关注读取都会由服务端重新检查当前任课关系。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 20.dp),
            )
        }
    }
}

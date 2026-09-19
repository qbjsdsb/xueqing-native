package com.xueqing.app.presentation.today

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import com.xueqing.app.application.learning.PersonalTodayAction
import com.xueqing.app.presentation.LearningReadStatus
import com.xueqing.app.presentation.StudentDirectoryItem
import com.xueqing.app.presentation.StudentDirectoryStatus
import com.xueqing.app.presentation.TodayLearningUiState
import com.xueqing.app.presentation.design.EmptyMessage
import com.xueqing.app.presentation.design.SectionTitle
import com.xueqing.app.presentation.design.StatusMessage
import com.xueqing.app.presentation.design.dueLabel
import com.xueqing.app.presentation.students.StudentRow
import com.xueqing.app.presentation.students.studentStorageKey
import com.xueqing.app.presentation.subjectLabelForKey

@Composable
internal fun TodayScreen(
    innerPadding: PaddingValues,
    students: List<StudentDirectoryItem>,
    directoryStatus: StudentDirectoryStatus,
    todayState: TodayLearningUiState,
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
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text("今日", style = MaterialTheme.typography.headlineSmall)
                TextButton(
                    onClick = onQuickCapture,
                    modifier = Modifier.testTag("today-quick-capture"),
                ) {
                    Text("记录")
                }
            }
        }

        item {
            SectionTitle(
                text = "待办行动",
                modifier = Modifier.padding(top = 20.dp),
            )
        }

        when (todayState.status) {
            LearningReadStatus.Idle,
            LearningReadStatus.Loading,
            -> item { StatusMessage("正在读取今日行动…") }

            LearningReadStatus.Empty -> item {
                EmptyMessage("当前没有待办教学行动。")
            }

            LearningReadStatus.Data -> {
                val actions = todayState.snapshot?.actions.orEmpty()
                items(actions, key = { it.actionId.toString() }) { action ->
                    TodayActionRow(
                        action = action,
                        onClick = {
                            onOpenStudent(
                                studentStorageKey(
                                    action.organizationId.toString(),
                                    action.studentId.toString(),
                                ),
                            )
                        },
                    )
                }
                if (todayState.snapshot?.hasMore == true) {
                    item { StatusMessage("仅显示前 200 条待办行动。") }
                }
            }

            LearningReadStatus.AuthenticationRequired -> item {
                EmptyMessage("请登录后查看今日行动。")
            }

            LearningReadStatus.AccessDenied -> item {
                EmptyMessage("当前教学权限已变化，未显示旧行动。")
            }

            LearningReadStatus.ServerInvariant -> item {
                EmptyMessage("行动数据需要服务器校验，暂不展示。")
            }

            LearningReadStatus.TransientFailure -> item {
                EmptyMessage("暂时无法刷新今日行动，可稍后再试。")
            }

            LearningReadStatus.ProtocolFailure -> item {
                EmptyMessage("服务器返回无法验证，已拒绝显示行动。")
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
                StatusMessage("正在读取学生…")
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
                            testTag = "today-student-row-" + student.key,
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun TodayActionRow(
    action: PersonalTodayAction,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(role = Role.Button, onClick = onClick)
            .testTag("today-action-" + action.actionId)
            .padding(vertical = 14.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.Top,
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .padding(end = 12.dp),
        ) {
            Text(
                text = action.actionText,
                style = MaterialTheme.typography.titleMedium,
            )
            Text(
                text = action.studentDisplayName + " · " + subjectLabelForKey(action.subjectKey),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp),
            )
            Text(
                text = action.caseTitle,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 3.dp),
            )
        }
        Text(
            text = dueLabel(action.dueBucket, action.dueOn),
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
    HorizontalDivider()
}

package com.xueqing.app.presentation.learning

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.xueqing.app.presentation.FocusLearningUiState
import com.xueqing.app.presentation.LearningReadStatus
import com.xueqing.app.presentation.design.SectionTitle
import com.xueqing.app.presentation.students.studentStorageKey
import com.xueqing.app.presentation.subjectLabelForKey

@Composable
internal fun LearningScreen(
    innerPadding: PaddingValues,
    focusState: FocusLearningUiState,
    onOpenStudent: (String) -> Unit,
    onOpenStudents: () -> Unit,
) {
    val snapshot = focusState.snapshot
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
        }

        if (focusState.status == LearningReadStatus.Data && snapshot != null) {
            item {
                SectionTitle(
                    text = "最近查看",
                    modifier = Modifier.padding(top = 24.dp),
                )
                Text(
                    text = snapshot.studentDisplayName + " · " +
                        subjectLabelForKey(snapshot.subjectKey),
                    style = MaterialTheme.typography.titleMedium,
                )
            }
            items(snapshot.cases, key = { it.caseId.toString() }) { learningCase ->
                CurrentFocusRow(learningCase)
                HorizontalDivider()
            }
            item {
                TextButton(
                    onClick = {
                        onOpenStudent(
                            studentStorageKey(
                                snapshot.organizationId.toString(),
                                snapshot.studentId.toString(),
                            ),
                        )
                    },
                    modifier = Modifier.padding(top = 8.dp),
                ) {
                    Text("查看学生")
                }
            }
        } else {
            item {
                Text(
                    text = "从学生进入具体学科，查看服务器确认的当前关注与下一步行动。",
                    style = MaterialTheme.typography.bodyLarge,
                    modifier = Modifier.padding(top = 24.dp),
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
}

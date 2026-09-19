package com.xueqing.app.presentation.learning

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import com.xueqing.app.application.learning.StudentLearningCaseFocus
import com.xueqing.app.presentation.FocusLearningUiState
import com.xueqing.app.presentation.LearningReadStatus
import com.xueqing.app.presentation.design.EmptyMessage
import com.xueqing.app.presentation.design.StatusMessage
import com.xueqing.app.presentation.design.caseStateLabel
import com.xueqing.app.presentation.design.dueLabel

@Composable
internal fun CurrentFocusInline(
    state: FocusLearningUiState,
    context: PersonalTeachingContext,
) {
    val matches = state.scope?.let { scope ->
        scope.organizationId == context.organizationId &&
            scope.studentId == context.studentId &&
            scope.subjectProfileId == context.subjectProfileId &&
            state.ownerAssignmentId == context.assignmentId
    } == true

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 8.dp, end = 8.dp, bottom = 12.dp),
    ) {
        Text(
            text = "当前关注",
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(bottom = 4.dp),
        )

        if (!matches || state.status == LearningReadStatus.Loading) {
            StatusMessage("正在读取当前关注…")
        } else {
            when (state.status) {
                LearningReadStatus.Empty ->
                    EmptyMessage("当前学科暂无持续跟进的问题。")

                LearningReadStatus.Data -> {
                    state.snapshot?.cases.orEmpty().forEach { learningCase ->
                        CurrentFocusRow(learningCase)
                    }
                    if (state.snapshot?.hasMore == true) {
                        StatusMessage("仅显示最近更新的 3 个关注项。")
                    }
                }

                LearningReadStatus.AuthenticationRequired ->
                    EmptyMessage("登录状态已失效，未显示旧关注项。")

                LearningReadStatus.AccessDenied ->
                    EmptyMessage("当前任课关系已变化，未显示旧关注项。")

                LearningReadStatus.ServerInvariant ->
                    EmptyMessage("当前学情数据需要服务器校验，暂不展示。")

                LearningReadStatus.TransientFailure ->
                    EmptyMessage("暂时无法刷新当前关注，可稍后再试。")

                LearningReadStatus.ProtocolFailure ->
                    EmptyMessage("服务器返回无法验证，已拒绝显示关注项。")

                LearningReadStatus.Idle,
                LearningReadStatus.Loading,
                -> StatusMessage("正在读取当前关注…")
            }
        }
    }
}

@Composable
internal fun CurrentFocusRow(
    learningCase: StudentLearningCaseFocus,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .testTag("current-focus-" + learningCase.caseId)
            .padding(vertical = 10.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = learningCase.title,
                style = MaterialTheme.typography.titleSmall,
                modifier = Modifier
                    .weight(1f)
                    .padding(end = 12.dp),
            )
            Text(
                text = caseStateLabel(learningCase.state),
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        Text(
            text = learningCase.primaryAction.actionText,
            style = MaterialTheme.typography.bodyMedium,
            modifier = Modifier.padding(top = 5.dp),
        )
        Text(
            text = dueLabel(
                learningCase.primaryAction.dueBucket,
                learningCase.primaryAction.dueOn,
            ),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(top = 3.dp),
        )
    }
}

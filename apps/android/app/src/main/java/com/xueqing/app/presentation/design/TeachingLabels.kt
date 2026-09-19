package com.xueqing.app.presentation.design

import com.xueqing.app.application.learning.ActionDueBucket
import com.xueqing.app.application.learning.LearningCaseState
import java.time.LocalDate

internal fun dueLabel(
    bucket: ActionDueBucket,
    dueOn: LocalDate?,
): String = when (bucket) {
    ActionDueBucket.Overdue ->
        dueOn?.let { "逾期 · " + it.monthValue + "月" + it.dayOfMonth + "日" } ?: "逾期"

    ActionDueBucket.Today -> "今天"
    ActionDueBucket.Undated -> "待安排"
    ActionDueBucket.Future ->
        dueOn?.let { it.monthValue.toString() + "月" + it.dayOfMonth + "日" } ?: "之后"
}

internal fun caseStateLabel(state: LearningCaseState): String = when (state) {
    LearningCaseState.New -> "新建"
    LearningCaseState.Confirmed -> "已确认"
    LearningCaseState.Intervening -> "跟进中"
    LearningCaseState.PendingVerification -> "待验证"
    LearningCaseState.Stable -> "稳定"
}

package com.xueqing.app.presentation.students

internal fun studentStorageKey(
    organizationId: String,
    studentId: String,
): String = organizationId + ":" + studentId

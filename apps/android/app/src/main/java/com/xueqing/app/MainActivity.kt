package com.xueqing.app

import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import com.xueqing.app.presentation.LearningReadViewModel
import com.xueqing.app.presentation.QuickCaptureViewModel
import com.xueqing.app.presentation.StudentDirectoryViewModel
import com.xueqing.app.presentation.XueqingApp

class MainActivity : ComponentActivity() {
    private val diagnosticsDocument = registerForActivityResult(
        ActivityResultContracts.CreateDocument("application/zip"),
    ) { uri ->
        if (uri == null) return@registerForActivityResult
        val result = runCatching {
            val output = requireNotNull(contentResolver.openOutputStream(uri, "wt")) {
                "Unable to open diagnostics destination."
            }
            output.use {
                BuildVariantRuntimeHooks.writeDiagnostics(applicationContext, it)
            }
        }
        Toast.makeText(
            this,
            if (result.isSuccess) "诊断信息已导出" else "诊断信息导出失败",
            Toast.LENGTH_SHORT,
        ).show()
    }

    private lateinit var quickCaptureViewModel: QuickCaptureViewModel
    private lateinit var studentDirectoryViewModel: StudentDirectoryViewModel
    private lateinit var learningReadViewModel: LearningReadViewModel

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        val bootstrapRemote = BuildVariantRuntimeHooks.bootstrapRemote(applicationContext)
        val sessionController = BuildVariantRuntimeHooks.sessionController(applicationContext)
        val factory = object : ViewModelProvider.Factory {
            override fun <T : ViewModel> create(modelClass: Class<T>): T {
                @Suppress("UNCHECKED_CAST")
                return when (modelClass) {
                    QuickCaptureViewModel::class.java ->
                        QuickCaptureViewModel.create(
                            context = applicationContext,
                            bootstrapRemote = bootstrapRemote,
                            environmentId = BuildVariantRuntimeHooks.environmentId(applicationContext),
                        ) as T

                    StudentDirectoryViewModel::class.java ->
                        StudentDirectoryViewModel(bootstrapRemote) as T

                    LearningReadViewModel::class.java ->
                        LearningReadViewModel(
                            todayRemote = BuildVariantRuntimeHooks.todayRemote(applicationContext),
                            focusRemote = BuildVariantRuntimeHooks.focusRemote(applicationContext),
                        ) as T

                    else -> error("Unsupported ViewModel: " + modelClass.name)
                }
            }
        }

        quickCaptureViewModel = ViewModelProvider(this, factory)[QuickCaptureViewModel::class.java]
        studentDirectoryViewModel = ViewModelProvider(this, factory)[StudentDirectoryViewModel::class.java]
        learningReadViewModel = ViewModelProvider(this, factory)[LearningReadViewModel::class.java]

        setContent {
            XueqingApp(
                quickCaptureViewModel = quickCaptureViewModel,
                studentDirectoryViewModel = studentDirectoryViewModel,
                learningReadViewModel = learningReadViewModel,
                startInQuickCapture = intent.getBooleanExtra(EXTRA_OPEN_QUICK_CAPTURE, false),
                sessionController = sessionController,
                onSessionBoundaryChanged = ::restartForSessionBoundary,
                onExportDiagnostics = {
                    diagnosticsDocument.launch("Xueqing-Diagnostics.zip")
                },
            )
        }
    }

    private fun restartForSessionBoundary() {
        startActivity(Intent(this, MainActivity::class.java))
        finish()
    }

    override fun onStop() {
        quickCaptureViewModel.flushNow()
        super.onStop()
    }

    companion object {
        const val EXTRA_OPEN_QUICK_CAPTURE = "com.xueqing.app.extra.OPEN_QUICK_CAPTURE"
    }
}

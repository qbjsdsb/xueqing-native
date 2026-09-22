package com.xueqing.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import com.xueqing.app.presentation.LearningReadViewModel
import com.xueqing.app.presentation.QuickCaptureViewModel
import com.xueqing.app.presentation.StudentDirectoryViewModel
import com.xueqing.app.presentation.XueqingApp

class MainActivity : ComponentActivity() {
    private lateinit var quickCaptureViewModel: QuickCaptureViewModel
    private lateinit var studentDirectoryViewModel: StudentDirectoryViewModel
    private lateinit var learningReadViewModel: LearningReadViewModel

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        val bootstrapRemote = BuildVariantRuntimeHooks.bootstrapRemote(applicationContext)
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
            )
        }
    }

    override fun onStop() {
        quickCaptureViewModel.flushNow()
        super.onStop()
    }

    companion object {
        const val EXTRA_OPEN_QUICK_CAPTURE = "com.xueqing.app.extra.OPEN_QUICK_CAPTURE"
    }
}

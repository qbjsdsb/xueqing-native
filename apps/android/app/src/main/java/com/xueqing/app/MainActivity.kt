package com.xueqing.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import com.xueqing.app.presentation.QuickCaptureViewModel
import com.xueqing.app.presentation.XueqingApp

class MainActivity : ComponentActivity() {
    private lateinit var quickCaptureViewModel: QuickCaptureViewModel

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        quickCaptureViewModel = ViewModelProvider(
            this,
            object : ViewModelProvider.Factory {
                override fun <T : ViewModel> create(modelClass: Class<T>): T {
                    require(modelClass == QuickCaptureViewModel::class.java)
                    @Suppress("UNCHECKED_CAST")
                    return QuickCaptureViewModel.create(
                        context = applicationContext,
                        bootstrapRemote = BuildVariantQuickCaptureBootstrap.remote(),
                        environmentId = BuildVariantQuickCaptureBootstrap.ENVIRONMENT_ID,
                    ) as T
                }
            },
        )[QuickCaptureViewModel::class.java]

        setContent {
            XueqingApp(
                quickCaptureViewModel = quickCaptureViewModel,
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

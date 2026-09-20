package com.xueqing.app.infrastructure.media

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.media.ExifInterface
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import java.io.ByteArrayInputStream
import java.io.File
import java.io.FileInputStream
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class ImageDerivativeFactoryInstrumentedTest {
    @Test
    fun derivativeAppliesOrientationStripsLocationMetadataAndResizes() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val sourceFile = File(context.cacheDir, "xueqing-derivative-source.jpg")
        val bitmap = Bitmap.createBitmap(1600, 800, Bitmap.Config.ARGB_8888)
        bitmap.eraseColor(Color.rgb(238, 238, 238))
        sourceFile.outputStream().use { output ->
            assertTrue(bitmap.compress(Bitmap.CompressFormat.JPEG, 96, output))
        }
        bitmap.recycle()

        ExifInterface(sourceFile.absolutePath).apply {
            setAttribute(
                ExifInterface.TAG_ORIENTATION,
                ExifInterface.ORIENTATION_ROTATE_90.toString(),
            )
            setLatLong(24.48, 118.08)
            saveAttributes()
        }

        val derivative = ImageDerivativeFactory(maxLongEdgePixels = 800).create(
            object : ImageDerivativeFactory.ReopenableSource {
                override fun open() = FileInputStream(sourceFile)
            },
        )

        assertEquals("image/jpeg", derivative.contentType)
        assertEquals(400, derivative.width)
        assertEquals(800, derivative.height)
        assertTrue(derivative.bytes.isNotEmpty())

        val decoded = BitmapFactory.decodeByteArray(
            derivative.bytes,
            0,
            derivative.bytes.size,
        )
        assertEquals(400, decoded.width)
        assertEquals(800, decoded.height)
        decoded.recycle()

        val derivativeExif = ExifInterface(ByteArrayInputStream(derivative.bytes))
        assertNull(derivativeExif.latLong)
        assertEquals(
            ExifInterface.ORIENTATION_NORMAL,
            derivativeExif.getAttributeInt(
                ExifInterface.TAG_ORIENTATION,
                ExifInterface.ORIENTATION_NORMAL,
            ),
        )

        sourceFile.delete()
    }
}

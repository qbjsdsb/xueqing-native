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
            setAttribute(ExifInterface.TAG_MAKE, "XUEQING_TEST_DEVICE")
            setAttribute(ExifInterface.TAG_MODEL, "XUEQING_TEST_MODEL")
            setAttribute(ExifInterface.TAG_GPS_LATITUDE_REF, "N")
            setAttribute(ExifInterface.TAG_GPS_LATITUDE, "24/1,28/1,4800/100")
            setAttribute(ExifInterface.TAG_GPS_LONGITUDE_REF, "E")
            setAttribute(ExifInterface.TAG_GPS_LONGITUDE, "118/1,4/1,4800/100")
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
        assertNull(derivativeExif.getAttribute(ExifInterface.TAG_MAKE))
        assertNull(derivativeExif.getAttribute(ExifInterface.TAG_MODEL))
        assertNull(derivativeExif.getAttribute(ExifInterface.TAG_GPS_LATITUDE))
        assertNull(derivativeExif.getAttribute(ExifInterface.TAG_GPS_LONGITUDE))
        // Re-encoding intentionally emits no source EXIF block at all. An
        // absent Orientation tag is stronger evidence than rewriting it to
        // ORIENTATION_NORMAL.
        assertNull(derivativeExif.getAttribute(ExifInterface.TAG_ORIENTATION))

        sourceFile.delete()
    }
}

package com.xueqing.app.infrastructure.media

import android.content.ContentResolver
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.media.ExifInterface
import android.net.Uri
import com.xueqing.app.durability.ProtectedAttachmentFileStore
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.InputStream
import kotlin.math.max
import kotlin.math.roundToInt

class ImageDerivativeFactory(
    private val maxLongEdgePixels: Int = DEFAULT_MAX_LONG_EDGE_PIXELS,
    private val maxEncodedBytes: Long = ProtectedAttachmentFileStore.MAX_STAGED_PLAINTEXT_BYTES,
) {
    data class Derivative(
        val bytes: ByteArray,
        val contentType: String,
        val width: Int,
        val height: Int,
    )

    interface ReopenableSource {
        fun open(): InputStream
    }

    class ContentResolverSource(
        private val contentResolver: ContentResolver,
        private val uri: Uri,
    ) : ReopenableSource {
        override fun open(): InputStream =
            requireNotNull(contentResolver.openInputStream(uri)) {
                "Unable to open selected image"
            }
    }

    init {
        require(maxLongEdgePixels >= MIN_LONG_EDGE_PIXELS)
        require(maxEncodedBytes > 0)
    }

    fun create(source: ReopenableSource): Derivative {
        val orientation = readOrientation(source)
        val bounds = readBounds(source)
        require(bounds.first > 0 && bounds.second > 0) { "Selected image has invalid dimensions" }

        val options = BitmapFactory.Options().apply {
            inSampleSize = calculateSampleSize(
                width = bounds.first,
                height = bounds.second,
            )
            inScaled = false
        }
        val decoded = source.open().use { input ->
            BitmapFactory.decodeStream(input, null, options)
        } ?: throw IOException("Selected image could not be decoded")

        var current = decoded
        try {
            val oriented = applyOrientation(current, orientation)
            if (oriented !== current) {
                current.recycle()
                current = oriented
            }

            val resized = resizeIfNeeded(current)
            if (resized !== current) {
                current.recycle()
                current = resized
            }

            return encode(current)
        } finally {
            current.recycle()
        }
    }

    private fun readOrientation(source: ReopenableSource): Int =
        runCatching {
            source.open().use { input ->
                ExifInterface(input).getAttributeInt(
                    ExifInterface.TAG_ORIENTATION,
                    ExifInterface.ORIENTATION_NORMAL,
                )
            }
        }.getOrDefault(ExifInterface.ORIENTATION_NORMAL)

    private fun readBounds(source: ReopenableSource): Pair<Int, Int> {
        val options = BitmapFactory.Options().apply {
            inJustDecodeBounds = true
        }
        source.open().use { input ->
            BitmapFactory.decodeStream(input, null, options)
        }
        return options.outWidth to options.outHeight
    }

    private fun calculateSampleSize(
        width: Int,
        height: Int,
    ): Int {
        var sample = 1
        val largest = max(width, height)
        while (largest / sample > maxLongEdgePixels * 2) {
            sample = Math.multiplyExact(sample, 2)
        }
        return sample
    }

    private fun applyOrientation(
        source: Bitmap,
        orientation: Int,
    ): Bitmap {
        val matrix = Matrix()
        when (orientation) {
            ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> matrix.setScale(-1f, 1f)
            ExifInterface.ORIENTATION_ROTATE_180 -> matrix.setRotate(180f)
            ExifInterface.ORIENTATION_FLIP_VERTICAL -> matrix.setScale(1f, -1f)
            ExifInterface.ORIENTATION_TRANSPOSE -> {
                matrix.setRotate(90f)
                matrix.postScale(-1f, 1f)
            }
            ExifInterface.ORIENTATION_ROTATE_90 -> matrix.setRotate(90f)
            ExifInterface.ORIENTATION_TRANSVERSE -> {
                matrix.setRotate(-90f)
                matrix.postScale(-1f, 1f)
            }
            ExifInterface.ORIENTATION_ROTATE_270 -> matrix.setRotate(-90f)
            else -> return source
        }
        return Bitmap.createBitmap(
            source,
            0,
            0,
            source.width,
            source.height,
            matrix,
            true,
        )
    }

    private fun resizeIfNeeded(source: Bitmap): Bitmap {
        val largest = max(source.width, source.height)
        if (largest <= maxLongEdgePixels) {
            return source
        }

        val scale = maxLongEdgePixels.toDouble() / largest.toDouble()
        val width = (source.width * scale).roundToInt().coerceAtLeast(1)
        val height = (source.height * scale).roundToInt().coerceAtLeast(1)
        return Bitmap.createScaledBitmap(source, width, height, true)
    }

    private fun encode(bitmap: Bitmap): Derivative {
        if (bitmap.hasAlpha()) {
            val png = compress(bitmap, Bitmap.CompressFormat.PNG, 100)
            if (png.size.toLong() <= maxEncodedBytes) {
                return Derivative(
                    bytes = png,
                    contentType = "image/png",
                    width = bitmap.width,
                    height = bitmap.height,
                )
            }
        }

        val opaque = if (bitmap.hasAlpha()) {
            Bitmap.createBitmap(bitmap.width, bitmap.height, Bitmap.Config.ARGB_8888).also { flattened ->
                Canvas(flattened).apply {
                    drawColor(Color.WHITE)
                    drawBitmap(bitmap, 0f, 0f, null)
                }
            }
        } else {
            bitmap
        }

        try {
            for (quality in JPEG_QUALITY_STEPS) {
                val jpeg = compress(opaque, Bitmap.CompressFormat.JPEG, quality)
                if (jpeg.size.toLong() <= maxEncodedBytes) {
                    return Derivative(
                        bytes = jpeg,
                        contentType = "image/jpeg",
                        width = opaque.width,
                        height = opaque.height,
                    )
                }
            }
        } finally {
            if (opaque !== bitmap) {
                opaque.recycle()
            }
        }

        throw ImageDerivativeTooLargeException(maxEncodedBytes)
    }

    private fun compress(
        bitmap: Bitmap,
        format: Bitmap.CompressFormat,
        quality: Int,
    ): ByteArray {
        val output = ByteArrayOutputStream()
        if (!bitmap.compress(format, quality, output)) {
            throw IOException("Unable to encode selected image derivative")
        }
        return output.toByteArray()
    }

    companion object {
        const val DEFAULT_MAX_LONG_EDGE_PIXELS = 2560
        private const val MIN_LONG_EDGE_PIXELS = 640
        private val JPEG_QUALITY_STEPS = intArrayOf(92, 88, 84, 80, 76)
    }
}

class ImageDerivativeTooLargeException(
    val maxBytes: Long,
) : IOException("Image derivative exceeds attachment staging limit")

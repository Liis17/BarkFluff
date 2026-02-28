package com.barkfluff.client.utils

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.util.Log
import java.io.ByteArrayOutputStream

/**
 * Утилита для сжатия изображений перед загрузкой на сервер.
 * Сжимает изображение только если одна из осей превышает 2500 пикселей.
 * Качество JPEG: 80%
 */
object ImageCompressor {

    private const val TAG = "ImageCompressor"
    private const val MAX_DIMENSION = 2500
    private const val JPEG_QUALITY = 80

    /**
     * Сжимает изображение из Uri до заданных размеров и качества.
     * @param uri Uri изображения
     * @param context Context для доступа к контенту
     * @return ByteArray со сжатым JPEG изображением
     */
    fun compressImage(uri: Uri, context: Context): Result<ByteArray> {
        return try {
            // Читаем исходное изображение
            val inputStream = context.contentResolver.openInputStream(uri)
                ?: return Result.failure(Exception("Не удалось открыть поток для Uri"))

            val options = BitmapFactory.Options().apply {
                inJustDecodeBounds = true
            }

            // Получаем размеры изображения
            BitmapFactory.decodeStream(inputStream, null, options)
            inputStream.close()

            val originalWidth = options.outWidth
            val originalHeight = options.outHeight

            Log.d(TAG, "Original image size: ${originalWidth}x${originalHeight}")

            // Вычисляем масштаб для сжатия, если нужно
            val sampleSize = calculateSampleSize(originalWidth, originalHeight)

            Log.d(TAG, "Using sampleSize: $sampleSize")

            // Читаем изображение с уменьшением
            val decodeOptions = BitmapFactory.Options().apply {
                inSampleSize = sampleSize
            }

            val decodeStream = context.contentResolver.openInputStream(uri)
            val bitmap = BitmapFactory.decodeStream(decodeStream, null, decodeOptions)
            decodeStream?.close()

            if (bitmap == null) {
                return Result.failure(Exception("Не удалось декодировать изображение"))
            }

            // Сжимаем в JPEG
            val outputStream = ByteArrayOutputStream()
            bitmap.compress(Bitmap.CompressFormat.JPEG, JPEG_QUALITY, outputStream)
            val compressedBytes = outputStream.toByteArray()
            outputStream.close()
            bitmap.recycle()

            Log.d(TAG, "Compressed image size: ${compressedBytes.size / 1024} KB")
            Result.success(compressedBytes)
        } catch (e: Exception) {
            Log.e(TAG, "Error compressing image", e)
            Result.failure(Exception("Ошибка сжатия изображения: ${e.message}"))
        }
    }

    /**
     * Вычисляет коэффициент уменьшения (sampleSize) для изображения.
     * Возвращает 1, если изображение не требует уменьшения.
     */
    private fun calculateSampleSize(width: Int, height: Int): Int {
        // Если обе стороны меньше или равны MAX_DIMENSION, не уменьшаем
        if (width <= MAX_DIMENSION && height <= MAX_DIMENSION) {
            return 1
        }

        // Вычисляем коэффициент уменьшения
        val scale = maxOf(width, height).toFloat() / MAX_DIMENSION
        var sampleSize = scale.toInt()

        // Округляем до ближайшей степени двойки (требование BitmapFactory)
        if (sampleSize < 1) {
            sampleSize = 1
        } else {
            // Находим ближайшую степень двойки
            var powerOfTwo = 1
            while (powerOfTwo * 2 <= sampleSize) {
                powerOfTwo *= 2
            }
            sampleSize = powerOfTwo
        }

        Log.d(TAG, "Calculated sampleSize: $sampleSize for ${width}x${height}")
        return sampleSize
    }

    /**
     * Сжимает изображение из ByteArray.
     * @param imageBytes Исходное изображение в виде ByteArray
     * @return ByteArray со сжатым JPEG изображением
     */
    fun compressImage(imageBytes: ByteArray): Result<ByteArray> {
        return try {
            val options = BitmapFactory.Options().apply {
                inJustDecodeBounds = true
            }

            // Получаем размеры
            BitmapFactory.decodeByteArray(imageBytes, 0, imageBytes.size, options)
            val originalWidth = options.outWidth
            val originalHeight = options.outHeight

            Log.d(TAG, "Original image size: ${originalWidth}x${originalHeight}")

            val sampleSize = calculateSampleSize(originalWidth, originalHeight)

            // Декодируем с уменьшением
            val decodeOptions = BitmapFactory.Options().apply {
                inSampleSize = sampleSize
            }

            val bitmap = BitmapFactory.decodeByteArray(imageBytes, 0, imageBytes.size, decodeOptions)

            if (bitmap == null) {
                return Result.failure(Exception("Не удалось декодировать изображение"))
            }

            // Сжимаем в JPEG
            val outputStream = ByteArrayOutputStream()
            bitmap.compress(Bitmap.CompressFormat.JPEG, JPEG_QUALITY, outputStream)
            val compressedBytes = outputStream.toByteArray()
            outputStream.close()
            bitmap.recycle()

            Log.d(TAG, "Compressed image size: ${compressedBytes.size / 1024} KB")
            Result.success(compressedBytes)
        } catch (e: Exception) {
            Log.e(TAG, "Error compressing image from bytes", e)
            Result.failure(Exception("Ошибка сжатия изображения: ${e.message}"))
        }
    }

    /**
     * Получает размеры изображения без полной загрузки в память.
     */
    fun getImageDimensions(uri: Uri, context: Context): Pair<Int, Int>? {
        return try {
            val inputStream = context.contentResolver.openInputStream(uri)
                ?: return null

            val options = BitmapFactory.Options().apply {
                inJustDecodeBounds = true
            }

            BitmapFactory.decodeStream(inputStream, null, options)
            inputStream.close()

            Pair(options.outWidth, options.outHeight)
        } catch (e: Exception) {
            Log.e(TAG, "Error getting image dimensions", e)
            null
        }
    }
}

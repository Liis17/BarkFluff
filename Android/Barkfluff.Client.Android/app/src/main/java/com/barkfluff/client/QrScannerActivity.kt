package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.camera.core.*
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.databinding.ActivityQrScannerBinding
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import kotlinx.coroutines.launch
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

class QrScannerActivity : AppCompatActivity() {

    private lateinit var binding: ActivityQrScannerBinding
    private lateinit var grpcManager: com.barkfluff.client.grpc.GrpcManager
    private lateinit var cameraExecutor: ExecutorService

    private val isProcessing = AtomicBoolean(false)

    companion object {
        private const val TAG = "QrScannerActivity"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityQrScannerBinding.inflate(layoutInflater)
        setContentView(binding.root)

        grpcManager = (application as BarkFluffApplication).grpcManager
        cameraExecutor = Executors.newSingleThreadExecutor()

        binding.buttonClose.setOnClickListener { finish() }

        startCamera()
    }

    private fun startCamera() {
        val cameraProviderFuture = ProcessCameraProvider.getInstance(this)
        cameraProviderFuture.addListener({
            val cameraProvider = cameraProviderFuture.get()

            val preview = Preview.Builder().build().also {
                it.surfaceProvider = binding.previewView.surfaceProvider
            }

            val options = BarcodeScannerOptions.Builder()
                .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
                .build()
            val scanner = BarcodeScanning.getClient(options)

            val imageAnalyzer = ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
                .also { analysis ->
                    analysis.setAnalyzer(cameraExecutor) { imageProxy ->
                        processImageProxy(scanner, imageProxy)
                    }
                }

            try {
                cameraProvider.unbindAll()
                cameraProvider.bindToLifecycle(
                    this,
                    CameraSelector.DEFAULT_BACK_CAMERA,
                    preview,
                    imageAnalyzer
                )
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка запуска камеры", e)
                Toast.makeText(this, "Ошибка запуска камеры", Toast.LENGTH_SHORT).show()
                finish()
            }
        }, ContextCompat.getMainExecutor(this))
    }

    @androidx.camera.core.ExperimentalGetImage
    private fun processImageProxy(
        scanner: com.google.mlkit.vision.barcode.BarcodeScanner,
        imageProxy: ImageProxy
    ) {
        val mediaImage = imageProxy.image
        if (mediaImage == null) {
            imageProxy.close()
            return
        }
        if (!isProcessing.compareAndSet(false, true)) {
            imageProxy.close()
            return
        }

        val image = InputImage.fromMediaImage(mediaImage, imageProxy.imageInfo.rotationDegrees)
        scanner.process(image)
            .addOnSuccessListener { barcodes ->
                val qrValue = barcodes.firstOrNull()?.rawValue
                if (!qrValue.isNullOrBlank()) {
                    runOnUiThread { onQrDetected(qrValue) }
                } else {
                    isProcessing.set(false)
                }
            }
            .addOnFailureListener {
                isProcessing.set(false)
            }
            .addOnCompleteListener {
                imageProxy.close()
            }
    }

    private fun onQrDetected(fastAuthId: String) {
        binding.progressScanning.visibility = View.VISIBLE
        binding.textScanHint.text = "Получение данных об устройстве…"

        lifecycleScope.launch {
            val result = grpcManager.scanFastAuth(fastAuthId)
            binding.progressScanning.visibility = View.GONE

            if (result.isSuccess) {
                val response = result.getOrNull()!!
                val intent = Intent(this@QrScannerActivity, FastAuthConfirmActivity::class.java).apply {
                    putExtra(FastAuthConfirmActivity.EXTRA_FAST_AUTH_ID, fastAuthId)
                    putExtra(FastAuthConfirmActivity.EXTRA_CONFIRMATION_CODE, response.confirmationCode)
                    putExtra(FastAuthConfirmActivity.EXTRA_DEVICE_NAME, response.deviceName)
                    putExtra(FastAuthConfirmActivity.EXTRA_OS, response.operationSystem)
                    putExtra(FastAuthConfirmActivity.EXTRA_APP_NAME, response.appName)
                    putExtra(FastAuthConfirmActivity.EXTRA_APP_VERSION, response.appVersion)
                    putExtra(FastAuthConfirmActivity.EXTRA_IP, response.ipAddress)
                }
                startActivity(intent)
                finish()
            } else {
                val msg = result.exceptionOrNull()?.message ?: "Неизвестная ошибка"
                Log.e(TAG, "Ошибка scanFastAuth: $msg")
                Toast.makeText(this@QrScannerActivity, "Ошибка: $msg", Toast.LENGTH_LONG).show()
                binding.textScanHint.text = "Наведите камеру на QR-код"
                isProcessing.set(false)
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        cameraExecutor.shutdown()
    }
}

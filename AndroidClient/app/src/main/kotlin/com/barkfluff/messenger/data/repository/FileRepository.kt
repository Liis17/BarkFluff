package com.barkfluff.messenger.data.repository

import android.content.Context
import android.net.Uri
import dagger.hilt.android.qualifiers.ApplicationContext
import io.grpc.ManagedChannel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.asRequestBody
import java.io.File
import javax.inject.Inject
import javax.inject.Named

data class UploadResult(
    val fileId: String,
    val fileUrl: String,
    val previewUrl: String?
)

class FileRepository @Inject constructor(
    @Named("filesChannel") private val filesChannel: ManagedChannel,
    @ApplicationContext private val context: Context
) {

    private val httpClient = OkHttpClient()

    suspend fun uploadFile(uri: Uri, fileType: String = "MESSAGE_ATTACHMENT_IMAGE"): Result<UploadResult> =
        withContext(Dispatchers.IO) {
            try {
                // Step 1: Get upload URL from gRPC
                val stub = files_api.FilesApiGrpc.newBlockingStub(filesChannel)

                val uploadRequest = files_api.FilesApi.GetUploadUrlRequest.newBuilder()
                    .setType(files_api.FilesApi.UploadFileType.valueOf(fileType))
                    .build()

                val uploadResponse = stub.getUploadUrl(uploadRequest)

                // Step 2: Upload file to the URL
                val file = uriToFile(uri)
                val requestBody = MultipartBody.Builder()
                    .setType(MultipartBody.FORM)
                    .addFormDataPart(
                        "file",
                        file.name,
                        file.asRequestBody("application/octet-stream".toMediaTypeOrNull())
                    )
                    .build()

                val request = Request.Builder()
                    .url(uploadResponse.url)
                    .post(requestBody)
                    .build()

                httpClient.newCall(request).execute().use { response ->
                    if (response.isSuccessful) {
                        Result.success(
                            UploadResult(
                                fileId = uploadResponse.fileId,
                                fileUrl = uploadResponse.url,
                                previewUrl = null
                            )
                        )
                    } else {
                        Result.failure(Exception("Upload failed: ${response.message}"))
                    }
                }
            } catch (e: Exception) {
                Result.failure(e)
            }
        }

    suspend fun getDownloadUrls(fileIds: List<String>): Result<Map<String, String>> =
        withContext(Dispatchers.IO) {
            try {
                val stub = files_api.FilesApiGrpc.newBlockingStub(filesChannel)

                val request = files_api.FilesApi.GetTempDownloadUrlRequest.newBuilder()
                    .addAllFileIds(fileIds)
                    .build()

                val response = stub.getTempDownloadUrl(request)

                val urls = response.filesList.associate { file ->
                    file.fileId to file.url
                }

                Result.success(urls)
            } catch (e: Exception) {
                Result.failure(e)
            }
        }

    private fun uriToFile(uri: Uri): File {
        val inputStream = context.contentResolver.openInputStream(uri)
            ?: throw IllegalArgumentException("Cannot open file")

        val tempFile = File.createTempFile("upload", null, context.cacheDir)
        tempFile.outputStream().use { output ->
            inputStream.copyTo(output)
        }

        return tempFile
    }
}

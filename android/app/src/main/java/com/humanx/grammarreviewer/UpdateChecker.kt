package com.humanx.grammarreviewer

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/** Versión instalada de esta app. Debe coincidir con versionName en build.gradle.kts. */
const val APP_VERSION = "1.2.0"

data class UpdateInfo(val version: String, val url: String, val notes: String)

object UpdateChecker {
    /** Archivo publicado en GitHub Pages; lo regenera el workflow de releases. */
    private const val MANIFEST_URL = "https://humanx123.github.io/grammar-reviewer/version.json"

    /** Devuelve la actualización si hay una versión más nueva; null si no hay o no se pudo consultar. */
    suspend fun check(): UpdateInfo? = withContext(Dispatchers.IO) {
        try {
            val conn = (URL(MANIFEST_URL).openConnection() as HttpURLConnection).apply {
                connectTimeout = 8_000
                readTimeout = 8_000
            }
            val body = try {
                if (conn.responseCode !in 200..299) return@withContext null
                conn.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
            } finally {
                conn.disconnect()
            }
            val android = JSONObject(body).getJSONObject("android")
            val latest = android.getString("version")
            if (isNewer(latest, APP_VERSION)) {
                UpdateInfo(latest, android.getString("url"), android.optString("notes"))
            } else {
                null
            }
        } catch (e: Exception) {
            null
        }
    }

    internal fun isNewer(candidate: String, current: String): Boolean {
        val a = candidate.split('.').map { it.toIntOrNull() ?: 0 }
        val b = current.split('.').map { it.toIntOrNull() ?: 0 }
        for (i in 0 until maxOf(a.size, b.size)) {
            val x = a.getOrElse(i) { 0 }
            val y = b.getOrElse(i) { 0 }
            if (x != y) return x > y
        }
        return false
    }
}

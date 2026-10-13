package com.humanx.grammarreviewer

import org.json.JSONArray
import org.json.JSONObject
import java.io.OutputStreamWriter
import java.net.HttpURLConnection
import java.net.URL

class LlmException(message: String) : Exception(message)

data class Change(
    val original: String,
    val replacement: String,
    val reason: String,
    var selected: Boolean = true,
)

data class Result(val corrected: String, val changes: List<Change>)

data class Keys(val groq: String = "", val gemini: String = "", val nvidia: String = "")

enum class Mode(val label: String) {
    Correct("Corregir"),
    Scientific("Científico"),
    Formal("Formal"),
    Rewrite("Reescribir"),
    Concise("Conciso"),
    Casual("Casual"),
    Translate("Traducir"),
}

private const val CONTRACT =
    "Respond only with a JSON object of this exact shape: " +
        "{\"corrected\": string, \"changes\": [{\"original\": string, \"replacement\": string, \"reason\": string}]}. " +
        "\"corrected\" is the full resulting text. \"changes\" lists each edit with a short reason in the same language as the text. " +
        "If the text needs no change, return it unchanged with an empty changes array. Do not add commentary."

object Llm {
    /** Modos que el usuario puede elegir; el idioma lo decide el interruptor ES/EN. */
    fun system(mode: Mode, english: Boolean): String {
        val lang = if (english) "The source text is in English; keep the output in English."
        else "El texto de origen está en español; mantén la salida en español."
        return when (mode) {
            Mode.Correct -> "Fix grammar, spelling, punctuation and clarity. Keep the author's meaning, tone and language. Make the smallest changes that make the text correct. $lang"
            Mode.Scientific -> "Correct grammar and improve the scientific style for a peer-reviewed article in English: formal, precise, concise. " +
                "Hedge claims the text does not support. Never alter numbers, statistics, names, abbreviations or citations. Make only needed edits. $lang"
            Mode.Formal -> "Rewrite the text in a formal, professional register keeping the same meaning and language. $lang"
            Mode.Rewrite -> "Rewrite the text to read naturally and clearly keeping the same meaning and language. $lang"
            Mode.Concise -> "Shorten the text without losing meaning, keeping the same language. $lang"
            Mode.Casual -> "Rewrite the text in a relaxed, friendly register keeping the same meaning and language. $lang"
            Mode.Translate -> {
                val (source, target) = if (english) "English" to "Spanish" else "Spanish" to "English"
                "Produce an interpretive translation: convey the meaning, intent and tone, not a word-for-word rendering. " +
                    "Rephrase idioms with natural equivalents. Keep names, numbers and technical terms exact. " +
                    "The source text is in $source. Translate it into $target. In \"corrected\" put only the translation. " +
                    "In \"changes\" list notable renderings: original is the source phrase, replacement the rendering, reason a short note in $target."
            }
        }
    }

    /** Revisión o traducción: prueba los proveedores en orden y usa el primero que responda. */
    suspend fun review(text: String, mode: Mode, english: Boolean, keys: Keys): Result {
        if (text.isBlank()) throw LlmException("No hay texto para revisar.")
        val system = system(mode, english) + " " + CONTRACT
        var lastError: LlmException? = null
        for (provider in providers(keys)) {
            try {
                val raw = provider.complete(system, listOf("user" to text), json = true)
                return parseResult(raw)
            } catch (e: LlmException) {
                lastError = e
            }
        }
        throw lastError ?: LlmException("Ningún modelo respondió.")
    }

    /** Chat libre con los modelos. */
    suspend fun chat(turns: List<Pair<String, String>>, keys: Keys): String {
        val system = "You are Grammar Reviewer, a bilingual (Spanish and English) writing assistant. " +
            "You help with grammar, style, interpretive translation and explaining language. " +
            "Answer in the same language the user writes in. Keep answers concise and practical."
        var lastError: LlmException? = null
        for (provider in providers(keys)) {
            try {
                return provider.complete(system, turns, json = false).trim()
            } catch (e: LlmException) {
                lastError = e
            }
        }
        throw lastError ?: LlmException("Ningún modelo respondió.")
    }

    /** Aplica solo los cambios seleccionados, en orden, sobre el texto original. */
    fun apply(original: String, changes: List<Change>): String {
        val positions = IntArray(changes.size) { -1 }
        var cursor = 0
        changes.forEachIndexed { i, c ->
            if (c.original.isNotEmpty()) {
                val pos = original.indexOf(c.original, cursor)
                if (pos >= 0) {
                    positions[i] = pos
                    cursor = pos + c.original.length
                }
            }
        }
        val out = StringBuilder()
        var last = 0
        changes.forEachIndexed { i, c ->
            if (positions[i] >= 0 && c.selected) {
                out.append(original, last, positions[i])
                out.append(c.replacement)
                last = positions[i] + c.original.length
            }
        }
        out.append(original, last, original.length)
        return out.toString()
    }

    private fun parseResult(raw: String): Result {
        val start = raw.indexOf('{')
        val end = raw.lastIndexOf('}')
        if (start < 0 || end <= start) throw LlmException("El modelo devolvió una respuesta sin formato.")
        val obj = try {
            JSONObject(raw.substring(start, end + 1))
        } catch (e: Exception) {
            throw LlmException("El modelo devolvió JSON inválido.")
        }
        val corrected = obj.optString("corrected", "")
        if (corrected.isEmpty()) throw LlmException("El modelo devolvió un resultado vacío.")
        val list = mutableListOf<Change>()
        val arr: JSONArray = obj.optJSONArray("changes") ?: JSONArray()
        for (i in 0 until arr.length()) {
            val c = arr.getJSONObject(i)
            list.add(Change(c.optString("original"), c.optString("replacement"), c.optString("reason")))
        }
        return Result(corrected, list)
    }

    private fun providers(keys: Keys): List<Provider> {
        val list = mutableListOf<Provider>()
        if (keys.gemini.isNotBlank()) list.add(Provider.Gemini(keys.gemini))
        if (keys.groq.isNotBlank()) list.add(Provider.OpenAi("Groq", "https://api.groq.com/openai/v1", "openai/gpt-oss-120b", keys.groq))
        if (keys.nvidia.isNotBlank()) list.add(Provider.OpenAi("NVIDIA", "https://integrate.api.nvidia.com/v1", "meta/llama-3.3-70b-instruct", keys.nvidia))
        list.add(Provider.OpenAi("Kilo", "https://api.kilo.ai/api/gateway", "kilo-auto/free", null))
        list.add(Provider.OpenAi("LLM7", "https://api.llm7.io/v1", "gpt-oss:20b", null))
        return list
    }

    internal sealed class Provider {
        abstract suspend fun complete(system: String, turns: List<Pair<String, String>>, json: Boolean): String

        class Gemini(private val key: String) : Provider() {
            override suspend fun complete(system: String, turns: List<Pair<String, String>>, json: Boolean): String {
                val contents = JSONArray()
                for ((role, text) in turns) {
                    contents.put(JSONObject().put("role", if (role == "assistant") "model" else "user")
                        .put("parts", JSONArray().put(JSONObject().put("text", text))))
                }
                val body = JSONObject()
                    .put("systemInstruction", JSONObject().put("parts", JSONArray().put(JSONObject().put("text", system))))
                    .put("contents", contents)
                    .put("generationConfig", JSONObject().put("temperature", 0.2)
                        .put("responseMimeType", if (json) "application/json" else "text/plain"))
                val url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent"
                val raw = post(url, mapOf("x-goog-api-key" to key), body.toString(), "Gemini")
                val text = JSONObject(raw).optJSONArray("candidates")?.optJSONObject(0)
                    ?.optJSONObject("content")?.optJSONArray("parts")?.optJSONObject(0)?.optString("text")
                if (text.isNullOrBlank()) throw LlmException("Gemini devolvió una respuesta vacía.")
                return text
            }
        }

        class OpenAi(
            private val label: String,
            private val baseUrl: String,
            private val model: String,
            private val key: String?,
        ) : Provider() {
            override suspend fun complete(system: String, turns: List<Pair<String, String>>, json: Boolean): String {
                val messages = JSONArray().put(JSONObject().put("role", "system").put("content", system))
                for ((role, text) in turns) messages.put(JSONObject().put("role", role).put("content", text))
                val headers = if (key.isNullOrBlank()) emptyMap() else mapOf("Authorization" to "Bearer $key")
                fun body(withJson: Boolean): String {
                    val b = JSONObject().put("model", model).put("messages", messages).put("temperature", 0.3)
                    if (withJson && json) b.put("response_format", JSONObject().put("type", "json_object"))
                    return b.toString()
                }
                val raw = try {
                    post("$baseUrl/chat/completions", headers, body(true), label)
                } catch (e: LlmException) {
                    // Algunos respaldos rechazan el modo JSON: reintenta sin él.
                    if (json && e.message?.startsWith("BAD_REQUEST") == true) post("$baseUrl/chat/completions", headers, body(false), label)
                    else throw e
                }
                val text = JSONObject(raw).optJSONArray("choices")?.optJSONObject(0)
                    ?.optJSONObject("message")?.optString("content")
                if (text.isNullOrBlank()) throw LlmException("$label devolvió una respuesta vacía.")
                return text
            }
        }
    }

    private suspend fun post(url: String, headers: Map<String, String>, body: String, label: String): String =
        kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            val conn = (URL(url).openConnection() as HttpURLConnection).apply {
                requestMethod = "POST"
                connectTimeout = 20_000
                readTimeout = 60_000
                doOutput = true
                setRequestProperty("Content-Type", "application/json")
                headers.forEach { (k, v) -> setRequestProperty(k, v) }
            }
            try {
                OutputStreamWriter(conn.outputStream, Charsets.UTF_8).use { it.write(body) }
                val code = conn.responseCode
                val stream = if (code in 200..299) conn.inputStream else conn.errorStream
                val text = stream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() } ?: ""
                when (code) {
                    in 200..299 -> text
                    429 -> throw LlmException("$label: límite gratuito alcanzado.")
                    401, 403 -> throw LlmException("$label: clave no válida o sin acceso.")
                    400 -> throw LlmException("BAD_REQUEST $label")
                    else -> throw LlmException("$label respondió $code.")
                }
            } catch (e: java.io.IOException) {
                throw LlmException("$label: sin conexión o tiempo agotado.")
            } finally {
                conn.disconnect()
            }
        }
}

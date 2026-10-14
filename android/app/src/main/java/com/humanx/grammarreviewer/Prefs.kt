package com.humanx.grammarreviewer

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

/** Preferencias privadas de la app. Todo queda solo en este dispositivo. */
object Prefs {
    private const val FILE = "grammar_reviewer"

    private fun prefs(ctx: Context) = ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE)

    fun keys(ctx: Context): Keys {
        val p = prefs(ctx)
        val defaults = Keys()
        return Keys(
            groq = p.getString("groq", "") ?: "",
            gemini = p.getString("gemini", "") ?: "",
            nvidia = p.getString("nvidia", "") ?: "",
            groqModel = p.getString("groqModel", defaults.groqModel) ?: defaults.groqModel,
            geminiModel = p.getString("geminiModel", defaults.geminiModel) ?: defaults.geminiModel,
            nvidiaModel = p.getString("nvidiaModel", defaults.nvidiaModel) ?: defaults.nvidiaModel,
            custom = customProviders(ctx),
        )
    }

    fun saveKeys(ctx: Context, keys: Keys) {
        prefs(ctx).edit()
            .putString("groq", keys.groq.trim())
            .putString("gemini", keys.gemini.trim())
            .putString("nvidia", keys.nvidia.trim())
            .putString("groqModel", keys.groqModel.trim().ifBlank { Keys().groqModel })
            .putString("geminiModel", keys.geminiModel.trim().ifBlank { Keys().geminiModel })
            .putString("nvidiaModel", keys.nvidiaModel.trim().ifBlank { Keys().nvidiaModel })
            .apply()
    }

    fun customProviders(ctx: Context): List<CustomProvider> {
        val raw = prefs(ctx).getString("custom", "[]") ?: "[]"
        val arr = runCatching { JSONArray(raw) }.getOrElse { JSONArray() }
        return (0 until arr.length()).map { i ->
            val o = arr.getJSONObject(i)
            CustomProvider(o.optString("name"), o.optString("baseUrl"), o.optString("model"), o.optString("key"))
        }
    }

    fun saveCustomProviders(ctx: Context, list: List<CustomProvider>) {
        val arr = JSONArray()
        list.forEach { c ->
            arr.put(JSONObject().put("name", c.name).put("baseUrl", c.baseUrl).put("model", c.model).put("key", c.key))
        }
        prefs(ctx).edit().putString("custom", arr.toString()).apply()
    }

    fun english(ctx: Context): Boolean = prefs(ctx).getBoolean("english", false)

    fun saveEnglish(ctx: Context, english: Boolean) {
        prefs(ctx).edit().putBoolean("english", english).apply()
    }

    fun lastUpdateCheck(ctx: Context): Long = prefs(ctx).getLong("lastUpdateCheck", 0L)

    fun saveUpdateCheck(ctx: Context, millis: Long) {
        prefs(ctx).edit().putLong("lastUpdateCheck", millis).apply()
    }
}

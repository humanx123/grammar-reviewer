package com.humanx.grammarreviewer

import android.content.Context

/** Preferencias privadas de la app. Las claves quedan solo en este dispositivo. */
object Prefs {
    private const val FILE = "grammar_reviewer"

    fun keys(ctx: Context): Keys {
        val p = ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE)
        return Keys(
            groq = p.getString("groq", "") ?: "",
            gemini = p.getString("gemini", "") ?: "",
            nvidia = p.getString("nvidia", "") ?: "",
        )
    }

    fun saveKeys(ctx: Context, keys: Keys) {
        ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit()
            .putString("groq", keys.groq.trim())
            .putString("gemini", keys.gemini.trim())
            .putString("nvidia", keys.nvidia.trim())
            .apply()
    }

    fun english(ctx: Context): Boolean =
        ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).getBoolean("english", false)

    fun saveEnglish(ctx: Context, english: Boolean) {
        ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit().putBoolean("english", english).apply()
    }
}

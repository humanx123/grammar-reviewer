package com.humanx.grammarreviewer

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

class MainActivity : Activity() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private lateinit var statusView: TextView
    private lateinit var langButton: Button
    private lateinit var groqField: EditText
    private lateinit var geminiField: EditText
    private lateinit var nvidiaField: EditText
    private lateinit var input: EditText
    private lateinit var modeSpinner: Spinner
    private lateinit var resultView: TextView
    private lateinit var chatInput: EditText
    private lateinit var chatLog: TextView
    private var lastResult = ""
    private val chatTurns = mutableListOf<Pair<String, String>>()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(buildUi())
        refreshStatus()
        if (Build.VERSION.SDK_INT >= 33) {
            requestPermissions(arrayOf(android.Manifest.permission.POST_NOTIFICATIONS), 1)
        }
    }

    override fun onResume() {
        super.onResume()
        refreshStatus()
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }

    private fun buildUi(): ScrollView {
        val keys = Prefs.keys(this)
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(18), dp(18), dp(28))
        }

        root.addView(title("Grammar Reviewer"))
        root.addView(hint("Revisa y traduce cualquier texto sin cambiar de app: toca el globo flotante sobre el campo que estás escribiendo."))

        statusView = hint("")
        root.addView(statusView)
        root.addView(button("1. Permitir mostrar sobre otras apps") {
            startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName")))
        })
        root.addView(button("2. Activar el lector de texto (Accesibilidad)") {
            startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
        })
        root.addView(button("Mostrar globo") {
            if (!BubbleService.canDrawOverlays(this)) {
                toast("Primero activa el paso 1.")
            } else {
                BubbleService.start(this)
                toast("Globo activo. Ábrelo desde cualquier app.")
            }
        })
        root.addView(button("Ocultar globo") { BubbleService.stop(this) })

        root.addView(section("Idioma del texto"))
        langButton = button(languageLabel()) {
            Prefs.saveEnglish(this, !Prefs.english(this))
            langButton.text = languageLabel()
        }
        root.addView(langButton)

        root.addView(section("Claves de API (opcionales)"))
        root.addView(hint("Sin claves se usan los respaldos gratuitos. Se guardan solo en este teléfono."))
        groqField = secretField("Groq", keys.groq)
        geminiField = secretField("Gemini", keys.gemini)
        nvidiaField = secretField("NVIDIA", keys.nvidia)
        root.addView(button("Guardar claves") {
            Prefs.saveKeys(this, Keys(groqField.text.toString(), geminiField.text.toString(), nvidiaField.text.toString()))
            toast("Claves guardadas.")
        })

        root.addView(section("Revisar o traducir texto"))
        input = EditText(this).apply {
            hint = "Escribe o pega el texto aquí"
            minLines = 4
            gravity = Gravity.TOP
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE
        }
        root.addView(input)
        modeSpinner = Spinner(this).apply {
            adapter = ArrayAdapter(this@MainActivity, android.R.layout.simple_spinner_dropdown_item, Mode.entries.map { it.label })
        }
        root.addView(modeSpinner)
        root.addView(button("Revisar") { runReview() })
        resultView = hint("El resultado aparecerá aquí.").apply { setTextColor(Color.parseColor("#1D2230")) }
        root.addView(resultView)
        root.addView(button("Copiar resultado") {
            if (lastResult.isBlank()) return@button
            val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cm.setPrimaryClip(ClipData.newPlainText("Grammar Reviewer", lastResult))
            toast("Copiado.")
        })

        root.addView(section("Chat con los modelos"))
        chatLog = hint("")
        root.addView(chatLog)
        chatInput = EditText(this).apply { hint = "Pregunta sobre gramática, estilo o traducción" }
        root.addView(chatInput)
        root.addView(button("Enviar") { sendChat() })

        return ScrollView(this).apply { addView(root) }
    }

    private fun runReview() {
        val text = input.text.toString()
        if (text.isBlank()) {
            toast("Escribe un texto primero.")
            return
        }
        val mode = Mode.entries[modeSpinner.selectedItemPosition]
        resultView.text = "Revisando…"
        scope.launch {
            try {
                val result = Llm.review(text, mode, Prefs.english(this@MainActivity), currentKeys())
                lastResult = if (mode == Mode.Translate) result.corrected else buildString {
                    append(result.corrected)
                    if (result.changes.isNotEmpty()) {
                        append("\n\nCambios:\n")
                        result.changes.forEach { append("• ${it.original} → ${it.replacement}  (${it.reason})\n") }
                    }
                }.trimEnd()
                resultView.text = lastResult
            } catch (e: LlmException) {
                resultView.text = e.message
            }
        }
    }

    private fun sendChat() {
        val question = chatInput.text.toString().trim()
        if (question.isEmpty()) return
        chatTurns.add("user" to question)
        chatInput.setText("")
        chatLog.text = "Tú: $question\nPensando…"
        scope.launch {
            try {
                val reply = Llm.chat(chatTurns.toList(), currentKeys())
                chatTurns.add("assistant" to reply)
                chatLog.text = chatTurns.joinToString("\n\n") { (role, text) -> (if (role == "user") "Tú: " else "IA: ") + text }
            } catch (e: LlmException) {
                chatTurns.removeAt(chatTurns.lastIndex)
                chatLog.text = e.message
            }
        }
    }

    private fun currentKeys(): Keys = Keys(
        groqField.text.toString().trim(),
        geminiField.text.toString().trim(),
        nvidiaField.text.toString().trim(),
    )

    private fun refreshStatus() {
        val overlay = if (BubbleService.canDrawOverlays(this)) "Superposición: lista" else "Superposición: falta permiso"
        val reader = if (TextAccessibilityService.instance != null) "Lector de texto: activo" else "Lector de texto: inactivo"
        statusView.text = "$overlay · $reader"
    }

    private fun languageLabel() = if (Prefs.english(this)) "Idioma del texto: English" else "Idioma del texto: Español"

    private fun secretField(label: String, value: String): EditText = EditText(this).apply {
        hint = "$label (clave)"
        setText(value)
        inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
    }

    private fun title(text: String) = TextView(this).apply {
        this.text = text
        textSize = 24f
        setTextColor(Color.parseColor("#1D2230"))
        setPadding(0, 0, 0, dp(6))
    }

    private fun section(text: String) = TextView(this).apply {
        this.text = text
        textSize = 16f
        setTextColor(Color.parseColor("#2C3E6B"))
        setPadding(0, dp(18), 0, dp(6))
    }

    private fun hint(text: String) = TextView(this).apply {
        this.text = text
        textSize = 13f
        setTextColor(Color.parseColor("#5B6171"))
        setPadding(0, dp(4), 0, dp(4))
        visibility = if (text.isEmpty()) View.GONE else View.VISIBLE
    }

    private fun button(label: String, onClick: () -> Unit) = Button(this).apply {
        text = label
        setOnClickListener { onClick() }
    }

    private fun toast(text: String) = Toast.makeText(this, text, Toast.LENGTH_SHORT).show()

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()
}

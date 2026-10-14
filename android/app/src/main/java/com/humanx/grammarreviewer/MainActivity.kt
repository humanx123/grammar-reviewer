package com.humanx.grammarreviewer

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

class MainActivity : Activity() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private val chatTurns = mutableListOf<Pair<String, String>>()
    private var selectedMode: Mode = Mode.Correct
    private val modeChips = mutableMapOf<Mode, TextView>()

    private lateinit var statusLine: TextView
    private lateinit var langChip: TextView
    private lateinit var input: EditText
    private lateinit var resultCard: TextView
    private lateinit var resultActions: LinearLayout
    private lateinit var chatLog: LinearLayout
    private lateinit var chatInput: EditText
    private lateinit var updateBanner: LinearLayout
    private var lastResult = ""

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(buildUi())
        if (Build.VERSION.SDK_INT >= 33) {
            requestPermissions(arrayOf(android.Manifest.permission.POST_NOTIFICATIONS), 1)
        }
        checkForUpdate()
    }

    override fun onResume() {
        super.onResume()
        refreshStatus()
        langChip.text = languageLabel()
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }

    private fun buildUi(): View {
        val page = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(16), dp(16), dp(32))
            background = gradient("#0F1424", "#1A2340", GradientDrawable.Orientation.TOP_BOTTOM)
        }

        page.addView(header())

        updateBanner = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            visibility = View.GONE
            background = roundedFill("#33C9B98A", "#C9B98A", 16)
            setPadding(dp(14), dp(12), dp(14), dp(12))
        }
        page.addView(updateBanner, cardParams(top = 14))

        page.addView(card().apply {
            addView(sectionTitle("Activar el globo"))
            addView(hint("Dos permisos y listo. El círculo se queda a un lado de la pantalla sobre cualquier app."))
            statusLine = hint("")
            addView(statusLine)
            addView(row(
                pill("Permiso de superposición") { startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName"))) },
                pill("Lector de texto") { startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)) },
            ))
            addView(row(
                pill("Mostrar globo", primary = true) {
                    if (!BubbleService.canDrawOverlays(this@MainActivity)) toast("Primero activa el permiso de superposición.")
                    else { BubbleService.start(this@MainActivity); toast("Globo activo.") }
                },
                pill("Ocultar") { BubbleService.stop(this@MainActivity) },
            ))
        }, cardParams(top = 14))

        page.addView(card().apply {
            addView(sectionTitle("Revisar o traducir"))
            addView(modeRow())
            input = EditText(this@MainActivity).apply {
                hint = "Escribe o pega el texto aquí…"
                setTextColor(Color.WHITE)
                setHintTextColor(Color.parseColor("#7F8AA8"))
                minLines = 4
                gravity = Gravity.TOP
                inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE
                background = roundedFill("#14FFFFFF", "#33C9B98A", 14)
                setPadding(dp(12), dp(10), dp(12), dp(10))
            }
            addView(input, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(10) })
            addView(row(
                pill("Ejecutar", primary = true) { runReview() },
                pill("Limpiar") { input.setText(""); resultCard.text = "" ; resultActions.visibility = View.GONE },
            ))
            resultCard = TextView(this@MainActivity).apply {
                setTextColor(Color.WHITE)
                textSize = 15f
                setLineSpacing(0f, 1.3f)
                background = roundedFill("#1AFFFFFF", "#22C9B98A", 14)
                setPadding(dp(14), dp(12), dp(14), dp(12))
                text = ""
                visibility = View.GONE
            }
            addView(resultCard, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(12) })
            resultActions = row(
                pill("Copiar") { copyLast() },
                pill("Pegar en el campo") { pasteLast() },
            ).apply { visibility = View.GONE }
            addView(resultActions)
        }, cardParams(top = 14))

        page.addView(card().apply {
            addView(sectionTitle("Chat con los modelos"))
            chatLog = LinearLayout(this@MainActivity).apply { orientation = LinearLayout.VERTICAL }
            addView(chatLog)
            chatInput = EditText(this@MainActivity).apply {
                hint = "Pregunta sobre gramática, estilo o traducción…"
                setTextColor(Color.WHITE)
                setHintTextColor(Color.parseColor("#7F8AA8"))
                background = roundedFill("#14FFFFFF", "#33C9B98A", 14)
                setPadding(dp(12), dp(10), dp(12), dp(10))
            }
            addView(chatInput, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(10) })
            addView(row(pill("Enviar", primary = true) { sendChat() }))
        }, cardParams(top = 14))

        return ScrollView(this).apply {
            isFillViewport = true
            addView(page)
        }
    }

    private fun header(): View {
        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(18), dp(18), dp(18))
            background = gradient("#2C3E6B", "#5B3FA0", GradientDrawable.Orientation.TL_BR, radius = 22)
        }
        val top = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        val orb = FrameLayout(this).apply {
            background = gradient("#C9B98A", "#8E7A45", GradientDrawable.Orientation.TL_BR, oval = true)
            addView(ImageView(this@MainActivity).apply {
                setImageResource(R.drawable.ic_edit)
                setColorFilter(Color.parseColor("#1C1F27"))
                setPadding(dp(10), dp(10), dp(10), dp(10))
            }, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        }
        top.addView(orb, LinearLayout.LayoutParams(dp(48), dp(48)))
        val titles = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(12), 0, 0, 0) }
        titles.addView(TextView(this).apply {
            text = "Grammar Reviewer"
            setTextColor(Color.WHITE)
            textSize = 22f
            typeface = android.graphics.Typeface.create("serif", android.graphics.Typeface.BOLD)
        })
        titles.addView(TextView(this).apply {
            text = "Corrige, traduce y mejora tu redacción"
            setTextColor(Color.parseColor("#D8DCEB"))
            textSize = 13f
        })
        top.addView(titles, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        top.addView(iconChip(R.drawable.ic_settings) { startActivity(Intent(this, SettingsActivity::class.java)) })
        box.addView(top)

        langChip = TextView(this).apply {
            text = languageLabel()
            setTextColor(Color.WHITE)
            textSize = 12f
            background = roundedFill("#33FFFFFF", "#66FFFFFF", 999)
            setPadding(dp(12), dp(6), dp(12), dp(6))
            setOnClickListener {
                Prefs.saveEnglish(this@MainActivity, !Prefs.english(this@MainActivity))
                text = languageLabel()
            }
        }
        box.addView(langChip, LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(12) })
        return box
    }

    private fun modeRow(): View {
        val scroll = HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false }
        val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; setPadding(0, dp(10), 0, 0) }
        for (mode in Mode.entries) {
            val chip = TextView(this).apply {
                text = mode.label
                textSize = 13f
                setPadding(dp(14), dp(8), dp(14), dp(8))
                setOnClickListener { selectMode(mode) }
            }
            modeChips[mode] = chip
            row.addView(chip, LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply { setMargins(0, 0, dp(8), 0) })
        }
        scroll.addView(row)
        selectMode(selectedMode)
        return scroll
    }

    private fun selectMode(mode: Mode) {
        selectedMode = mode
        modeChips.forEach { (m, chip) ->
            val on = m == mode
            chip.setTextColor(if (on) Color.parseColor("#1C1F27") else Color.WHITE)
            chip.background = if (on) roundedFill("#C9B98A", "#C9B98A", 999) else roundedFill("#14FFFFFF", "#33C9B98A", 999)
        }
    }

    private fun runReview() {
        val text = input.text.toString()
        if (text.isBlank()) {
            toast("Escribe un texto primero.")
            return
        }
        resultCard.visibility = View.VISIBLE
        resultCard.text = "Pensando…"
        resultActions.visibility = View.GONE
        scope.launch {
            try {
                val result = Llm.review(text, selectedMode, Prefs.english(this@MainActivity), Prefs.keys(this@MainActivity))
                lastResult = if (selectedMode == Mode.Translate) result.corrected else buildString {
                    append(result.corrected)
                    if (result.changes.isNotEmpty()) {
                        append("\n\n")
                        result.changes.forEach { append("• ${it.original} → ${it.replacement}\n") }
                    }
                }.trimEnd()
                resultCard.text = lastResult
                resultActions.visibility = View.VISIBLE
            } catch (e: LlmException) {
                resultCard.text = e.message
            }
        }
    }

    private fun sendChat() {
        val question = chatInput.text.toString().trim()
        if (question.isEmpty()) return
        chatTurns.add("user" to question)
        chatInput.setText("")
        addBubble("user", question)
        val pending = addBubble("assistant", "Pensando…")
        scope.launch {
            try {
                val reply = Llm.chat(chatTurns.toList(), Prefs.keys(this@MainActivity))
                chatTurns.add("assistant" to reply)
                (pending as TextView).text = reply
            } catch (e: LlmException) {
                chatTurns.removeAt(chatTurns.lastIndex)
                (pending as TextView).text = e.message
            }
        }
    }

    private fun addBubble(role: String, text: String): View {
        val me = role == "user"
        val bubble = TextView(this).apply {
            this.text = text
            setTextColor(if (me) Color.parseColor("#1C1F27") else Color.WHITE)
            textSize = 14f
            setLineSpacing(0f, 1.25f)
            background = if (me) roundedFill("#C9B98A", "#C9B98A", 16) else roundedFill("#1FFFFFFF", "#22FFFFFF", 16)
            setPadding(dp(12), dp(9), dp(12), dp(9))
        }
        val lp = LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply {
            gravity = if (me) Gravity.END else Gravity.START
            setMargins(0, dp(8), 0, 0)
        }
        chatLog.addView(bubble, lp)
        return bubble
    }

    private fun copyLast() {
        if (lastResult.isBlank()) return
        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText("Grammar Reviewer", lastResult))
        toast("Copiado.")
    }

    private fun pasteLast() {
        copyLast()
        toast("Ahora abre el campo y usa Pegar, o usa el botón Pegar del globo.")
    }

    private fun checkForUpdate() {
        val now = System.currentTimeMillis()
        if (now - Prefs.lastUpdateCheck(this) < 12 * 60 * 60 * 1000L) return
        scope.launch {
            val info = UpdateChecker.check()
            Prefs.saveUpdateCheck(this@MainActivity, now)
            if (info != null) showUpdate(info)
        }
    }

    private fun showUpdate(info: UpdateInfo) {
        updateBanner.removeAllViews()
        val text = TextView(this).apply {
            text = "Nueva versión ${info.version} disponible"
            setTextColor(Color.WHITE)
            textSize = 14f
            typeface = android.graphics.Typeface.DEFAULT_BOLD
        }
        updateBanner.addView(text, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        updateBanner.addView(pill("Descargar", primary = true) {
            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(info.url)))
        })
        updateBanner.visibility = View.VISIBLE
    }

    private fun refreshStatus() {
        val overlay = if (BubbleService.canDrawOverlays(this)) "Permiso: listo" else "Permiso: falta"
        val reader = if (TextAccessibilityService.instance != null) "Lector: activo" else "Lector: inactivo"
        statusLine.text = "$overlay · $reader"
    }

    private fun languageLabel() = if (Prefs.english(this)) "Idioma del texto: English" else "Idioma del texto: Español"

    // --- Piezas visuales ---

    private fun card() = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        setPadding(dp(16), dp(16), dp(16), dp(16))
        background = roundedFill("#1AFFFFFF", "#26C9B98A", 20)
    }

    private fun cardParams(top: Int) = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply {
        topMargin = dp(top)
    }

    private fun sectionTitle(text: String) = TextView(this).apply {
        this.text = text
        setTextColor(Color.parseColor("#C9B98A"))
        textSize = 15f
        typeface = android.graphics.Typeface.DEFAULT_BOLD
        setPadding(0, 0, 0, dp(4))
    }

    private fun hint(text: String) = TextView(this).apply {
        this.text = text
        setTextColor(Color.parseColor("#AEB6CC"))
        textSize = 12.5f
        setPadding(0, dp(2), 0, dp(4))
        visibility = if (text.isEmpty()) View.GONE else View.VISIBLE
    }

    private fun row(vararg views: View): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.HORIZONTAL
        setPadding(0, dp(10), 0, 0)
        views.forEach { v ->
            addView(v, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f).apply { setMargins(dp(3), 0, dp(3), 0) })
        }
    }

    private fun pill(label: String, primary: Boolean = false, onClick: () -> Unit) = TextView(this).apply {
        text = label
        gravity = Gravity.CENTER
        textSize = 13f
        typeface = android.graphics.Typeface.DEFAULT_BOLD
        setTextColor(if (primary) Color.parseColor("#1C1F27") else Color.WHITE)
        background = if (primary) roundedFill("#C9B98A", "#C9B98A", 999) else roundedFill("#1FFFFFFF", "#33C9B98A", 999)
        setPadding(dp(12), dp(10), dp(12), dp(10))
        setOnClickListener { onClick() }
    }

    private fun iconChip(icon: Int, onClick: () -> Unit) = FrameLayout(this).apply {
        background = roundedFill("#33FFFFFF", "#66FFFFFF", 999)
        addView(ImageView(this@MainActivity).apply {
            setImageResource(icon)
            setColorFilter(Color.WHITE)
            setPadding(dp(9), dp(9), dp(9), dp(9))
        }, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        setOnClickListener { onClick() }
        layoutParams = LinearLayout.LayoutParams(dp(42), dp(42))
    }

    private fun roundedFill(fill: String, stroke: String, radiusDp: Int) = GradientDrawable().apply {
        cornerRadius = dp(radiusDp).toFloat()
        setColor(Color.parseColor(fill))
        setStroke(dp(1), Color.parseColor(stroke))
    }

    private fun gradient(a: String, b: String, orientation: GradientDrawable.Orientation, radius: Int = 0, oval: Boolean = false) =
        GradientDrawable(orientation, intArrayOf(Color.parseColor(a), Color.parseColor(b))).apply {
            if (oval) shape = GradientDrawable.OVAL else cornerRadius = dp(radius).toFloat()
        }

    private fun toast(text: String) = Toast.makeText(this, text, Toast.LENGTH_SHORT).show()

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()
}

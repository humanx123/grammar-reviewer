package com.humanx.grammarreviewer

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.graphics.Color
import android.graphics.PixelFormat
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.IBinder
import android.provider.Settings
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlin.math.abs

/** Globo flotante sobre otras apps. Se mueve, se oculta a un lado y abre un panel de revisión. */
class BubbleService : Service() {

    companion object {
        private const val CHANNEL_ID = "bubble"
        private const val NOTIFICATION_ID = 42

        fun start(ctx: Context) {
            ctx.startForegroundService(Intent(ctx, BubbleService::class.java))
        }

        fun stop(ctx: Context) {
            ctx.stopService(Intent(ctx, BubbleService::class.java))
        }

        fun canDrawOverlays(ctx: Context): Boolean = Settings.canDrawOverlays(ctx)
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var reviewJob: Job? = null

    private lateinit var windowManager: WindowManager
    private lateinit var bubble: TextView
    private lateinit var bubbleParams: WindowManager.LayoutParams
    private var panel: LinearLayout? = null
    private var panelParams: WindowManager.LayoutParams? = null
    private var resultView: TextView? = null
    private var titleView: TextView? = null

    private var lastField: FocusedField? = null
    private var lastSource: String = ""
    private var lastResult: Result? = null
    private var lastMode: Mode = Mode.Correct

    private val sizePx by lazy { dp(56) }
    private val screenW by lazy { resources.displayMetrics.widthPixels }
    private val screenH by lazy { resources.displayMetrics.heightPixels }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        windowManager = getSystemService(WINDOW_SERVICE) as WindowManager
        startAsForeground()
        createBubble()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int = START_STICKY

    override fun onDestroy() {
        reviewJob?.cancel()
        scope.cancel()
        removePanel()
        runCatching { windowManager.removeView(bubble) }
        super.onDestroy()
    }

    private fun startAsForeground() {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_ID, "Globo de revisión", NotificationManager.IMPORTANCE_LOW),
        )
        val notification: Notification = Notification.Builder(this, CHANNEL_ID)
            .setContentTitle("Grammar Reviewer activo")
            .setContentText("Toca el globo para revisar o traducir el campo que editas.")
            .setSmallIcon(android.R.drawable.ic_menu_edit)
            .setOngoing(true)
            .build()
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    private fun overlayType(): Int =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
        else @Suppress("DEPRECATION") WindowManager.LayoutParams.TYPE_PHONE

    private fun createBubble() {
        bubble = TextView(this).apply {
            text = "GR"
            setTextColor(Color.WHITE)
            textSize = 15f
            typeface = android.graphics.Typeface.DEFAULT_BOLD
            gravity = Gravity.CENTER
            background = GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                setColor(Color.parseColor("#2C3E6B"))
                setStroke(dp(2), Color.parseColor("#C9B98A"))
            }
            elevation = dp(6).toFloat()
        }

        bubbleParams = WindowManager.LayoutParams(
            sizePx, sizePx, overlayType(),
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE,
            PixelFormat.TRANSLUCENT,
        ).apply {
            gravity = Gravity.TOP or Gravity.START
            x = screenW - sizePx
            y = screenH / 3
        }

        var startX = 0f
        var startY = 0f
        var downX = 0
        var downY = 0
        var moved = false
        bubble.setOnTouchListener { _, e ->
            when (e.action) {
                MotionEvent.ACTION_DOWN -> {
                    startX = e.rawX
                    startY = e.rawY
                    downX = bubbleParams.x
                    downY = bubbleParams.y
                    moved = false
                    true
                }
                MotionEvent.ACTION_MOVE -> {
                    val dx = e.rawX - startX
                    val dy = e.rawY - startY
                    if (abs(dx) > dp(6) || abs(dy) > dp(6)) moved = true
                    if (moved) {
                        bubbleParams.x = (downX + dx).toInt().coerceIn(-sizePx, screenW)
                        bubbleParams.y = (downY + dy).toInt().coerceIn(0, screenH - sizePx)
                        windowManager.updateViewLayout(bubble, bubbleParams)
                        hidePanelIfMoved()
                    }
                    true
                }
                MotionEvent.ACTION_UP -> {
                    if (moved) snapToEdge() else togglePanel()
                    true
                }
                else -> false
            }
        }

        windowManager.addView(bubble, bubbleParams)
        snapToEdge()
    }

    /** Se queda pegado al borde, medio oculto, para no estorbar el texto. */
    private fun snapToEdge() {
        val leftSide = bubbleParams.x + sizePx / 2 < screenW / 2
        bubbleParams.x = if (leftSide) -(sizePx * 0.6f).toInt() else screenW - (sizePx * 0.4f).toInt()
        bubbleParams.y = bubbleParams.y.coerceIn(dp(40), screenH - sizePx - dp(40))
        windowManager.updateViewLayout(bubble, bubbleParams)
        panelParams?.let { positionPanel(it) }
    }

    /** Saca el globo del borde para poder tocarlo bien cuando se abre el panel. */
    private fun bringOutFromEdge() {
        if (bubbleParams.x < 0) bubbleParams.x = dp(4)
        if (bubbleParams.x > screenW - sizePx) bubbleParams.x = screenW - sizePx - dp(4)
        windowManager.updateViewLayout(bubble, bubbleParams)
    }

    private fun hidePanelIfMoved() {
        // Mientras se arrastra el globo no tapamos la pantalla con el panel.
        panel?.visibility = View.GONE
    }

    private fun togglePanel() {
        val existing = panel
        if (existing != null) {
            if (existing.visibility == View.VISIBLE) removePanel() else existing.visibility = View.VISIBLE
            return
        }
        bringOutFromEdge()
        buildPanel()
    }

    private fun buildPanel() {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(14), dp(12), dp(14), dp(12))
            background = GradientDrawable().apply {
                cornerRadius = dp(16).toFloat()
                setColor(Color.parseColor("#FBFAF6"))
                setStroke(dp(1), Color.parseColor("#E4E1D8"))
            }
            elevation = dp(10).toFloat()
        }

        val header = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        titleView = TextView(this).apply {
            text = "Grammar Reviewer"
            setTextColor(Color.parseColor("#1D2230"))
            textSize = 15f
            typeface = android.graphics.Typeface.SERIF
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        val close = Button(this).apply {
            text = "×"
            textSize = 18f
            setOnClickListener { removePanel() }
        }
        header.addView(titleView)
        header.addView(close)
        root.addView(header)

        val scroll = ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(200))
        }
        resultView = TextView(this).apply {
            text = "Abre un campo de texto, selecciona lo que quieras y toca Revisar o Traducir."
            setTextColor(Color.parseColor("#1D2230"))
            textSize = 14f
            setLineSpacing(0f, 1.2f)
        }
        scroll.addView(resultView)
        root.addView(scroll)

        val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        row.addView(actionButton("Revisar") { runAction(Mode.Correct) })
        row.addView(actionButton("Traducir") { runAction(Mode.Translate) })
        root.addView(row)

        val row2 = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        row2.addView(actionButton("Aplicar") { applyResult() })
        row2.addView(actionButton("Copiar") { copyResult() })
        root.addView(row2)

        panel = root
        panelParams = WindowManager.LayoutParams(
            dp(300), WindowManager.LayoutParams.WRAP_CONTENT, overlayType(),
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE,
            PixelFormat.TRANSLUCENT,
        ).apply { gravity = Gravity.TOP or Gravity.START }
        positionPanel(panelParams!!)
        windowManager.addView(root, panelParams)
    }

    private fun positionPanel(params: WindowManager.LayoutParams) {
        val onLeft = bubbleParams.x < screenW / 2
        params.x = if (onLeft) dp(60) else (screenW - dp(300) - dp(60)).coerceAtLeast(0)
        params.y = bubbleParams.y.coerceIn(dp(40), (screenH - dp(420)).coerceAtLeast(dp(40)))
        panel?.let { if (it.isAttachedToWindow) windowManager.updateViewLayout(it, params) }
    }

    private fun actionButton(label: String, onClick: () -> Unit): Button =
        Button(this).apply {
            text = label
            textSize = 12f
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
            setOnClickListener { onClick() }
        }

    private fun removePanel() {
        reviewJob?.cancel()
        panel?.let { runCatching { windowManager.removeView(it) } }
        panel = null
        panelParams = null
        resultView = null
        titleView = null
    }

    private fun setResult(text: String) {
        resultView?.text = text
    }

    private fun runAction(mode: Mode) {
        val field = TextAccessibilityService.instance?.readFocused()
        if (field == null) {
            setResult("No puedo leer el campo activo. Abre Ajustes > Accesibilidad > Grammar Reviewer y actívalo; luego vuelve a intentar.")
            return
        }
        val source = if (field.hasSelection) field.selectedText else field.full
        if (source.isBlank()) {
            setResult("El campo está vacío. Escribe o selecciona un texto.")
            return
        }

        lastField = field
        lastSource = source
        lastMode = mode
        lastResult = null
        titleView?.text = if (mode == Mode.Translate) "Traducción" else "Revisión"
        setResult("Revisando…")

        val keys = Prefs.keys(this)
        val english = Prefs.english(this)
        reviewJob?.cancel()
        reviewJob = scope.launch {
            try {
                val result = Llm.review(source, mode, english, keys)
                lastResult = result
                setResult(formatResult(result, mode))
            } catch (e: LlmException) {
                setResult(e.message ?: "Error al revisar el texto.")
            }
        }
    }

    private fun formatResult(result: Result, mode: Mode): String {
        if (mode == Mode.Translate) return result.corrected
        if (result.changes.isEmpty()) return result.corrected.ifBlank { "Sin cambios: el texto ya está bien." }
        val sb = StringBuilder(result.corrected)
        sb.append("\n\nCambios:\n")
        result.changes.forEach { c -> sb.append("• ${c.original} → ${c.replacement}\n") }
        return sb.toString().trimEnd()
    }

    private fun applyResult() {
        val result = lastResult
        val field = lastField
        if (result == null || field == null) {
            Toast.makeText(this, "Primero revisa o traduce un texto.", Toast.LENGTH_SHORT).show()
            return
        }
        val replacement = result.corrected
        val newFull = if (field.hasSelection) {
            field.full.substring(0, field.selStart) + replacement + field.full.substring(field.selEnd)
        } else {
            replacement
        }
        val ok = TextAccessibilityService.instance?.replaceFocused(newFull) == true
        Toast.makeText(
            this,
            if (ok) "Texto reemplazado." else "No pude escribir en este campo. Copia el resultado y pégalo.",
            Toast.LENGTH_SHORT,
        ).show()
        if (ok) removePanel()
    }

    private fun copyResult() {
        val text = lastResult?.corrected ?: resultView?.text?.toString().orEmpty()
        if (text.isBlank()) return
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        clipboard.setPrimaryClip(ClipData.newPlainText("Grammar Reviewer", text))
        Toast.makeText(this, "Copiado.", Toast.LENGTH_SHORT).show()
    }

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()
}

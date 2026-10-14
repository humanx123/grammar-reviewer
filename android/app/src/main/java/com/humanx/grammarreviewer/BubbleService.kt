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
import android.text.Spannable
import android.text.SpannableStringBuilder
import android.text.style.ForegroundColorSpan
import android.text.style.StyleSpan
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.ImageView
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

/** Globo flotante sobre otras apps: se arrastra, se esconde a un lado y abre un panel con iconos. */
class BubbleService : Service() {

    companion object {
        private const val CHANNEL_ID = "bubble"
        private const val NOTIFICATION_ID = 42
        private const val GOLD = "#C9B98A"
        private const val NAVY = "#2C3E6B"
        private const val NAVY_DEEP = "#141C33"

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
    private lateinit var bubble: FrameLayout
    private lateinit var bubbleParams: WindowManager.LayoutParams
    private var bubbleShown = false
    private var panel: LinearLayout? = null
    private var panelParams: WindowManager.LayoutParams? = null
    private var resultView: TextView? = null
    private var statusView: TextView? = null

    private var lastField: FocusedField? = null
    private var lastResult: Result? = null

    private val sizePx by lazy { dp(60) }
    private val screenW by lazy { resources.displayMetrics.widthPixels }
    private val screenH by lazy { resources.displayMetrics.heightPixels }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        windowManager = getSystemService(WINDOW_SERVICE) as WindowManager
        startAsForeground()
        createBubble()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        showBubble()
        return START_STICKY
    }

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
            .setContentText("Toca el globo para revisar, traducir o copiar el campo que editas.")
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
        val orb = ImageView(this).apply {
            setImageResource(R.drawable.ic_edit)
            setColorFilter(Color.parseColor(GOLD))
            scaleType = ImageView.ScaleType.CENTER
            setPadding(dp(16), dp(16), dp(16), dp(16))
            background = GradientDrawable(
                GradientDrawable.Orientation.TL_BR,
                intArrayOf(Color.parseColor(NAVY), Color.parseColor(NAVY_DEEP)),
            ).apply {
                shape = GradientDrawable.OVAL
                setStroke(dp(2), Color.parseColor(GOLD))
            }
            elevation = dp(10).toFloat()
        }
        bubble = FrameLayout(this).apply { addView(orb, FrameLayout.LayoutParams(sizePx, sizePx)) }

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
                        panel?.visibility = View.GONE
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

        showBubble()
    }

    /** Muestra el globo (si estaba oculto) y lo pega al borde. */
    private fun showBubble() {
        if (bubbleShown) return
        windowManager.addView(bubble, bubbleParams)
        bubbleShown = true
        snapToEdge()
        updateNotification("Toca el globo para revisar, traducir o copiar el campo que editas.")
    }

    /** Oculta el globo y el panel; el servicio sigue vivo hasta que pulses "Ocultar" en la app. */
    private fun hideBubble() {
        if (!bubbleShown) return
        removePanel()
        runCatching { windowManager.removeView(bubble) }
        bubbleShown = false
        updateNotification("Globo oculto. Abre la app y pulsa Mostrar globo para volver a verlo.")
    }

    private fun updateNotification(text: String) {
        val manager = getSystemService(NotificationManager::class.java)
        manager.notify(NOTIFICATION_ID, buildNotification(text))
    }

    private fun buildNotification(text: String): Notification =
        Notification.Builder(this, CHANNEL_ID)
            .setContentTitle("Grammar Reviewer")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.ic_menu_edit)
            .setOngoing(true)
            .build()

    /** Se queda pegado al borde, medio oculto, para no estorbar el texto. */
    private fun snapToEdge() {
        val leftSide = bubbleParams.x + sizePx / 2 < screenW / 2
        bubbleParams.x = if (leftSide) -(sizePx * 0.55f).toInt() else screenW - (sizePx * 0.45f).toInt()
        bubbleParams.y = bubbleParams.y.coerceIn(dp(40), screenH - sizePx - dp(40))
        windowManager.updateViewLayout(bubble, bubbleParams)
        panelParams?.let { positionPanel(it) }
    }

    /** Saca el globo del borde para poder tocarlo bien cuando se abre el panel. */
    private fun bringOutFromEdge() {
        if (bubbleParams.x < 0) bubbleParams.x = dp(6)
        if (bubbleParams.x > screenW - sizePx) bubbleParams.x = screenW - sizePx - dp(6)
        windowManager.updateViewLayout(bubble, bubbleParams)
    }

    private fun togglePanel() {
        val existing = panel
        if (existing != null) {
            // Tocar el globo de nuevo cierra el panel.
            removePanel()
            snapToEdge()
            return
        }
        bringOutFromEdge()
        buildPanel()
    }

    private fun buildPanel() {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(14), dp(12), dp(14), dp(14))
            background = GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM,
                intArrayOf(Color.parseColor("#22305A"), Color.parseColor(NAVY_DEEP)),
            ).apply {
                cornerRadius = dp(22).toFloat()
                setStroke(dp(1), Color.parseColor(GOLD))
            }
            elevation = dp(14).toFloat()
        }

        // Cabecera: estado y cerrar
        val header = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        // Icono y nombre: al tocarlos se abre la pantalla principal de la app.
        val logo = FrameLayout(this).apply {
            background = GradientDrawable(
                GradientDrawable.Orientation.TL_BR,
                intArrayOf(Color.parseColor(GOLD), Color.parseColor("#8E7A45")),
            ).apply { shape = GradientDrawable.OVAL }
            addView(ImageView(this@BubbleService).apply {
                setImageResource(R.drawable.ic_edit)
                setColorFilter(Color.parseColor("#1C1F27"))
                setPadding(dp(6), dp(6), dp(6), dp(6))
            }, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
            setOnClickListener { openMainApp() }
        }
        header.addView(logo, LinearLayout.LayoutParams(dp(30), dp(30)).apply { setMargins(0, 0, dp(8), 0) })
        statusView = TextView(this).apply {
            text = "Grammar Reviewer  ● Listo"
            setOnClickListener { openMainApp() }
            setTextColor(Color.parseColor(GOLD))
            textSize = 14f
            typeface = android.graphics.Typeface.DEFAULT_BOLD
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        header.addView(statusView)
        header.addView(iconButton(R.drawable.ic_hide, "Ocultar globo") { hideBubble() }, dp(40), dp(40))
        header.addView(iconButton(R.drawable.ic_close, "Cerrar") { removePanel(); snapToEdge() }, dp(40), dp(40))
        root.addView(header)

        // Resultado en tarjeta
        val card = ScrollView(this).apply {
            background = GradientDrawable().apply {
                cornerRadius = dp(14).toFloat()
                setColor(Color.parseColor("#1AFFFFFF"))
            }
            setPadding(dp(12), dp(10), dp(12), dp(10))
            layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(130)).apply {
                topMargin = dp(8)
                bottomMargin = dp(12)
            }
        }
        resultView = TextView(this).apply {
            text = "Toca un campo de texto y pulsa revisar o traducir."
            setTextColor(Color.WHITE)
            textSize = 15f
            setLineSpacing(0f, 1.25f)
        }
        card.addView(resultView)
        root.addView(card)

        // Acciones: solo iconos
        val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER }
        row.addView(iconButton(R.drawable.ic_edit, "Revisar") { runAction(Mode.Correct) }, dp(54), dp(54))
        row.addView(iconButton(R.drawable.ic_translate, "Traducir") { runAction(Mode.Translate) }, dp(54), dp(54))
        row.addView(iconButton(R.drawable.ic_copy, "Copiar") { copyResult() }, dp(54), dp(54))
        row.addView(iconButton(R.drawable.ic_paste, "Pegar") { pasteClipboard() }, dp(54), dp(54))
        row.addView(iconButton(R.drawable.ic_check, "Aplicar") { applyResult() }, dp(54), dp(54))
        root.addView(row)

        panel = root
        panelParams = WindowManager.LayoutParams(
            dp(330), WindowManager.LayoutParams.WRAP_CONTENT, overlayType(),
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE,
            PixelFormat.TRANSLUCENT,
        ).apply { gravity = Gravity.TOP or Gravity.START }
        positionPanel(panelParams!!)
        windowManager.addView(root, panelParams)
    }

    private fun iconButton(iconRes: Int, description: String, onClick: () -> Unit): FrameLayout =
        FrameLayout(this).apply {
            contentDescription = description
            isClickable = true
            setOnLongClickListener {
                Toast.makeText(this@BubbleService, description, Toast.LENGTH_SHORT).show()
                true
            }
            background = GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                setColor(Color.parseColor("#33C9B98A"))
                setStroke(dp(1), Color.parseColor(GOLD))
            }
            addView(ImageView(this@BubbleService).apply {
                setImageResource(iconRes)
                setColorFilter(Color.parseColor(GOLD))
                setPadding(dp(12), dp(12), dp(12), dp(12))
            }, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
            setOnClickListener { onClick() }
            layoutParams = LinearLayout.LayoutParams(dp(54), dp(54)).apply { setMargins(dp(4), 0, dp(4), 0) }
        }

    private fun positionPanel(params: WindowManager.LayoutParams) {
        val onLeft = bubbleParams.x < screenW / 2
        params.x = if (onLeft) dp(66) else (screenW - dp(330) - dp(66)).coerceAtLeast(0)
        params.y = bubbleParams.y.coerceIn(dp(40), (screenH - dp(420)).coerceAtLeast(dp(40)))
        panel?.let { if (it.isAttachedToWindow) windowManager.updateViewLayout(it, params) }
    }

    private fun removePanel() {
        reviewJob?.cancel()
        panel?.let { runCatching { windowManager.removeView(it) } }
        panel = null
        panelParams = null
        resultView = null
        statusView = null
    }

    private fun setResult(text: CharSequence) {
        resultView?.text = text
    }

    private fun setStatus(text: String) {
        statusView?.text = "Grammar Reviewer  $text"
    }

    /** Cierra el panel y abre la pantalla principal de la app. */
    private fun openMainApp() {
        removePanel()
        snapToEdge()
        val intent = Intent(this, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        startActivity(intent)
    }

    private fun runAction(mode: Mode) {
        val field = TextAccessibilityService.instance?.readFocused()
        if (field == null) {
            setResult("No puedo leer el campo activo. Activa el lector de texto en Ajustes > Accesibilidad.")
            setStatus("● Sin lector")
            return
        }
        val source = if (field.hasSelection) field.selectedText else field.full
        if (source.isBlank()) {
            setResult("El campo está vacío. Escribe o selecciona un texto.")
            return
        }

        lastField = field
        lastResult = null
        setStatus(if (mode == Mode.Translate) "● Traduciendo…" else "● Revisando…")
        setResult("…")

        val keys = Prefs.keys(this)
        val english = Prefs.english(this)
        reviewJob?.cancel()
        reviewJob = scope.launch {
            try {
                val result = Llm.review(source, mode, english, keys)
                lastResult = result
                setResult(formatResult(result, mode))
                setStatus("● Listo")
            } catch (e: LlmException) {
                setResult(e.message ?: "Error al revisar el texto.")
                setStatus("● Error")
            }
        }
    }

    /** Texto corregido con cada cambio resaltado en verde; las traducciones van sin resaltar. */
    private fun formatResult(result: Result, mode: Mode): CharSequence {
        if (mode == Mode.Translate) return result.corrected
        if (result.changes.isEmpty()) return result.corrected.ifBlank { "Sin cambios: el texto ya está bien." }
        val sb = SpannableStringBuilder(result.corrected)
        for (c in result.changes) {
            if (c.replacement.isEmpty()) continue
            val start = sb.indexOf(c.replacement)
            if (start < 0) continue
            val end = start + c.replacement.length
            sb.setSpan(ForegroundColorSpan(Color.parseColor("#7CE0A8")), start, end, Spannable.SPAN_EXCLUSIVE_EXCLUSIVE)
            sb.setSpan(StyleSpan(android.graphics.Typeface.BOLD), start, end, Spannable.SPAN_EXCLUSIVE_EXCLUSIVE)
        }
        return sb
    }

    private fun applyResult() {
        val result = lastResult
        val field = lastField
        if (result == null || field == null) {
            Toast.makeText(this, "Primero revisa o traduce un texto.", Toast.LENGTH_SHORT).show()
            return
        }
        val newFull = if (field.hasSelection) {
            field.full.substring(0, field.selStart) + result.corrected + field.full.substring(field.selEnd)
        } else {
            result.corrected
        }
        val ok = TextAccessibilityService.instance?.replaceFocused(newFull) == true
        Toast.makeText(
            this,
            if (ok) "Listo, texto reemplazado." else "No pude escribir en este campo. Usa Copiar y pégalo.",
            Toast.LENGTH_SHORT,
        ).show()
        if (ok) {
            removePanel()
            snapToEdge()
        }
    }

    private fun copyResult() {
        val text = lastResult?.corrected ?: resultView?.text?.toString().orEmpty()
        if (text.isBlank()) {
            Toast.makeText(this, "No hay nada que copiar todavía.", Toast.LENGTH_SHORT).show()
            return
        }
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        clipboard.setPrimaryClip(ClipData.newPlainText("Grammar Reviewer", text))
        Toast.makeText(this, "Copiado.", Toast.LENGTH_SHORT).show()
    }

    private fun pasteClipboard() {
        val service = TextAccessibilityService.instance
        if (service == null) {
            Toast.makeText(this, "Activa el lector de texto para pegar.", Toast.LENGTH_SHORT).show()
            return
        }
        val ok = service.pasteIntoFocused()
        Toast.makeText(
            this,
            if (ok) "Pegado." else "Abre un campo de texto y vuelve a intentar.",
            Toast.LENGTH_SHORT,
        ).show()
    }

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()
}

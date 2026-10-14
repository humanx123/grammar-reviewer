package com.humanx.grammarreviewer

import android.app.Activity
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast

/** Ajustes: claves y modelos de cada proveedor, y proveedores propios. */
class SettingsActivity : Activity() {

    private lateinit var groqKey: EditText
    private lateinit var groqModel: EditText
    private lateinit var geminiKey: EditText
    private lateinit var geminiModel: EditText
    private lateinit var nvidiaKey: EditText
    private lateinit var nvidiaModel: EditText
    private lateinit var customList: LinearLayout
    private val custom = mutableListOf<CustomProvider>()

    private lateinit var nameField: EditText
    private lateinit var urlField: EditText
    private lateinit var modelField: EditText
    private lateinit var keyField: EditText

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(buildUi())
    }

    private fun buildUi(): View {
        val keys = Prefs.keys(this)
        custom.clear()
        custom.addAll(keys.custom)

        val page = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(16), dp(16), dp(32))
            background = gradient("#0F1424", "#1A2340", GradientDrawable.Orientation.TOP_BOTTOM)
        }

        page.addView(TextView(this).apply {
            text = "Ajustes"
            setTextColor(Color.WHITE)
            textSize = 24f
            typeface = android.graphics.Typeface.create("serif", android.graphics.Typeface.BOLD)
        })
        page.addView(hint("Todo se guarda solo en este teléfono. Sin claves se usan los respaldos gratuitos."))

        page.addView(card().apply {
            addView(sectionTitle("Proveedores integrados"))
            groqKey = field("Clave de Groq", keys.groq, secret = true)
            groqModel = field("Modelo de Groq", keys.groqModel)
            addView(label("Groq"))
            addView(groqKey); addView(groqModel)
            geminiKey = field("Clave de Gemini", keys.gemini, secret = true)
            geminiModel = field("Modelo de Gemini", keys.geminiModel)
            addView(label("Gemini"))
            addView(geminiKey); addView(geminiModel)
            nvidiaKey = field("Clave de NVIDIA", keys.nvidia, secret = true)
            nvidiaModel = field("Modelo de NVIDIA", keys.nvidiaModel)
            addView(label("NVIDIA"))
            addView(nvidiaKey); addView(nvidiaModel)
            addView(hint("Kilo y LLM7 son gratuitos y no piden clave; siempre quedan como respaldo."))
        }, cardParams())

        page.addView(card().apply {
            addView(sectionTitle("Mis proveedores"))
            addView(hint("Cualquier servicio compatible con OpenAI: escribe su URL base (por ejemplo https://api.openai.com/v1), el modelo y, si lo necesita, la clave."))
            customList = LinearLayout(this@SettingsActivity).apply { orientation = LinearLayout.VERTICAL }
            addView(customList)
            renderCustom()
            nameField = field("Nombre (ej. Mi servidor)", "")
            urlField = field("URL base (https://…/v1)", "")
            modelField = field("Modelo (ej. gpt-4o-mini)", "")
            keyField = field("Clave (opcional)", "", secret = true)
            addView(label("Agregar proveedor"))
            addView(nameField); addView(urlField); addView(modelField); addView(keyField)
            addView(row(pill("Agregar", primary = true) { addCustom() }))
        }, cardParams())

        page.addView(row(
            pill("Guardar todo", primary = true) { save() },
            pill("Volver") { finish() },
        ), cardParams())

        return ScrollView(this).apply {
            isFillViewport = true
            addView(page)
        }
    }

    private fun renderCustom() {
        customList.removeAllViews()
        if (custom.isEmpty()) {
            customList.addView(hint("Aún no tienes proveedores propios."))
            return
        }
        custom.forEachIndexed { index, c ->
            val item = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = gradient("#14FFFFFF", "#14FFFFFF", GradientDrawable.Orientation.TOP_BOTTOM, radius = 14)
                setPadding(dp(12), dp(10), dp(12), dp(10))
            }
            val info = TextView(this).apply {
                text = "${c.name}\n${c.model} · ${c.baseUrl}"
                setTextColor(Color.WHITE)
                textSize = 13f
            }
            item.addView(info, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
            item.addView(pill("Quitar") {
                custom.removeAt(index)
                renderCustom()
            })
            customList.addView(item, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply { setMargins(0, 0, 0, dp(8)) })
        }
    }

    private fun addCustom() {
        val url = urlField.text.toString().trim()
        val model = modelField.text.toString().trim()
        if (!url.startsWith("http", ignoreCase = true) || model.isEmpty()) {
            toast("Escribe una URL que empiece con http(s) y el nombre del modelo.")
            return
        }
        custom.add(CustomProvider(
            name = nameField.text.toString().trim().ifBlank { "Personal" },
            baseUrl = url,
            model = model,
            key = keyField.text.toString().trim(),
        ))
        nameField.setText(""); urlField.setText(""); modelField.setText(""); keyField.setText("")
        renderCustom()
        toast("Proveedor agregado. Toca Guardar todo.")
    }

    private fun save() {
        Prefs.saveKeys(this, Keys(
            groq = groqKey.text.toString(),
            gemini = geminiKey.text.toString(),
            nvidia = nvidiaKey.text.toString(),
            groqModel = groqModel.text.toString(),
            geminiModel = geminiModel.text.toString(),
            nvidiaModel = nvidiaModel.text.toString(),
        ))
        Prefs.saveCustomProviders(this, custom.toList())
        toast("Ajustes guardados.")
    }

    // --- Piezas visuales ---

    private fun card() = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        setPadding(dp(16), dp(16), dp(16), dp(16))
        background = GradientDrawable().apply {
            cornerRadius = dp(20).toFloat()
            setColor(Color.parseColor("#1AFFFFFF"))
            setStroke(dp(1), Color.parseColor("#26C9B98A"))
        }
    }

    private fun cardParams() = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply {
        topMargin = dp(14)
    }

    private fun sectionTitle(text: String) = TextView(this).apply {
        this.text = text
        setTextColor(Color.parseColor("#C9B98A"))
        textSize = 16f
        typeface = android.graphics.Typeface.DEFAULT_BOLD
        setPadding(0, 0, 0, dp(4))
    }

    private fun label(text: String) = TextView(this).apply {
        this.text = text
        setTextColor(Color.WHITE)
        textSize = 13f
        typeface = android.graphics.Typeface.DEFAULT_BOLD
        setPadding(0, dp(12), 0, dp(4))
    }

    private fun hint(text: String) = TextView(this).apply {
        this.text = text
        setTextColor(Color.parseColor("#AEB6CC"))
        textSize = 12.5f
        setPadding(0, dp(4), 0, dp(4))
    }

    private fun field(hintText: String, value: String, secret: Boolean = false) = EditText(this).apply {
        hint = hintText
        setText(value)
        setTextColor(Color.WHITE)
        setHintTextColor(Color.parseColor("#7F8AA8"))
        inputType = if (secret) InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD else InputType.TYPE_CLASS_TEXT
        background = GradientDrawable().apply {
            cornerRadius = dp(12).toFloat()
            setColor(Color.parseColor("#14FFFFFF"))
            setStroke(dp(1), Color.parseColor("#33C9B98A"))
        }
        setPadding(dp(12), dp(10), dp(12), dp(10))
        layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT).apply { setMargins(0, dp(6), 0, 0) }
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
        background = GradientDrawable().apply {
            cornerRadius = dp(999).toFloat()
            setColor(if (primary) Color.parseColor("#C9B98A") else Color.parseColor("#1FFFFFFF"))
            setStroke(dp(1), Color.parseColor("#33C9B98A"))
        }
        setPadding(dp(12), dp(10), dp(12), dp(10))
        setOnClickListener { onClick() }
    }

    private fun gradient(a: String, b: String, orientation: GradientDrawable.Orientation, radius: Int = 0) =
        GradientDrawable(orientation, intArrayOf(Color.parseColor(a), Color.parseColor(b))).apply {
            cornerRadius = dp(radius).toFloat()
        }

    private fun toast(text: String) = Toast.makeText(this, text, Toast.LENGTH_SHORT).show()

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()
}

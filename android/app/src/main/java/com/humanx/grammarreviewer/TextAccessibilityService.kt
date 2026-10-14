package com.humanx.grammarreviewer

import android.accessibilityservice.AccessibilityService
import android.os.Bundle
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo

/** Texto del campo enfocado: contenido completo y selección (si la hay). */
data class FocusedField(val full: String, val selStart: Int, val selEnd: Int) {
    val hasSelection: Boolean get() = selEnd > selStart
    val selectedText: String get() = if (hasSelection) full.substring(selStart, selEnd) else ""
}

/**
 * Lee el campo de texto que el usuario está editando y puede reemplazarlo.
 * Android no permite leer texto de otras apps sin este servicio; el usuario lo activa a mano.
 */
class TextAccessibilityService : AccessibilityService() {

    companion object {
        @Volatile
        var instance: TextAccessibilityService? = null
    }

    override fun onServiceConnected() {
        instance = this
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        // No guardamos nada de los eventos: solo consultamos el campo cuando el usuario lo pide.
    }

    override fun onInterrupt() {}

    override fun onDestroy() {
        instance = null
        super.onDestroy()
    }

    fun readFocused(): FocusedField? {
        val node = findFocus(AccessibilityNodeInfo.FOCUS_INPUT) ?: return null
        if (!node.isEditable) return null
        val full = node.text?.toString() ?: return null
        val start = node.textSelectionStart
        val end = node.textSelectionEnd
        return if (start in 0..end && end > start && end <= full.length) {
            FocusedField(full, start, end)
        } else {
            FocusedField(full, 0, 0)
        }
    }

    /** Pega lo que haya en el portapapeles en el campo activo, reemplazando la selección. */
    fun pasteIntoFocused(): Boolean {
        val node = findFocus(AccessibilityNodeInfo.FOCUS_INPUT) ?: return false
        if (!node.isEditable) return false
        return node.performAction(AccessibilityNodeInfo.ACTION_PASTE)
    }

    fun replaceFocused(newFull: String): Boolean {
        val node = findFocus(AccessibilityNodeInfo.FOCUS_INPUT) ?: return false
        if (!node.isEditable) return false
        val args = Bundle().apply {
            putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, newFull)
        }
        return node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, args)
    }
}

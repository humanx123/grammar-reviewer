# Grammar Reviewer

Corrector de gramática, estilo y traducción con IA que se abre sobre cualquier texto que estés escribiendo. Pensado para redacción académica en inglés y español.

Sitio del proyecto: https://humanx123.github.io/grammar-reviewer/

## Funciones

- **Globo flotante (Windows):** selecciona texto en Word, el navegador o el correo y pulsa **Ctrl+Alt+G**.
- **Modos:** Corregir, Científico, Formal, Reescribir, Conciso, Casual y Traducir (traducción interpretativa).
- **Cambios por oración:** acepta o rechaza cada corrección; el texto final se recalcula al instante.
- **Escribir o pegar texto** dentro de la app, con límite de 10 000 caracteres.
- **Chat** con los modelos para preguntas de gramática, estilo, traducción o interpretación.
- **Varios modelos:** Gemini, Groq, NVIDIA, Kilo y LLM7. Si uno alcanza su límite gratuito, la app usa el siguiente.
- **Semáforo** de estado de las APIs y claves guardadas de forma cifrada.

## Instalación

### Windows (aplicación completa)

1. Descarga `GrammarReviewer-Setup-1.2.0.exe` desde [Releases](https://github.com/humanx123/grammar-reviewer/releases).
2. Ejecútalo. Se instala por usuario, sin permisos de administrador.
3. Requiere Microsoft Edge WebView2 Runtime (Windows 10/11 normalmente ya lo tienen).

### Android (globo flotante)

1. Descarga `grammar-reviewer-android.apk` desde https://humanx123.github.io/grammar-reviewer/ y acepta instalar desde esa fuente.
2. Abre la app y activa **Mostrar sobre otras apps** y el **lector de texto** (Accesibilidad).
3. Pulsa **Mostrar globo**. El círculo "GR" se queda a un lado de la pantalla; tócalo sobre cualquier app para revisar o traducir el campo que editas.

El lector de texto solo se usa cuando pulsas el globo. Requiere Android 8.0 o superior. Código en `android/`.

### Linux (línea de comandos)

La aplicación de escritorio con globo flotante es solo para Windows por ahora. En Linux está disponible la CLI:

```bash
tar -xzf grammar-reviewer-cli-linux-x64.tar.gz
chmod +x grammar-reviewer-cli
./grammar-reviewer-cli Correct "Me and him goes to the store yesterday."
./grammar-reviewer-cli Scientific "Our data shows the effect is big."
./grammar-reviewer-cli Translate "Se me cayó el mundo."
./grammar-reviewer-cli --chat "¿Cómo digo 'ni modo' en inglés?"
```

Opciones: `--en` indica que el texto está en inglés; `--fallback-only` usa solo los modelos sin clave.

## Claves de API

Todas son opcionales: sin claves, la app usa los respaldos gratuitos sin clave (Kilo y LLM7).

| Servicio | Dónde obtenerla | Variable de entorno |
|---|---|---|
| Groq | https://console.groq.com/keys | `GROQ_API_KEY` |
| Gemini | https://aistudio.google.com/apikey | `GEMINI_API_KEY` |
| NVIDIA | https://build.nvidia.com | `NVIDIA_API_KEY` |
| Kilo | https://kilo.ai | `KILO_API_KEY` |

En Windows las claves se pegan en la pantalla de claves (icono de engranaje) y se cifran con tu cuenta de usuario (DPAPI). En Linux se guardan en `~/.config/GrammarBubble/keys.bin` con permisos de solo tu usuario; también puedes usar las variables de entorno.

## Privacidad

El texto que revisas se envía a los proveedores de IA configurados. En los niveles gratuitos, algunos proveedores (Google Gemini, Kilo y OpenRouter, entre otros) pueden usar esos datos para mejorar sus productos o registrar las peticiones. **No envíes información confidencial sin evaluarlo antes.**

## Compilar desde el código

Requisitos: .NET 8 SDK. Para la aplicación de Windows, Windows 10/11 y Inno Setup 6 (para el instalador).

```bash
# CLI (Windows o Linux)
dotnet publish Cli/GrammarBubble.Cli.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Aplicación de Windows
dotnet publish App/GrammarBubble.App.csproj -c Release -r win-x64 --self-contained true -o publish/app

# Instalador (Windows)
iscc installer/GrammarBubble.iss
```

Estructura:

- `Core/`: motor compartido (proveedores de IA, prompts, diff, claves). Multiplataforma.
- `Cli/`: línea de comandos (Windows y Linux).
- `App/`: aplicación de Windows (WPF + WebView2, atajo global, captura de texto). La interfaz está en `App/ui/`.
- `installer/`: script de Inno Setup.
- `tools/`: generador del icono.

## Limitaciones conocidas

- El globo con Ctrl+Alt+G funciona solo en Windows.
- La captura de texto usa el portapapeles (Ctrl+C); algunas apps pueden no responder a esa captura.
- Los modelos gratuitos tienen límites por minuto y por día; la app los indica en el semáforo.

## Licencia

Pendiente de definir por el autor.

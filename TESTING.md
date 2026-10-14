# Lista de pruebas antes de publicar

Las pruebas automáticas (`dotnet test`) y la compilación de GitHub Actions cubren el motor y que todo compile. Esta lista cubre lo que no se puede probar automáticamente: la interfaz y el comportamiento en dispositivos reales.

## Automático (cada cambio, GitHub Actions)
- [ ] Job **Pruebas del motor** en verde.
- [ ] Jobs de CLI (Windows, Linux, macOS Intel y Apple Silicon), app de Windows y APK en verde.

## Android (teléfono real, Android 8 o superior)
- [ ] Instalar el APK desde el enlace de la página.
- [ ] Conceder "Superposición" y activar el lector de texto. El estado de la app debe cambiar a *listo* y *activo*.
- [ ] Pulsar **Mostrar globo**: aparece el círculo a un lado de la pantalla.
- [ ] Escribir un texto en Word o en el navegador, tocar el globo, pulsar **Revisar**: aparece la corrección con las partes resaltadas.
- [ ] **Traducir** funciona y el resultado está en el otro idioma.
- [ ] **Copiar** deja el texto en el portapapeles.
- [ ] **Pegar** pega el texto en el campo activo.
- [ ] **Aplicar** reemplaza el texto del campo (probar con y sin selección).
- [ ] El icono de ojo oculta el globo; **Mostrar globo** lo vuelve a poner.
- [ ] Ajustes: agregar un proveedor propio, quitarlo, guardar y volver a abrir la app.
- [ ] Aviso de actualización aparece cuando `version.json` tiene una versión mayor.

## Windows (escritorio)
- [ ] Instalar `GrammarReviewer-Setup-<versión>.exe` y abrirlo.
- [ ] Seleccionar texto en Word y pulsar **Ctrl+Alt+G**: aparece el globo junto al cursor.
- [ ] Revisar, aceptar algunos cambios y pulsar **Aplicar**: el texto se reemplaza en Word.
- [ ] Pantalla de claves: guardar una clave, cerrar y volver a abrir: no la vuelve a pedir.
- [ ] Aviso de actualización en la bandeja cuando hay versión nueva.

## Linux y macOS (CLI)
- [ ] `./grammar-reviewer-cli Correct "Me and him goes to the store."` devuelve una corrección.
- [ ] `./grammar-reviewer-cli --chat "..."` responde.

## Antes de publicar una release
- [ ] Subir `version.json` solo si la plataforma cambió (el flujo de Actions lo hace al publicar la release).
- [ ] Revisar que los enlaces de descarga apuntan a la release correcta (no a `latest` si la última release no trae ese archivo).
- [ ] Revisar la página en el móvil y en el escritorio.

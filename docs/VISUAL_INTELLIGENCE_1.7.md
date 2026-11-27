# Bali Desktop 1.7 — Visual Intelligence

## Continuidad

La versión instalada era 1.6 con la corrección de VPN, 203 comprobaciones automatizadas y 20 verificaciones flotantes en el registro. El proyecto conservaba las herramientas 1.5, Windows Speech, Whisper Tiny y Base, la conversación hablada, Helena y visemas, el contexto temporal de voz y las iniciativas opcionales. Small seguía fuera de producción. 1.7 añade frases de reconocimiento de Visión a la gramática de Windows y conserva los modelos y la selección previa.

## Cómo probar

Abre **Visión** desde la bandeja de Bali y activa **Activar Visión**. Cierra el panel y vuelve a una ventana con contenido de prueba. Puedes decir «¿Qué tengo abierto?», «Lee lo que aparece en esta ventana», «¿Qué estoy viendo?» o «Explícame este error». Luego prueba «¿Cómo puedo solucionarlo?» y «Revisa nuevamente». Bali usa la ventana anterior a su propio clic, muestra un indicador durante la lectura y reutiliza únicamente un contexto vigente. «Revisa nuevamente» toma una nueva observación y distingue si cambió el texto extraído. El comando «Analiza el administrador de tareas» combina texto visible con una muestra local de recursos.

Para ver una previsualización, habilita el permiso de captura que corresponda. El panel admite selección explícita de ventana, monitor y región en píxeles. OCR tiene su propio permiso. El texto, la previsualización y los datos extraídos son temporales.

Bali también reconoce frases breves como «Lee la ventana» y «Mira la pantalla». Si Whisper devuelve una frase incompleta con vocabulario de Visión, se prueba Windows Speech con el mismo audio y se usa solo un comando visual reconocido con suficiente confianza. El ajuste pasó 210 comprobaciones.

En la prueba de voz posterior, Whisper Base devolvió «Lea la ventana» al oír «Lee la ventana», y Windows y Base devolvieron «Le esta ventana» al oír «Lee esta ventana». Bali admite esas variantes exactas y rechaza las negaciones o instrucciones adicionales. La bandeja incluye **Leer ventana actual** para ejecutar la misma lectura local sin dictar un comando. Pasaron 211 comprobaciones. [Comparación de comandos cortos](../evidence/visual-short-commands-fixed/visual-speech.txt)

## Componentes

- **Identificación:** Win32 y DWM comprueban ventana, proceso, inicio, título, geometría, monitor, visibilidad y escritorio interactivo. La referencia de primer plano se toma cuando el usuario interactúa con Bali y caduca a los 45 segundos. La información no se recopila mediante un sondeo continuo.
- **Accesibilidad:** UI Automation utiliza el árbol de controles y los rangos visibles. Lee nombres, etiquetas, estados, elementos de lista y texto que exponga la aplicación. No usa escritura, invocación ni patrones de interacción. La calidad depende del proveedor accesible.
- **Captura:** PrintWindow toma una ventana identificada y CopyFromScreen recibe únicamente el rectángulo elegido para un monitor o región. Se compara la lista de ventanas alrededor de una captura amplia. La aplicación debe exponer la superficie, y contenido acelerado o protegido puede resultar vacío. Windows Graphics Capture aporta selección nativa y usa cuadros Direct3D. Desktop Duplication por DXGI es una opción para captura de alta frecuencia. Ambas quedaron fuera del producto orientado a observaciones puntuales. [Captura nativa de Windows](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture) · [PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)
- **OCR:** Windows OCR conserva saltos de línea del resultado y usa los idiomas que informe Windows. Funcionó en la laptop aun con esta compilación WPF sin paquete. Microsoft documenta la interfaz como compatible oficialmente con aplicaciones de escritorio con identidad de paquete, por lo que en otros equipos puede faltar o requerir otro despliegue. Tesseract ofrece datos por idioma y exige distribuir un motor y datos adicionales. No se instaló. [Windows OCR](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr) · [Tesseract](https://tesseract-ocr.github.io/tessdoc/Installation.html)

## Privacidad y razonamiento

Visión, la captura, OCR y el envío externo comienzan desactivados. La identificación y la accesibilidad disponen de controles independientes. Las exclusiones por nombre de proceso o texto del título y los campos protegidos se verifican antes de exponer contenido. La captura amplia se rechaza si se detecta una ventana excluida, un campo protegido o un árbol inaccesible. Estos filtros dependen de las señales que exponga Windows y la aplicación. Mantén cerrada cualquier ventana privada antes de autorizar una captura amplia.

El contexto vence a los dos minutos, el permiso corresponde a un fragmento concreto y vence a los 75 segundos. Al cambiar de ventana, volver a analizar, cancelar o modificar permisos se deja de usar el fragmento. El texto corto de UI Automation se lee antes de pedir una confirmación verbal. El OCR, el código y el texto largo requieren revisar el fragmento exacto en Visión. Las comprobaciones de privacidad filtran credenciales reconocibles, direcciones de correo, enlaces y líneas que intenten dar órdenes al modelo. Los filtros no identifican toda información confidencial, de modo que la revisión sigue siendo necesaria.

DeepSeek recibe solo el fragmento textual aprobado y la pregunta explícita. El contexto visual no incluye imágenes, herramientas ni historial, y las respuestas visuales se marcan como locales para futuros contextos. Se conservan los límites diarios y mensuales. El texto observado se trata como datos no confiables; carece de autoridad para solicitar herramientas, ejecutar comandos o cambiar permisos. La API documenta imágenes para DeepSeek Flash. Esta versión mantiene el transporte de texto, que también sirve al cambiar de modelo. [Capacidades de DeepSeek Flash](https://api-docs.deepseek.com/guides/vision/)

Para una versión posterior, un modelo local liviano como SmolVLM o Qwen2-VL de 2B puede evaluarse con llama.cpp y su proyector. El tamaño de los pesos, la licencia del modelo y el uso de memoria de textos, imágenes y proyector deben medirse por compilación. La CPU es viable como vía experimental, y GPU solo con un backend probado. El aprendizaje de precisión gráfica y latencia de esos modelos queda sin evaluar en esta laptop. No se descargaron modelos visuales ni se contrataron servicios. Visión lee texto y controles, con capacidad limitada para explicar diagramas, fotografías o gráficos sin texto. [Compatibilidad multimodal de llama.cpp](https://github.com/ggml-org/llama.cpp/blob/master/docs/multimodal.md)

## Evidencia

Las pruebas de Windows usaron ventanas WPF sintéticas con un error C#, una consulta SQL, texto de documento en español e inglés, un botón y un campo protegido. La validación flotante conserva captura física y voz de Helena con texto reconocido y DeepSeek simulados. Falta validar con tu voz, aplicaciones cotidianas, captura de monitor o región con contenido real no sensible y una consulta visual real autorizada.

Pasaron **209 comprobaciones automatizadas** y **20 verificaciones flotantes**. En Windows se leyó la ventana sintética mediante UI Automation en 137 ms. La captura tardó 42 ms y su OCR 80 ms. El proceso de diagnóstico usó 0.33 s de CPU acumulada, marcó 67.9 MiB de pico y terminó con 58.5 MiB residentes. Windows tenía OCR es-MX. El texto inglés de prueba también fue legible con ese motor. No había paquete OCR en-* y no se añadió otro idioma. Se comprobó que un campo protegido y una captura sin permiso quedaran bloqueados.

Bali 1.7 quedó abierto con la ventana flotante como única ventana. Una muestra breve de reposo registró 1.04 % de CPU del equipo y 155.8 MiB residentes. La credencial, el registro de consumo, la agenda y los permisos conservaron sus huellas tras el despliegue.

[Prueba real de Visión](../evidence/visual-17-release/vision.txt) · [Comprobaciones](../evidence/unit.txt) · [Flujo flotante](../evidence/floating-17-release/runtime.txt) · [Reposo](../evidence/visual-17-release/idle.txt) · [Ejecutable](../dist/BaliDesktop.exe) · [Recuperación 1.6](../recovery/1.6-before-vision/)

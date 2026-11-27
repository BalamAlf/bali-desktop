# Bali Desktop 1.4.1 — Voice & Natural Understanding

## Diagnóstico

El registro de 1.4 mostró aperturas del micrófono y detección de actividad seguidas de «Detecté voz, pero no pude entenderla». También hubo turnos aceptados. Los fallos observados ocurrieron antes de interpretar la intención. Windows usaba el dispositivo predeterminado y el reconocedor es-ES. El registro carecía de niveles, picos y confianza, así que no permite distinguir ruido, recorte, pronunciación, selección del dispositivo o configuración del dictado.

La prueba recibió 35 bloques del micrófono físico, descartó 92 098 bytes PCM y pudo reabrirlo. Windows enumeró una matriz Realtek. Queda por verificar qué mejoras acústicas ofrece el controlador y cuáles están activas.

## Cambios

La captura flotante conserva 500 ms antes del inicio de voz estimado. La detección por energía exige actividad sostenida y mide nivel RMS, pico y saturación. El filtro opcional atenúa graves y aplica ganancia moderada. Está apagado de forma predeterminada mientras se valida con ruido real. Una conversación de fondo también puede activar el detector.

Windows conserva sus umbrales. En uso flotante, si reconoce palabras fiables y también rechaza un segmento, Bali conserva la transcripción para revisión. Si Windows no obtiene texto, puede intentar recuperarlo con Whisper Tiny usando el mismo audio. En ambos casos, una acción requiere que Bali diga lo que entendió y reciba una confirmación clara con Windows Speech. El texto dudoso no se envía automáticamente a DeepSeek. Whisper procesa WAV en memoria en un proceso que termina al completar o cancelar. El diagnóstico compara ambos motores con el mismo PCM, carece de acceso a herramientas y no guarda grabaciones.

El enrutador entiende las cuatro formas solicitadas de pedir un recordatorio, horas en palabras o cifras, plazos relativos y variaciones de pendientes, aplicaciones, notas y consultas. Si falta el horario, Bali pregunta y reabre brevemente el micrófono después del audio. Puedes completar, corregir o cancelar. Las acciones de Whisper requieren confirmación con Windows Speech. Una petición fiable que parece una acción puede pasar a **Herramientas** para que el usuario solicite una propuesta de DeepSeek y apruebe cada acción. Las preguntas abiertas de Windows siguen la configuración y el presupuesto existentes.

## Resultados

Se usó el mismo PCM generado con Microsoft Helena Desktop. Ruido, televisión y voces superpuestas se simularon mediante mezcla digital. WER es tasa de errores de palabra e incluye acentos y cifras escritas como dígitos.

| Caso | Windows Speech | Whisper Tiny |
| --- | --- | --- |
| Frase corta y repetición | 0 % WER | 50 %, «habre calculadora» |
| Frase larga limpia | 8 % | 8 % |
| Horario con pausa | 0 % | 14 %, «10» en vez de «diez» |
| Ruido continuo simulado | Rechazo | 23 % |
| Televisión simulada | Rechazo | 100 %, mezcló palabras de fondo |
| Voz superpuesta simulada | 8 % | 15 % |
| Expresión coloquial mexicana | Rechazo | 71 % |
| Silencio | Sin transcripción | Sin transcripción |

Windows tardó aproximadamente 0.13–0.65 s. Whisper tardó unos 0.9–1.6 s y usó 3–6 s acumulados de CPU en cuatro hilos y hasta unos 148 MiB residentes. Carga de modelo aproximada de 55–105 ms. La muestra en reposo de Bali flotante registró 0.73 % de CPU total y 118.4 MiB de RAM, sin proceso de Whisper. El turno WPF con reproducción real midió 4.5 s, 1.30 % de CPU total y 130.3 MiB de RAM residente. Se comprobaron los visemas y la reapertura del micrófono tras el audio. La sonda física valida captura y reapertura. El flujo WPF usa texto conocido para aislar la reproducción de Helena. Falta aislar el coste del filtro acústico en una medición independiente.

Se incluyó el binario portátil de whisper.cpp v1.8.3 y el modelo multilingüe Tiny q5_1 de 31 MiB. Base q5_1 (57 MiB) y Small q5_1 (181 MiB) son cifras de catálogo. La autorización cubrió exclusivamente Tiny. La vía de Whisper con ONNX Runtime añade un runtime y una ruta de despliegue sin medir. [Distribución portátil](https://github.com/ggml-org/whisper.cpp/releases/tag/v1.8.3) · [Modelos](https://huggingface.co/ggerganov/whisper.cpp) · [Ejemplos de Windows ML](https://github.com/microsoft/WindowsML/tree/main/Samples/Runtime)

RNNoise puede atenuar ruido y Silero VAD ayuda a localizar actividad vocal. Separar la voz de una persona de otra requiere un paso distinto. Estas alternativas quedaron fuera del paquete. La identificación de hablante necesitaría enrolamiento autorizado, protección local de muestras y pruebas con voces simultáneas. Su coste y precisión aquí siguen sin medirse. [RNNoise](https://github.com/xiph/rnnoise) · [Silero VAD](https://github.com/snakers4/silero-vad)

## Prueba interactiva

Valores recomendados para empezar: Windows Speech, sensibilidad 100 %, silencio de 1.15 s, máximo de 20 s, filtro apagado y continuidad desactivada. Abre **Voz → Diagnóstico de reconocimiento**, pulsa **Capturar y comparar Windows / Whisper** y di la misma frase primero en silencio y luego con las conversaciones habituales de fondo. Para probar el seguimiento, haz clic en Bali y di «Recuérdame revisar mis proyectos». Cuando Bali termine de preguntar, responde «Mañana a las diez de la mañana». Este último paso guarda un recordatorio en Agenda.

Pasaron las 165 comprobaciones automatizadas, incluidas las 142 anteriores, además de la cancelación en ambos reconocedores. Se admiten el saludo «hola» de una palabra y la consulta «¿qué tengo hoy?», juntos o por separado. Si no hay elementos programados para hoy, Bali lo indica explícitamente. El registro mostró rechazos totales intermitentes de ambos motores y otros turnos con texto de Whisper. Ahora se registra el nivel de voz, el pico, la saturación y el resultado de cada motor sin guardar audio o texto personal. Una señal débil activa un realce limitado. Tras un rechazo total, Bali puede probar el otro motor local, salvo durante una confirmación.

Si la detección de pausas termina el turno demasiado pronto, abre la bandeja de Windows y selecciona **Iniciar escucha manual**. Di la frase y pulsa una vez sobre Bali o elige **Terminar escucha manual**. La captura conserva la frase completa hasta ese momento o hasta el límite configurado. Queda pendiente validar el resultado con voz humana de español de México en el entorno real. Las pruebas no consultaron DeepSeek.

## Conversación flotante con DeepSeek

La consulta hablada está habilitada por defecto, condicionada a que DeepSeek esté activado, verificado y dentro del presupuesto. Las consultas locales se resuelven primero. Una pregunta informativa aceptada por Windows se envía directamente. Whisper puede seguir el mismo camino si la frase tiene una estructura informativa clara, al menos cinco palabras y carece de indicios sencillos de repetición o truncamiento. Es una heurística, no una estimación de confianza.

Ante una duda relevante, Bali repite la pregunta y escucha «sí», «no», «cancela» o una corrección después de hablar. La confirmación vence a los 25 segundos y el silencio cierra antes la escucha según el ajuste de espera. Un «no» descarta la pregunta anterior y abre una oportunidad de corrección de hasta 15 segundos. No hay captura durante la reproducción. Otro clic cancela el turno. Se evita repetir la misma consulta en un intervalo de ocho segundos y se conserva el bloqueo de solicitudes simultáneas del proveedor.

En **Voz** se pueden cambiar las consultas directas, la confirmación verbal y la apertura automática de Conversar. Esta última está apagada inicialmente. La preferencia de conversación continua se conserva. La voz lee una versión sin marcado ni enlaces y el historial de sesión retiene la respuesta completa. Las herramientas siguen bajo el enrutador local y sus autorizaciones. Se conservan los controles de privacidad, credencial y consumo.

Pasaron 172 comprobaciones automatizadas, incluidas las 165 anteriores, y 16 verificaciones flotantes. La prueba flotante usa texto reconocido y transporte simulados, además del micrófono físico y reproducción real con Helena. Comprueba el chat cerrado, la reapertura después de hablar, las consultas directas de ambos motores, la modificación de una pregunta dudosa, la deduplicación y una respuesta de proveedor inaccesible. Las pruebas de servicio conservan la cobertura mediante transporte simulado del permiso, límites diario y mensual y bloqueo de turnos simultáneos. No se realizó una consulta real. Para validar con tu voz, haz clic sobre Bali, pregunta «¿Qué es una VPN?» y di «sí» si te pide confirmar. [Prueba flotante](../evidence/floating-deepseek-141/runtime.txt) · [Compilación anterior recuperable](../recovery/1.4.1-before-floating/)

[Ejecutable](../dist/BaliDesktop.exe) · [Recuperación 1.4](../recovery/1.4/) · [Comprobaciones](../evidence/unit.txt) · [Comparación](../evidence/stt-comparison.txt) · [Flujo flotante](../evidence/floating-141/runtime.txt)

# Bali Desktop 1.6 — Proactive Intelligence & Voice 2.0

## Punto de partida

La compilación activa de 1.5 usaba Windows Speech y Whisper local, con Tiny q5_1 (31 MiB) y Base q5_1 (57 MiB) disponibles. La suite más reciente registraba 193 aprobaciones y la última validación flotante de escritorio registraba 17. El catálogo, los permisos de apertura, la consulta de recursos y el análisis de temporales estaban disponibles. Los intentos reales anteriores mostraban que VPN y algunas acciones locales funcionaban, con problemas intermitentes de transcripción de nombres y confirmaciones breves.

Whisper funciona con CPU, cuatro hilos, lenguaje español y arranque por turno desde el ejecutable portátil. El audio viaja por memoria. Windows usa el reconocedor local disponible y amplía la gramática con nombres y alias de aplicaciones. 1.6 conserva el proveedor seleccionado en el perfil.

## Resultados de voz

Se empleó el mismo PCM de Helena para Windows, Tiny, Base y un único candidato adicional, Small q5_1 (181 MiB). Se probaron frases cortas, largas, pausas, distintas velocidades, ruido y conversaciones superpuestas mezclados digitalmente, una expresión mexicana, un nombre de aplicación y una sigla. El equipo tenía AMD Ryzen AI 5 340, seis núcleos y doce procesadores lógicos, con aproximadamente 16 GB de RAM. Se verificó SHA-1 de Small 6FE57DDCFDD1C6B07CDCC73AAF620810CE5FC771.

| Modelo | Decodificación representativa | Pico del proceso | Resultado relevante |
| --- | --- | --- | --- |
| Tiny | ~1 s | ~145 MiB | Reconoció «sí, autorizo». Produjo «uve bene» en la muestra de VPN. |
| Base | ~2 s | ~212 MiB | Mejoró la expresión coloquial. Produjo «Abre ahí» y «WPN». |
| Small | ~6–8 s | ~479 MiB | Reconoció VPN y la frase lenta completa. Repitió palabras en la orden de abrir Brave. |

Un prompt técnico fijo de Base recuperó VPN, falló la orden de Brave y produjo texto en silencio. La distribución de uso diario conserva Tiny y Base y prescinde de ese prompt. Small queda en la evidencia de laboratorio. Medium se consideró por tamaño y coste esperado a partir de Small, sin descarga ni medición. No se deduce precisión real en español de México de una voz sintética ni de mezclas digitales. [Modelos cuantizados](https://huggingface.co/ggerganov/whisper.cpp) · [Opciones de contexto del motor](https://github.com/ggml-org/whisper.cpp/blob/master/examples/cli/README.md) · [Comparación reproducible](../evidence/voice-16-model/stt-comparison.txt)

## Correcciones y conversación

En **Sistema → Diccionario técnico** puedes copiar el último texto reconocido, conservar solo el término incorrecto y aprobar una equivalencia para **Aplicaciones**, **Preguntas técnicas** o **Agenda**. Puedes editarla y eliminarla. Las reglas usan límites de palabras, se aplican una sola vez y no se encadenan entre sí. Las variantes guardadas previamente siguen disponibles. El laboratorio **Voz** admite una frase de referencia para calcular errores de palabra a partir de una comparación local. Las transcripciones y la referencia de diagnóstico permanecen en RAM.

«¿Y para qué sirve?» y otros seguimientos breves pueden reutilizar el último tema respondido con éxito. El contexto dura dos minutos de forma predeterminada y se puede configurar entre 30 y 300 segundos o desactivar en **Voz**. Bali incorpora la pregunta previa únicamente cuando reconoce un seguimiento, conserva las opciones de historial existentes y descarta el tema al cancelar. No graba durante Helena. La continuidad del micrófono sigue dependiendo de la preferencia existente. El uso en línea conserva los permisos, la comprobación de datos sensibles y el límite de consumo. Las acciones locales y propuestas de herramientas siguen su recorrido de autorización.

## Iniciativas locales

En **Vida artificial → Iniciativas locales** se habilitan por separado los saludos, resumen de agenda, recordatorios próximos, sugerencias de rutinas, reanudación, memoria, batería y disco. La observación de primer plano requiere un permiso separado y conserva solo el nombre de una aplicación clásica autorizada durante la sesión. No consulta títulos o contenido. La hora y actividad observada por el motor, la actividad reciente, el uso de rutinas y la valoración explícita influyen en la selección. Se respeta el modo y la autonomía, con un intervalo de 15 minutos y un máximo diario por categoría. No hay acciones automáticas ni solicitudes a DeepSeek para generar sugerencias.

La valoración puede hacerse desde el panel o diciendo «acepto sugerencia» o «rechazo sugerencia». El panel muestra preferencias, motivo de la última propuesta, inactividad, duración de la sesión, tema activo y recursos. Los procesos y ventanas sin identificación válida quedan fuera de la observación, igual que la actividad personal ajena a Bali.

## Repetir con voz humana

En **Voz → Capturar y comparar Whisper Tiny / Base**, escribe una referencia antes de hablar. Prueba «Abre el editor de Microsoft», «Sí», «¿Qué es una VPN?» y una frase con pausa. Repite primero sin ruido y luego con tu ruido habitual. La prueba es local y no abre aplicaciones. Para validar la conversación real, haz clic en Bali y pregunta «¿Qué es una VPN?». Después de la respuesta, pregunta «¿Y para qué sirve?». Con continuidad desactivada, haz otro clic para iniciar ese segundo turno.

Pasaron 202 comprobaciones, incluidas las 193 de la compilación anterior, y 20 verificaciones flotantes. En la prueba flotante se usaron captura física y reproducción de Helena, texto reconocido inyectado y transporte simulado. Se comprobó que la pregunta de seguimiento usó el tema previo, que cancelar lo descartó y que un turno sin tema no llamó al proveedor. Faltan una prueba con voz humana y ruido ambiental, una consulta real y observar las iniciativas durante el uso cotidiano.

La compilación 1.6 quedó abierta con la ventana flotante como única ventana de Bali. En una muestra corta de reposo usó 0.78 % de CPU del equipo y 155.5 MiB residentes. Los archivos existentes de credencial, consumo, agenda y permisos conservaron sus huellas. [Medición de reposo](../evidence/voice-16-model/idle.txt)

La corrección posterior admite «red privada virtual» y propone VPN al recibir «WPN» o «uve bene» en una pregunta compatible. Las variantes ambiguas requieren confirmar antes de enviarse. Helena pronuncia «uve pe ene» en esa confirmación. El diagnóstico muestra la interpretación local junto a la transcripción original. [Prueba flotante del ajuste](../evidence/voice-16-vpn-fix/runtime.txt)

[Pruebas automatizadas](../evidence/unit.txt) · [Flujo flotante simulado](../evidence/voice-16-floating/runtime.txt) · [Ejecutable 1.6](../dist/BaliDesktop.exe) · [Copia recuperable 1.5](../recovery/1.5-before-proactive/)

# Personal Intelligence 1.4

## Demostración local

Haz un clic en Bali, pronuncia una instrucción y deja un breve silencio. Estos ejemplos pasan por el enrutador local y funcionan sin DeepSeek.

- «Bali, recuérdame en cinco minutos beber agua».
- «Bali, crea un pendiente para hoy revisar mis notas» y «Bali, ¿qué pendientes tengo hoy?».
- «Bali, abre Calculadora». El catálogo verifica primero que el ejecutable de Windows esté presente.
- «Bali, muéstrame mis recordatorios». Para cancelar el último, di «cancela mi último recordatorio» y autorízalo con «confirma» en un turno posterior.

Agenda, Aplicaciones, Documentos, Conocimiento y Herramientas están en el panel que abre el clic derecho. Si Bali ignora los clics, inicia el turno desde la bandeja. «Abre Obsidian» requiere que el ejecutable esté autorizado en Aplicaciones.

## Autoridad y permisos

`IBaliTool` declara ID, descripción, parámetros, riesgo y funcionamiento offline. `BaliToolRouter` interpreta texto local mediante reglas, registra herramientas y autoriza orígenes de voz, texto y panel. El ejecutor verifica cada parámetro y cada permiso. Las solicitudes compuestas admiten hasta cuatro acciones y las evalúan por separado. El bloqueo de duplicados cubre la ventana reciente y los recordatorios equivalentes activos. No se reintenta automáticamente una operación ambigua.

Herramientas permite revocar permisos individualmente. La cancelación por voz conserva el ID seleccionado y exige confirmar desde el mismo canal en 45 segundos. Borrar datos o exportar desde el panel requiere confirmación. Exportar usa un archivo Markdown nuevo. El registro conserva hora, herramienta, origen, autorización, resultado y tipos de error, sin volcar textos, documentos o secretos.

La conversación normal no ejecuta acciones a partir de respuestas del modelo. El botón de propuesta en Herramientas pide autorización para una consulta posiblemente facturable, envía solo la petición y la hora local, y acepta exclusivamente un arreglo JSON con campos permitidos. Cada acción propuesta se muestra completa y necesita aprobación individual. Se rechazan rutas propuestas, herramientas fuera de catálogo y destinos de archivos. Los resultados de herramientas y sus preguntas quedan en el historial local y se excluyen del contexto en línea.

## Agenda y conocimiento

Los recordatorios se guardan en hora local de Windows. Se admiten intervalos relativos, fecha y hora, mañana, recurrencia diaria y semanal. Una recurrencia sin hora usa las 09:00. Agenda permite editar, consultar, buscar, posponer, completar y archivar. La hora anterior de aviso queda en el historial. Las ocurrencias vencidas se agrupan al reabrir y se actualiza la próxima fecha antes de emitir el aviso. Si el proceso termina entre guardado y aviso, esa ocurrencia podría quedar registrada sin haber sido visible. Windows también puede silenciar globos de bandeja. Bali debe estar ejecutándose y no despierta el equipo.

La voz de recordatorios en Concentrado y Descanso es configurable y comienza desactivada. Los avisos no interrumpen una captura o respuesta activa. La sugerencia de recordatorios próximos es opcional, se presenta en modo Libre o Interactivo y tiene un intervalo mínimo de 30 minutos.

El archivo `asistente-personal.json` conserva agenda, catálogo, proyectos, notas, ideas, decisiones, preferencias, recuerdos y bitácora independientemente de la memoria de Bali. Conocimiento admite búsqueda textual, fechas, etiquetas, prioridad, vínculo entre elementos y exportación Markdown. Se puede importar ese Markdown en el futuro, sin conexión automática a una bóveda.

## Aplicaciones y documentos

Calculadora y Explorador se incorporan si están presentes en sus rutas de Windows. **Detectar aplicaciones** consulta App Paths de usuario y equipo para nombres conocidos. El registro manual permite seleccionar otros ejecutables instalados y confirmar la ruta. Las aplicaciones se abren con ruta local y sin argumentos. Nunca se enumeran procesos para cerrarlos. [Registro de aplicaciones de Windows](https://learn.microsoft.com/en-us/windows/win32/shell/app-registration)

Documentos acepta selección o arrastre explícito. TXT y Markdown se agrupan por líneas o encabezado, CSV se consulta por línea, DOCX usa ZIP y XML con DTD desactivado, y PDF seleccionable se lee por página mediante PdfPig 0.1.12, Apache-2.0. Se limita el archivo a 12 MiB, el PDF a 30 páginas y el trabajador PDF a 12 segundos. Se permiten búsqueda de fragmentos, vista extractiva de puntos y detección de líneas que podrían describir pendientes. Los documentos quedan en memoria hasta descartarlos o cerrar Bali. El PDF sin texto se identifica como tal. No se incluye OCR ni se envía contenido documental al proveedor. [PdfPig y licencia](https://www.nuget.org/packages/PdfPig/0.1.12)

Los paquetes de compilación están fijados y disponibles offline en `vendor`. La distribución incluye los avisos de licencia en `dist/licenses`. El lector PDF se carga bajo demanda en un proceso independiente que solo recibe el archivo previamente elegido.

## Síntesis local

| Opción | Español y voz | Compatibilidad y coste de integración | Sincronización |
| --- | --- | --- | --- |
| Microsoft Helena | Femenina, es-ES | Incluida y validada en esta laptop | Visemas SAPI reales |
| Supertonic 3 | Español genérico y estilos F disponibles, sin acreditación es-MX | ~398 MB, ONNX, ejemplo C# .NET 9 y repositorio archivado. Código MIT y modelo OpenRAIL-M | Requeriría medir PCM y sincronizarlo con el reloj de reproducción |
| Piper Claude high | Ficha es-MX, la ficha no acredita género | ~63,1 MB de modelo más runtime local. El motor es GPL-3.0-or-later y la ficha del conjunto de entrenamiento indica Apache-2.0 | Requeriría validar el reloj PCM, calidad y latencia en Windows |

Se conservó el proveedor Windows con muestras y selector de voces instaladas. No se descargaron modelos ni se midieron CPU, RAM, latencia o naturalidad de Supertonic/Piper en esta laptop. Integrar uno requiere validar sus dependencias, optar por un formato de distribución apropiado y comprobar sincronización real. [Supertonic](https://github.com/supertone-oss-archive/supertonic) · [Piper y licencia](https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/LICENSING.md) · [Ficha es-MX](https://huggingface.co/rhasspy/piper-voices/blob/main/es/es_MX/claude/high/MODEL_CARD)

## Validación en Windows

Pasaron 142 comprobaciones, incluidas las 119 previas. Las nuevas cubren herramientas, autorización, propuestas no ejecutadas, solicitudes múltiples, duplicados, fechas, recurrencia y reinicio, conocimiento, privacidad, exportación y formatos de lectura. El PDF sintético seleccionable y la variante sin texto se probaron mediante el trabajador real. [Pruebas automatizadas](../evidence/unit.txt)

La ejecución WPF aislada creó y consultó un pendiente, guardó un recordatorio desde la ruta flotante con una transcripción inyectada, reprodujo audio real de Helena con visemas y conservó el laboratorio. Windows inició Calculadora a través del catálogo. El contador de DeepSeek permaneció en cero. La transcripción humana de los comandos nuevos y el recorrido micrófono → DeepSeek → respuesta hablada quedan sin verificación independiente en 1.4. La prueba real de reconocimiento local de 1.3.2 fue confirmada por el usuario. [Ejecución WPF](../evidence/personal-1.4-91f387/runtime.txt) · [Agenda](../evidence/personal-1.4-91f387/agenda.png)

| Fase WPF | CPU media | Memoria residente | Memoria privada |
| --- | ---: | ---: | ---: |
| Reposo | 0,83 % | 119,8 MiB | 111,8 MiB |
| Agenda | 1,04 % | 160,5 MiB | 149,5 MiB |
| Turno flotante local | 1,27 % | 167,7 MiB | 151,4 MiB |
| Extracción PDF | 1,09 % | 169,5 MiB | 151,5 MiB |
| Laboratorio | 1,82 % | 175,9 MiB | 155,5 MiB |
| Síntesis | 0,97 % | 175,7 MiB | 155,8 MiB |

Las fases duraron ocho segundos en una misma ejecución, por lo que la memoria incorpora las vistas abiertas previamente. La fila PDF indica el proceso principal. El trabajador del PDF sintético usó 43,3 MiB residentes y 9,06 % de CPU media durante 0,39 s. Una muestra corta no determina la estabilidad de memoria a largo plazo ni representa un documento largo.

La ejecución no hace solicitudes facturables por iniciativa propia. Las pruebas no enviaron tráfico a DeepSeek. El binario 1.3.2 está recuperable por separado. [Recuperación](../recovery/1.3.2/README.md)

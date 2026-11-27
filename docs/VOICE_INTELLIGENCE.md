# Voice Intelligence 1.3

## Usar el proveedor

1. En **Configuración → Inteligencia**, selecciona **DeepSeek a petición**.
2. Introduce la credencial en el campo enmascarado y usa **Guardar / sustituir clave**.
3. Pulsa **Prueba controlada · puede generar cargo** y confirma si deseas autorizar una petición. Usa una frase fija, un máximo de 24 tokens de salida y ninguna categoría opcional. Guardar una clave o habilitar DeepSeek por sí solos no envía solicitudes.
4. En **Conversar**, Enter y **Enviar local** ejecutan comandos reconocidos. **Consultar a DeepSeek** también resuelve primero los comandos locales y solo envía una pregunta abierta si la credencial ya superó la prueba. **Cancelar** detiene el turno. El historial de mensajes permanece en la sesión.

## Arquitectura

`IBaliIntelligenceProvider` conserva las respuestas y acciones locales. `IStreamingConversationProvider` recibe texto y una proyección de contexto ya construida. **DeepSeek.cs** implementa Chat Completions sobre HTTP asíncrono, consumo, tiempo de espera y cancelación. Devuelve únicamente fragmentos de texto y estadísticas autorizadas del proveedor. El resultado se muestra y se puede leer en voz alta. El motor local mantiene la autoridad sobre modos, memoria, hábitos, datos cognitivos y simulaciones. La respuesta remota no se reinyecta en el enrutador local.

**ConversationData.cs** separa los recuerdos conversacionales, el presupuesto de contexto y la credencial del perfil cognitivo. **ConversationPanel.cs** reutiliza el rostro y las expresiones para indicar captura, procesamiento y reproducción. La aplicación mantiene un único turno activo, no ejecuta nuevas consultas en segundo plano y no reintenta automáticamente errores o límites.

## Contrato de DeepSeek

Se verificaron el 26 de noviembre de 2026 el endpoint `POST https://api.deepseek.com/chat/completions`, la autenticación Bearer y los identificadores `deepseek-flash` y `deepseek-v4-pro`. Flash es la selección inicial. Se desactiva el modo de razonamiento para ambos. En este modo la API aplica temperatura configurable a Pro y fija la de Flash. El proveedor publica un contexto de un millón de tokens y máximos de salida de hasta 384 K. Bali aplica límites locales más estrechos. Solicita streaming SSE con `stream_options.include_usage`, consume solo `delta.content` y espera `[DONE]`. El cliente distingue fallos de autenticación, saldo, concurrencia, servidores y tiempo de espera. Los límites publicados de concurrencia son 2500 para Flash y 500 para Pro por cuenta y pueden cambiar. Bali admite un turno. [Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/) · [Límites](https://api-docs.deepseek.com/quick_start/rate_limit/) · [Errores](https://api-docs.deepseek.com/quick_start/error_codes/)

Tarifas verificadas en USD por millón de tokens, según caché y franja publicada:

| Modelo | Entrada con caché pico / valle | Entrada sin caché pico / valle | Salida pico / valle |
| --- | ---: | ---: | ---: |
| Flash | 0,006 / 0,003 | 0,30 / 0,15 | 1,20 / 0,60 |
| V4 Pro | 0,044 / 0,022 | 1,32 / 0,66 | 3,96 / 1,98 |

La estimación local usa tarifa pico y un presupuesto de contexto basado en bytes UTF-8 más márgenes de estructura. No es el tokenizador del proveedor ni reproduce su factura. Antes de una tentativa se reserva memoria de cuota y coste en disco. Al recibir estadísticas se registra lo informado, incluidos caché y razonamiento si se reportan. Sin estadísticas se conserva la reserva y se marca como desconocido. El presupuesto diario y mensual cubre esta instalación. Los cargos de otros clientes y los cambios de tarifa quedan fuera. [Precios oficiales](https://api-docs.deepseek.com/quick_start/pricing/)

## Privacidad y memoria

La API Key se guarda solo como blob DPAPI **CurrentUser** en `%LOCALAPPDATA%\BaliDesktop\deepseek.dpapi`. Se puede sustituir y eliminar. La segunda acción invalida la verificación, al igual que guardar otra clave. El archivo de uso no guarda prompts, respuestas, claves ni cuerpos de error de DeepSeek. No se guardan audios. El código no define herramientas ni comandos ejecutables para el modelo.

El proveedor recibe la pregunta que se envía explícitamente y la identidad básica de Bali. Estado cognitivo, hábitos agregados, turnos recientes y recuerdos son cuatro categorías opcionales, desactivadas por defecto. Los recuerdos exigen aprobación local y permiso individual de envío. Se guarda fecha, ID, procedencia, categoría, relevancia, confianza de procedencia y permiso. La coincidencia usa palabras clave, antigüedad, relevancia y reutilización, con hasta cuatro recuerdos por turno y un máximo de 120 recuerdos persistentes. Los turnos inmediatos tienen un límite de 24 en memoria y se pueden limpiar con **Nueva conversación**. El contexto nunca adjunta archivos ni la memoria completa. Diagnóstico indica categorías y no sus datos.

## Voz en Windows

**WindowsAudio.cs** usa WinMM para capturar PCM mono a 16 kHz en el dispositivo elegido, con un máximo de 20 segundos y sin archivos intermedios. System.Speech usa gramáticas de comandos y dictado cuando el reconocedor local lo permite. El modo predeterminado es mantener pulsado y soltar. En Voz se puede elegir pulsación única y revisar la transcripción. Solo un comando reconocido puede ejecutarse automáticamente si se desactiva la revisión. Una transcripción de pregunta abierta requiere el botón explícito de DeepSeek.

La síntesis usa Windows y prefiere una voz en español de México o latinoamericano si está instalada. Ofrece elección de voz, velocidad, volumen, reproducción automática y detención. **Supertonic 3** se consideró como otro proveedor local. Su repositorio archivado incluye un ejemplo C# que requiere .NET 9 y ONNX Runtime. En Windows x64 necesitaría un binario auxiliar con ese runtime o una adaptación comprobada a .NET Framework 4.8.1. Soporta español genérico, tiene muestras oficiales de pronunciación y publica código MIT y modelos OpenRAIL-M. Los modelos ONNX ocupan unos 398 MB. No se instaló el runtime ni se descargaron los modelos. Las muestras del proveedor no establecen una voz mexicana específica ni el consumo, la latencia o la calidad en esta laptop. La revisión deja Windows como implementación local entregada. [Proyecto y licencia](https://github.com/supertone-oss-archive/supertonic) · [Modelos ONNX](https://huggingface.co/Supertone/supertonic-3/tree/main/onnx)

## Migración y recuperación

El esquema 3 conserva `estado.1.2.json` byte a byte antes del primer guardado. Los recuerdos conversacionales, la credencial y el consumo son archivos independientes. La copia del ejecutable 1.2 se encuentra en `recovery/1.2`. [Resultados de pruebas](RESULTADOS_VOICE_INTELLIGENCE.md)
# Resultados de Voice Intelligence 1.3

Validación realizada el 26 de noviembre de 2026.

## Pruebas

Pasaron 96 comprobaciones, incluidas las 72 de 1.2. Las nuevas cubren migración, DPAPI, sustitución y eliminación de credenciales, autorización de recuerdos, búsqueda, contexto, enrutamiento local, presupuesto, SSE, caché, autenticación, saldo, límite de solicitudes, fallo de transporte, cancelación, aislamiento de agentes y salida remota sin permisos de control. HTTP se sustituyó por transportes simulados. Se enviaron **cero peticiones reales a DeepSeek** y el coste de pruebas fue **USD 0**. [Registro](../evidence/unit.txt)

Bali 1.3 abrió en la sesión real con **Inteligencia** visible, proveedor local y campo de clave vacío. La migración preservó 29 interacciones, 12 asociaciones, familiaridad y fecha inicial. El hash del respaldo `estado.1.2.json` coincidió con el del perfil antes de reemplazar el ejecutable. En una ejecución WPF aislada con la misma compilación se comprobó conversación por texto, laboratorio y modo local, con cero solicitudes. [Inteligencia](../evidence/voice-final-1.3/inteligencia.png) · [Conversación local](../evidence/voice-final-1.3/conversacion-local.png) · [Registro WPF](../evidence/voice-final-1.3/runtime.txt)

## Audio real

Windows enumeró el micrófono integrado Realtek, un motor de reconocimiento `es-ES` y las voces Microsoft Helena Desktop y Microsoft Zira Desktop. En esa sesión no apareció un motor `es-MX`. Se abrió y liberó el micrófono predeterminado después de capturar 256.258 bytes PCM. La captura se descartó sin transcripción ni guardado. Windows completó una reproducción con Microsoft Helena. En una prueba de dictado separada, la canalización nueva reconoció «Hoy quiero hablar de música y de libros» a partir de audio generado en memoria. El dictado con una persona hablando al micrófono queda sin verificar de extremo a extremo. [Diagnóstico de audio](../evidence/audio-real-1.3/audio-windows.txt)

## Auditoría de Cognitive Life 1.2

Ensayo de 30 días con semilla 71 y escenario Negativa. Las iniciativas excluyen Calma. Las señales se cuentan en las decisiones observadas por el escenario y pueden incluir Calma. «Sin señal» significa que el escenario no ofreció retroalimentación. Las ausencias registradas por el motor son otro contador y solo aplican al perfil que aprende por consecuencias.

| Política | Iniciativas | Positivas | Negativas | Sin señal | Objetivos satisfechos | Explorar | Observar |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1.1 | 64.281 | 8.128 | 41.965 | 14.188 | No aplica | 50.895 | 9.928 |
| 1.2 sin consecuencias | 64.068 | 8.131 | 44.037 | 12.196 | 29 | 53.257 | 9.922 |
| 1.2 con consecuencias | 63.935 | 51.971 | 77 | 12.300 | 29 | 88 | 63.417 |

Con el aprendizaje activo, el total varía un −0,21 % frente al control 1.2. La reducción del 83,51 % al 0,15 % de negativas entre ocasiones evaluadas se explica por la redistribución hacia Observar. Explorar y Microexpresión siguen apareciendo, con 88 y 8 ejecuciones en el perfil que aprendió. Se registraron 8.412 ventanas sin respuesta en ese perfil. La distribución depende de la regla sintética de retroalimentación. [Contadores](../evidence/auditoria-iniciativas-1.3.txt) · [Experimento completo](../evidence/experimentos-1.2.txt)

## Pendiente

La primera comprobación real de DeepSeek requiere que el usuario guarde su clave y autorice una petición desde el diagnóstico. No se verificó el tiempo de respuesta, el consumo ni la facturación de un turno real. El modo local se probó con el proveedor desactivado y las caídas de red se simularon. La laptop no se desconectó físicamente. Los filtros locales reconocen patrones frecuentes de datos sensibles y no cubren cualquier forma de describirlos.

## Recursos

La CPU es la media del intervalo sobre los 12 procesadores lógicos. La memoria es la muestra al final. Las cuatro primeras fases se midieron en la compilación final de WPF con perfil aislado. El laboratorio usó tres rostros activos. Las dos últimas filas corresponden al diagnóstico real de audio como proceso independiente, sin sumar el coste del escritorio WPF.

| Escenario | Duración | CPU | Conjunto de trabajo | Privada |
| --- | ---: | ---: | ---: | ---: |
| Reposo | 16,0 s | 0,45 % | 165,9 MiB | 149,4 MiB |
| Conversación por texto local | 16,0 s | 0,59 % | 172,2 MiB | 157,7 MiB |
| Laboratorio | 16,0 s | 0,78 % | 171,4 MiB | 156,8 MiB |
| Modo local sin proveedor | 16,0 s | 0,51 % | 171,7 MiB | 156,9 MiB |
| Captura física | 8,72 s | 0,04 % | 32,3 MiB | 30,8 MiB |
| Síntesis y reproducción local | 9,17 s | 0,09 % | 54,1 MiB | 34,5 MiB |

La referencia de 1.2 en Libre fue 0,468 % de CPU, 162,4 MiB de conjunto de trabajo y 149,4 MiB privados. En el reposo de 1.3, el conjunto de trabajo fue 3,5 MiB mayor y la memoria privada coincidió al redondeo. El laboratorio de 1.3 se midió después de usar conversación, mientras la referencia 1.2 del laboratorio se obtuvo en otra secuencia y sesión. Las muestras cortas no prueban tendencias de memoria a largo plazo. [Telemetría WPF](../evidence/voice-final-1.3/runtime.txt) · [Telemetría de audio](../evidence/audio-real-1.3/audio-windows.txt)

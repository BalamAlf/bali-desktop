# Resultados de pruebas

La validación de la actualización 1.3 está en [Resultados de Voice Intelligence](RESULTADOS_VOICE_INTELLIGENCE.md).

Los resultados de la actualización 1.2 están en [Resultados de Cognitive Life](RESULTADOS_COGNITIVE_LIFE.md).

Validación realizada el 26 de noviembre de 2026.

## Adaptive Life 1.1

Se ejecutaron 44 comprobaciones. Las 20 anteriores siguen aprobadas. Las 24 nuevas cubren evolución, estabilidad, selección, cooldown, confianza, ritmo diario, concentración, autonomía, discontinuidades del reloj, retención, consolidación, recuperación del perfil, conversación y simulación aislada. [Registro de pruebas](../evidence/unit.txt)

La migración real conservó 23 interacciones, familiaridad 0,400493825881842, modo Libre y estilo minimalista. Se verificó la presencia de `estado.1.0.json` en el perfil local. En paralelo, una ejecución con perfil aislado abrió la ventana de WPF y la bandeja, cambió de modo, dio una respuesta contextual, ocultó la mascota y terminó con código 0. El segundo arranque restauró modo Interactivo, una interacción y la posición guardada. Desde el laboratorio abierto en la sesión de Windows también se ejecutó la simulación predeterminada de siete días. El resultado mostró 168 horas simuladas y 64 interacciones experimentales. El contador real siguió en 23. [Laboratorio](../evidence/adaptive-1.1/laboratorio.png) · [Validación y reinicio](../evidence/adaptive-1.1/runtime.txt)

### Simulaciones reproducibles

Semilla 71. El escenario con más contacto usa 32 interacciones esperadas por día, horario fijo y actividad configurada al 70 %. El escenario tranquilo usa una interacción esperada por día, horario variable, concentración, actividad al 20 % y una pausa central sin ejecución.

| Escenario | Días observados | Horas | Interacciones | Sociabilidad | Rasgo de actividad |
| --- | ---: | ---: | ---: | ---: | ---: |
| 7 días, más contacto | 7 | 168 | 235 | 69,3 % | 49,2 % |
| 7 días, tranquilo | 5 | 120 | 7 | 34,1 % | 36,5 % |
| 30 días, más contacto | 30 | 720 | 938 | 58,3 % | 49,2 % |
| 30 días, tranquilo | 20 | 480 | 23 | 34,9 % | 36,3 % |

Las configuraciones difieren en varias variables. La tabla muestra separación de perfiles y las pruebas repitieron dos escenarios con la misma semilla. [Salida del simulador](../evidence/simulaciones-1.1.txt)

### Rendimiento por modo

Fases de 45 segundos en una aplicación WPF real con perfil aislado. Libre se midió después de inicializar el laboratorio y cerrar el panel. La última muestra de RAM de cada fase corresponde al conjunto de trabajo del proceso. Interactivo incluyó la apertura del panel de conversación.

| Modo | CPU media | RAM |
| --- | ---: | ---: |
| Libre | 0,40 % | 168 MiB |
| Concentrado | 0,24 % | 168 MiB |
| Descanso | 0,14 % | 168 MiB |
| Interactivo | 0,52 % | 170 MiB |
| Oculto | 0,00 % | 170 MiB |

La línea de base anterior de 0,89 % de CPU y 132,8 MiB procede de ocho segundos con el panel cerrado. Los 163 MiB de 1.0 con Diagnóstico abierto ofrecen una segunda referencia. Estas mediciones no forman una comparación controlada de arranque en frío. La compilación final corrige el texto de la candidatura de saludo cuando no hubo un regreso.

En una muestra adicional de 30 segundos, la compilación final abierta con Vida artificial visible en la sesión normal registró 0,785 % de CPU media y 163,1 MiB de RAM. La ventana siguió respondiendo.

### Alcance

La reanudación y las ausencias se probaron mediante saltos del reloj y simulaciones. No se suspendió físicamente la laptop. El hito de voz se validó con una señal de prueba del motor. No se realizó una nueva captura por micrófono. Los hábitos necesitan tiempo de uso real y la gráfica de personalidad requiere dos días con muestras de aprendizaje.

## Actualización de apariencia

Se recompiló y se ejecutaron 20 comprobaciones correctas. Las seis nuevas comprueban el estilo inicial, la migración de un perfil sin preferencia visual, el cambio inmediato al renderizar cada estilo y la persistencia de ambos con la memoria y los ajustes intactos. La aplicación se abrió en la sesión normal con estilo minimalista, modo Libre y las 22 interacciones previas del perfil. [Registro actualizado](../evidence/unit.txt).

En una muestra de ocho segundos con el panel cerrado, el estilo minimalista marcó 0,89 % de CPU media y 132,8 MiB de RAM. En la ejecución por fases con Diagnóstico abierto, el modo Libre marcó 1,60 % y 163 MiB. La ejecución cerró con código 0. [Captura del rostro](../evidence/minimalista/rostro.png) · [Mediciones por fase](../evidence/minimalista/runtime.txt).

## Automatizadas

La compilación y 14 comprobaciones finalizaron correctamente. Cubren semillas, variedad, evolución, modos y prioridades, saludo, familiaridad, interpolación y seguimiento numérico, parpadeo, poses, JSON y comandos. [Registro completo](../evidence/unit.txt).

## Aplicación de Windows

La ejecución por fases creó la ventana transparente y el icono de bandeja, alternó modos, ocultó la mascota, abrió el panel, ejecutó un comando de texto y salió con código 0. La ejecución siguiente recuperó el modo y una interacción. Una prueba con coordenadas de entrada conocidas comprobó que la ventana abrió en (617, 393). El primer intento de esa repetición convirtió la fecha de la prueba a un formato ajeno al serializador. El segundo preservó el JSON original y solamente cambió las coordenadas. [Ejecución por fases](../evidence/validacion/runtime.txt) · [Prueba de posición](../evidence/posicion/runtime.txt).

Se inspeccionaron capturas WPF tomadas de la aplicación en ejecución. [Rostro](../evidence/validacion/rostro.png) · [Diagnóstico](../evidence/validacion/configuracion.png) · [Conversación](../evidence/validacion/conversacion.png).

En el escritorio interactivo se sintetizó un WAV en español y se reconoció desde archivo con confianza 0,716. El ensayo aislado funcionó con texto ante la ausencia de dispositivos de voz accesibles. [Sondeo de voz](../evidence/voz-windows/host.txt).

## Recursos

CPU respecto de la capacidad total de 12 procesadores lógicos. Cada fase duró aproximadamente 8 segundos. RAM es el conjunto de trabajo hacia el final de la fase.

| Escenario | CPU media | RAM |
| --- | ---: | ---: |
| Libre, con Diagnóstico abierto | 1,73 % | 163 MiB |
| Concentrado | 0,78 % | 169 MiB |
| Descanso | 0,52 % | 169 MiB |
| Interactivo, abriendo y cerrando Conversar | 1,33 % | 177 MiB |
| Oculto, tras abrir el panel | 0,03 % | 174 MiB |
| Sesión interactiva normal, Libre con panel cerrado | 1,00 % | 144,3 MiB |

La CPU en sesión normal quedó por debajo del 2 %. Los 144,3 MiB equivalen aproximadamente a 151,3 MB decimales. La memoria privada en esa muestra fue 132,4 MiB. El conjunto de trabajo superó los 150 MB durante la secuencia que abrió paneles.

## Cobertura pendiente

La prueba de seguimiento alimentó coordenadas al motor de expresiones. La interacción física con el cursor, el arrastre, el paso de clics y la selección manual del icono de bandeja requieren una prueba interactiva. No había segundo monitor para comprobar DPI mixto. La reproducción audible, el micrófono físico, el tema claro y el inicio automático no se probaron de extremo a extremo. El código usa proveedores locales y no incluye clientes de red. No se desconectó la laptop para validar el funcionamiento en modo avión.

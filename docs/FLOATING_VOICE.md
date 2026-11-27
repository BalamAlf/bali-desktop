# Floating Voice 1.3.2

## Prueba interactiva

Haz un clic sobre la cara de Bali y di **«Bali, ¿cuánta energía tienes?»**. Deja un silencio de alrededor de un segundo. Es un comando local y no consume DeepSeek. La respuesta debe oírse con la boca en movimiento, sin abrir el panel. Otro clic cancela la captura, el procesamiento o la reproducción. Arrastrar con el botón presionado mueve Bali. El clic derecho sigue abriendo su panel. Si está activo Ignorar clics, usa **Hablar con Bali / cancelar turno** en la bandeja.

Hablar desde Bali flotante autoriza esa captura puntual. El permiso de micrófono del panel conserva su valor y se aplica al botón del panel. En **Configuración → Voz** puedes seleccionar el dispositivo y ajustar el silencio. **Conversación continua** está desactivada por defecto. Una vez activada, Bali abre otra captura 650 ms después de completar la voz. Una espera sin voz finaliza la sesión según el plazo configurado. La espera inicial permite seis segundos y cada captura tiene un tope de veinte segundos.

Las preguntas abiertas pueden consultar DeepSeek si ya está habilitado, verificado y dentro de su presupuesto. Se usa un turno del mismo servicio y los mismos controles de contexto. El historial local de la sesión se conserva entre preguntas. El envío de turnos previos respeta el ajuste existente de contexto compartido.

## Mecanismo y permisos

La ruta flotante mantiene ocho buffers WinMM de 80 ms y reutiliza el micrófono que seleccionó el usuario. Mide energía RMS y picos sobre muestras PCM con un piso de ruido adaptativo y exige unos 190 ms de señal antes de aceptar voz. Una pausa ordinaria de 1,15 s termina la captura. Esta heurística puede verse afectada por el volumen, las pausas y el ruido ambiental. Las capturas vacías e impulsos aislados se descartan. El reconocedor local aplica un umbral de confianza mayor en el flotante y las preguntas abiertas requieren al menos dos palabras. La aplicación libera el dispositivo antes de transcribir y nunca lo abre en el seguimiento hasta que termina la voz. Los bloques de audio se mantienen en memoria y se descartan al terminar.

Se comprobó capturar, liberar y reabrir el dispositivo predeterminado en la sesión de Windows. Windows enumeró un arreglo Realtek y el reconocedor `es-ES`. No se modificaron los permisos del sistema ni se instaló otro idioma. Si el dispositivo se desconecta o Windows deniega el acceso, el turno finaliza. La precisión con voz humana, incluido español de México en el reconocedor `es-ES`, queda pendiente de la prueba interactiva.

## Evidencias

Pasaron 119 comprobaciones, incluidas las 112 anteriores. Se probaron el detector de silencio con PCM controlado, clic frente a arrastre, umbrales y estado inicial. La captura física entregó 35 bloques y 90.498 bytes PCM que se descartaron. El sondeo de entrada, como proceso independiente de 3,49 s, registró 0,04 % de CPU y 30,0 MiB de memoria residente. [Pruebas automatizadas](../evidence/unit.txt) · [Entrada física](../evidence/floating-voice-1.3.2/entrada.txt)

En WPF, con el panel cerrado, se abrió y canceló el micrófono desde ambos estilos. Se inyectaron dos transcripciones para probar por separado las respuestas locales, la reproducción real de Helena, los visemas, la continuidad, la ausencia de captura durante el audio y la cancelación del seguimiento. No se creó un proveedor HTTP durante esas pruebas. El primer turno de la pasada final duró 4,3 s y promedió 0,75 % de CPU, con 125,6 MiB de memoria residente y 113,0 MiB de memoria privada. Una ejecución previa tardó más y no cumplió la comprobación compuesta del primer turno. La repetición agregó detalle para distinguir una eventual recurrencia y completó los dos turnos. El umbral de arrastre se validó por código. La precisión de voz humana y el gesto de arrastre con un ratón físico se pueden comprobar en la sesión interactiva. [Pasada final WPF](../evidence/floating-release-1.3.2/runtime.txt) · [Ejecución previa](../evidence/floating-final-1.3.2/runtime.txt)

La credencial, el consumo y el archivo persistente de recuerdos quedaron sin cambios durante la actualización. [Recuperación de 1.3.1](../recovery/1.3.1/README.md)

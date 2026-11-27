# Motor de vida artificial 1.1

`BaliLifeEngine` usa un reloj de simulación de aproximadamente 450 ms mientras Bali está visible y hasta 750 ms cuando está oculto. El renderizado sigue su propia cadencia. El motor recibe tiempo efectivo, hora civil local derivada del reloj UTC, inactividad de Windows, proximidad del cursor a Bali, visibilidad, preferencias y modo activo. La semilla aleatoria se puede fijar para las pruebas.

## Necesidades, estados y rasgos

- **Necesidades**: energía, estimulación, atención, descanso e interacción.
- **Estados simulados**: ánimo, curiosidad, satisfacción y activación.
- **Rasgos persistentes**: sociabilidad, curiosidad habitual, actividad, exploración y tranquilidad.

Los valores se acotan entre 0 y 1 y se acercan gradualmente a sus objetivos. Por ejemplo, mientras Bali está despierto, la energía tiende a `0,24 + 0,10 × sesgo circadiano`. Al descansar, tiende a 0,94 con una recuperación más rápida. Una interacción aumenta la satisfacción en 0,11 y reduce la necesidad de interacción en 0,18. Los rasgos cambian con muestras estadísticas, con un peso de 0,001 a 0,0015 por muestra. Un contacto por sí solo modifica los estados inmediatos.

## Decisiones

Concentrado impide iniciativas y Descanso aplica su política de reposo. La inactividad prolongada y los objetivos de energía determinan cuándo Bali se retira y se recupera. Una señal explícita prioritaria, como la escucha, conserva su tiempo de expresión. Las evaluaciones consideran calma, observación, exploración, saludo, invitación y microexpresión. La pausa por acción excluye al candidato. Después queda una penalización por repetición de hasta 0,22 que decae a cero al transcurrir tres pausas. Los candidatos elegibles reciben ruido reproducible de hasta 0,07. Gana la mayor puntuación. Un saludo tiene un peso alto al regresar. Una invitación pondera la necesidad de interacción, sociabilidad, activación y nivel de autonomía, y pierde puntuación con inactividad reciente o una franja habitualmente concentrada.

La pausa entre invitaciones se ajusta entre 12 y 300 segundos. Una invitación dura dos segundos y es visual. Una interacción en los 18 segundos siguientes se cuenta como respuesta aproximada, sin inferir intenciones. La exploración puede solicitar un movimiento si el usuario lo habilitó. El control de autonomía en cero bloquea iniciativas y movimiento autónomo. Las reacciones explícitas, el parpadeo y el descanso de base siguen operativos.

## Aprendizaje y ritmo diario

Cada minuto real observado agrega una muestra a la hora local correspondiente. Cada franja conserva observaciones activas e inactivas, distribución de modos, contactos y respuestas aproximadas. Al llegar a 120 muestras en una franja, las cuentas se dividen entre dos para dar peso a observaciones recientes. La confianza `n / (n + 12)` mide cobertura de muestra. El sesgo circadiano `(2 × muestras activas − n) / (n + 12)` vale cero con una franja vacía. Se atenúan las iniciativas con `0,35 × muestras en Concentrado / (n + 12)`. La sociabilidad incorpora la frecuencia suavizada de contactos en todas las franjas y las respuestas a invitaciones. El usuario conserva el control de los modos.

Ante una discontinuidad de más de dos minutos, los estados recuperables se aproximan analíticamente hacia sus niveles de reposo. Se descartan las muestras parciales y la pausa no se contabiliza como ejecución ni se añade a los hábitos. La aplicación descarta también intervalos superiores a cinco segundos entre pulsos. La fecha original del perfil migrado figura como desconocida.

## Memoria y simulación

La memoria guarda hasta 80 episodios. Los eventos iguales de una ventana de una hora se agrupan. Los eventos de relevancia baja o media caducan después de 90 días al registrar el siguiente acontecimiento. Cuando se llena la memoria, se retiran primero los menos relevantes y más antiguos. Se conservan hasta 60 registros diarios para la gráfica y las consultas recientes. La edad digital parte de la primera observación fiable, mientras que la ejecución y los días observados se cuentan por separado.

El simulador usa el mismo motor con un perfil nuevo en memoria y pasos de 10 segundos. La semilla gobierna las iniciativas y el escenario. El perfil experimental puede usar horario fijo o variable, concentración, ausencia y distintos niveles de contacto y actividad. Los resultados no se guardan en el perfil activo.
# Cognitive Life 1.2

## Decisión y consecuencias

BaliLearningEngine mantiene una estimación incremental de recompensa por acción y contexto. Utility AI sigue aportando necesidades, hora, hábitos, reglas de modo y cooldown. El perfil 1.1 puede ejecutarse como control sin las contribuciones cognitivas. Un segundo control retiene las motivaciones 1.2 y desactiva las consecuencias.

1. El motor observa proximidad al rostro, inactividad de Windows y energía. Agrupa esa información en seis contextos: cerca, activo a distancia o sin presencia reciente, cada uno con energía baja o suficiente.
2. Filtra los comportamientos por restricciones, prioridad y cooldown. Concentrado sigue impidiendo iniciativas. El movimiento exige autorización expresa y un cursor distante. Las invitaciones respetan la pausa configurada y un tope de tres por tramo de treinta minutos de simulación.
3. Para los candidatos elegibles, suma a la utilidad base ruido controlado, motivación y preferencia. Resta 0,035 por invitación y 0,05 por movimiento.
4. Registra un único resultado atribuible por iniciativa. La ventana dura 18 segundos. Un clic directo sobre Bali cuenta como contacto débil desde los 650 ms si el contexto era cercano o la iniciativa era una invitación o saludo con presencia. Las consultas por texto y las interacciones sociales experimentales no se atribuyen como clics del escritorio.
5. Una iniciativa reemplazada o separada por una pausa larga queda como desconocida. Un cambio de modo, ocultación manual o cierre cancelan la iniciativa como interrumpida. Una ventana ordinaria sin contacto se registra como ausencia neutral.

| Resultado | Recompensa |
| --- | ---: |
| Me gustó / prefiero que no | +1 / −1 |
| Más frecuente / menos frecuente | +0,55 / −0,55 |
| Contacto directo contextual | +0,28 |
| Sin respuesta, interrupción o desconocido | 0 |

Cada muestra con recompensa actualiza el valor según `V ← 0,9 × V decaído + 0,1 × recompensa`, acotado a ±0,90. La evidencia efectiva crece con el valor absoluto de la recompensa hasta 32. Evidencia y valor previo se atenúan con semivida de 21 días aplicada desde el último refuerzo. Al estimar, se combina el valor local con el general. El peso local es `evidencia local / (evidencia local + 5)`. La confianza se obtiene con `e / (e + 6)`, donde `e` suma toda la evidencia local y la mitad de la general. La contribución aprendida es `0,30 × expectativa × confianza`.

La novedad aporta hasta 0,034, multiplicado por `1 / √(1 + observaciones)`, solo a opciones discretas cuando la energía supera 35 %. Una negativa repetida pesa más que la novedad y ningún refuerzo amplía el presupuesto de invitaciones. La proporción de contacto se muestra a partir de ocho observaciones entre contacto directo y ausencia de respuesta. Usa `(contactos + 1) / (observaciones + 4)`. Es una estimación observacional, no una probabilidad validada con usuarios.

## Motivaciones

Se evalúan cada dos segundos virtuales o al cambiar el contexto dominante. Energía baja, exceso de estimulación, curiosidad, necesidad de contacto, novedad y tranquilidad tienen umbral, prioridad y condición de satisfacción propios. Se elige la mayor intensidad ponderada con margen de 0,10 para desplazar un objetivo vigente. Un objetivo caduca a los 65 segundos, o a los 35 si busca interacción, y entra en una pausa de 12 segundos. En reposo o concentración quedan fuera las motivaciones de exploración y contacto. La contribución del objetivo se acota por acción como parte de la utilidad.

## Memoria y persistencia

El perfil agrega hasta 96 estadísticas asociativas y las últimas 36 experiencias. El contexto, fecha, relevancia, resultado y recompensa quedan disponibles en Vida artificial. Las predicciones se distinguen de la observación. Desactivar el aprendizaje conserva las estadísticas previas y descarta la iniciativa pendiente. Restablecer asociaciones limpia sus estadísticas, experiencias y respaldo rotativo sin borrar la identidad, los hábitos por hora, los episodios ni las interacciones anteriores.

La migración a esquema 2 guarda `estado.1.1.json` antes del primer reemplazo y conserva el mecanismo de recuperación del archivo anterior. El perfil no incorpora datos de otras aplicaciones.

## Laboratorio de agentes

Cada agente crea sus propios Settings, BaliLifeEngine, semilla, reloj de expresiones, hábitos, experiencias y relaciones. El entorno usa reloj virtual. Los agentes eligen compañero y propuesta a partir de rasgos, afinidad y variación sembrada. El receptor usa su propia disposición y su propio generador para responder o retirarse. Los turnos de iniciativa social rotan. La afinidad pondera aceptación y actividad compartida y descuenta retiros, mientras la confianza depende del número de encuentros. Las relaciones pertenecen exclusivamente al experimento.

El laboratorio admite seis perfiles y hasta 100 eventos sociales recientes. Los agentes en vivo se actualizan únicamente con su pestaña activa. Los experimentos acelerados ejecutan pasos de 15 segundos en un hilo de trabajo, sin dibujar rostros y con perfiles nuevos. Se pueden guardar resultados locales JSON de forma explícita. Los perfiles vivos son de sesión: la exportación guarda evidencia, no restaura el reloj ni reanuda el experimento.

Los escenarios sintéticos simplifican el contacto y la retroalimentación. El ensayo de 30 días compara una política de referencia 1.1, otra 1.2 congelada y otra 1.2 con consecuencias, con la misma semilla y definición inicial. En grupos sociales, las decisiones divergentes producen secuencias de encuentros distintas; la misma semilla no obliga a observar exactamente los mismos eventos. [Resultados](RESULTADOS_COGNITIVE_LIFE.md)
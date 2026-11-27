# Resultados de Cognitive Life 1.2

Validación realizada el 26 de noviembre de 2026 en la laptop Windows del proyecto.

## Pruebas ejecutadas

Pasaron las 44 comprobaciones anteriores y 28 nuevas. Se probaron recompensas positivas y negativas, preferencias de frecuencia, contacto atribuible, neutralidad de la ausencia, interrupción, decaimiento, incertidumbre, límites, motivaciones, concentración, restauración, migración, aislamiento, pausa, eliminación, reinicio experimental y simulaciones de 90 días. [Registro completo](../evidence/unit.txt)

En WPF se comprobaron los tres rostros simultáneos y diez encuentros sociales en un perfil aislado, con cero interacciones en el Bali principal. En la sesión interactiva se ejecutó la comparación de siete días con los tres perfiles y se volvió a dejar visible el laboratorio. El perfil migrado conservó 24 interacciones, su fecha inicial, familiaridad, modo Libre y estilo minimalista. La copia local `estado.1.1.json` coincidió byte a byte con el archivo previo. [Captura del laboratorio](../evidence/agentes-final-1.2/agentes.png) · [Registro de WPF](../evidence/agentes-final-1.2/runtime.txt)

## Comparación controlada

Se simuló un Bali Explorador durante 30 días con semilla 71. Durante las horas activas, el entorno consulta la acción y emite negativas explícitas ante exploración o microexpresión y positivas ante observación, en una fracción sembrada de las ocasiones. Las demás acciones no reciben retroalimentación en ese escenario. Las negativas se cuentan incluso en los controles que no las incorporan.

| Política | Negativas / ocasiones evaluadas | Proporción | Exploraciones | Observaciones |
| --- | ---: | ---: | ---: | ---: |
| 1.1, sin consecuencias | 41 965 / 52 661 | 79,69 % | 50 895 | 9 928 |
| 1.2, consecuencias desactivadas | 44 037 / 52 735 | 83,51 % | 53 257 | 9 922 |
| 1.2, consecuencias activadas | 77 / 52 719 | 0,15 % | 88 | 63 417 |

El control 1.2 conserva las motivaciones, el límite de invitaciones y el coste de interrupción. El agente que aprende mantuvo exploraciones y microexpresiones con menor frecuencia, y formó una preferencia por observar. La comparación es reproducible. La magnitud del cambio depende del escenario sintético y del volumen de señales explícitas, muy superior al esperable en el escritorio.

En 90 días con tres agentes y una inversión de las preferencias del entorno a mitad del periodo, las memorias siguieron separadas y acotadas. El resultado de afinidad fue +0,26 entre B y C, −0,17 entre A y B y −0,18 entre A y C. Son estimaciones de interacciones simuladas. [Comparaciones completas](../evidence/experimentos-1.2.txt) · [Datos exportados](../evidence/experimentos-1.2.json)

## Rendimiento 1.1 frente a 1.2

Se usaron la misma laptop, el mismo perfil aislado inicial vacío y la misma secuencia de WPF. Cada fase duró 45 segundos. El laboratorio de Vida artificial se abrió y cerró al inicio. Antes de medir cada fase se dejaron unos 12 segundos para calentamiento. La CPU es la media de 25 segundos sobre 12 procesadores lógicos y la memoria es la muestra al final de ese intervalo. El laboratorio multiagente quedó fuera de esta comparación. Interactivo incluyó la misma acción de texto y el panel de conversación en ambas versiones.

| Modo | CPU 1.1 / 1.2 | Conjunto de trabajo 1.1 / 1.2 | Privada 1.1 / 1.2 |
| --- | ---: | ---: | ---: |
| Libre | 0,505 / 0,468 % | 169,5 / 162,4 MiB | 148,7 / 149,4 MiB |
| Concentrado | 0,271 / 0,193 % | 171,0 / 164,8 MiB | 149,2 / 150,0 MiB |
| Descanso | 0,177 / 0,156 % | 171,1 / 161,7 MiB | 149,6 / 145,2 MiB |
| Interactivo | 0,484 / 0,521 % | 174,4 / 165,9 MiB | 153,6 / 150,3 MiB |
| Oculto | 0,005 / 0,000 % | 174,5 / 165,9 MiB | 153,1 / 149,9 MiB |

La memoria privada permaneció en el mismo orden y el conjunto de trabajo fue menor en las cinco muestras de 1.2. La diferencia entre memoria privada y residente muestra que las lecturas históricas de conjunto de trabajo no bastan para diagnosticar una fuga. Hacen falta muestras largas y repetidas para evaluar esa hipótesis. Ambas ejecuciones por fases terminaron con código 0 y 1.2 restauró el perfil aislado al reiniciar. [Datos 1.1](../evidence/baseline-1.1/resources.csv) · [Datos 1.2](../evidence/comparacion-1.2/resources.csv) · [Reinicio 1.2](../evidence/comparacion-1.2/runtime.txt)

Con los tres agentes animados y el laboratorio visible, el proceso final registró 1,71 % de CPU media, 167,4 MiB de conjunto de trabajo y 155,5 MiB de memoria privada durante 30 segundos en el escritorio del usuario. Este escenario adicional no se equipara con el panel de Vida artificial de 1.1. [Muestra del laboratorio](../evidence/agentes-final-1.2/sesion-recursos.csv)

## Límites de las pruebas

La estimación de contacto requiere observaciones atribuibles. La confianza describe la evidencia estadística interna. La reducción de negativas no se validó con usuarios reales. Los controles de pausa y eliminación se verificaron contra el motor. No se repitieron pruebas de micrófono físico, suspensión real ni monitores adicionales. El guardado de resultados exporta JSON y los agentes abiertos pertenecen a la sesión.

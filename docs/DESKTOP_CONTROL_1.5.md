# Bali Desktop 1.5 — Desktop Control

## Prueba interactiva

- «¿Qué es una VPN?» prueba el vocabulario técnico y el recorrido habitual de DeepSeek. También puedes decir «uve pe ene». La consulta sigue los permisos y límites existentes.
- «¿Qué aplicaciones tengo instaladas?» consulta el catálogo. «Busca una aplicación llamada Git» busca coincidencias. Las aperturas aproximadas y las instrucciones dudosas requieren confirmación. Si hay varias coincidencias, Bali pregunta cuál.
- «Abre Visual Studio» permite comprobar la respuesta si falta una instalación. La sonda encontró Brave, Access, Excel, PowerPoint, Word, Calculadora, Bloc de notas y Microsoft Store entre 94 entradas. Visual Studio, Visual Studio Code, Obsidian, Chrome y Git no aparecieron. «Abre Calculadora» permite probar una alternativa instalada.
- «¿Cuánta RAM estoy utilizando?» consulta el equipo localmente. Bali también puede medir CPU, procesos, unidades fijas, tiempo activo y batería. Una muestra aporta observaciones y carece de evidencia para determinar por sí sola una causa de lentitud.
- «Analiza los archivos temporales» es de solo lectura. «Limpia los archivos temporales» analiza y pide confirmar ese conjunto. La prueba real encontró cero candidatos y no se ejecutó una limpieza.

## Vocabulario y catálogo

Windows incorpora ejemplos de siglas y nombres o alias autorizados en su gramática. Whisper conserva Tiny. Las transcripciones de ambos pasan por equivalencias contextualizadas de frases completas. El diccionario se edita en **Sistema** y no cambia los umbrales ni asigna confianza a Whisper.

Se consultan accesos directos del menú Inicio sin argumentos, App Paths, entradas de instalación con destino verificable y AppsFolder para Microsoft Store y MSIX. Las rutas y los identificadores se deduplican independientemente del nombre. El panel muestra el icono asociado de ejecutables clásicos cuando está disponible. Se omitieron entradas sin un destino usable y no se usó Win32_Product. [Registro de aplicaciones](https://learn.microsoft.com/en-us/windows/win32/shell/app-registration) · [Identificadores AUMID](https://learn.microsoft.com/en-us/windows/configuration/store/find-aumid)

Las aplicaciones descubiertas necesitan permiso. Para una aplicación instalada, Bali puede pedir por voz una autorización inicial identificando la aplicación. Los portátiles se autorizan en **Aplicaciones**. La apertura valida la ruta y la huella del ejecutable, evita abrir otra instancia si ya existe una ventana identificable y diferencia inicio de proceso de activación de una aplicación empaquetada. Se pueden registrar hasta 16 carpetas portátiles específicas. La indexación tiene límites de profundidad y cantidad, se actualiza cada 30 minutos fuera de conversación y se puede lanzar o cancelar desde el panel. Se pueden revocar carpetas, excluir aplicaciones y guardar alias y proyectos.

Solo se solicita cierre normal de aplicaciones autorizadas con proceso y ventana verificables. Se comprueba hora de inicio, identidad y ruta, y se excluyen el propio Bali, ejecutables de Windows y nombres de componentes protegidos. Windows puede mostrar un aviso de guardado. Las ventanas empaquetadas sin ruta clásica verificable quedan fuera de la consulta y del cierre.

## Mantenimiento y rutinas

La limpieza se limita a archivos regulares en la raíz de %LOCALAPPDATA%\Temp con patrones temporales concretos y al menos 14 días desde creación, modificación y último acceso informado. Se excluyen subcarpetas, enlaces, archivos bloqueados, de sistema o de solo lectura. Quedan fuera proyectos, datos personales, perfiles de navegador, cachés de aplicaciones y temporales globales.

El análisis caduca en 60 segundos. Se compara de nuevo el conjunto antes de borrar y se valida la ruta final y la identidad del mismo archivo que se marca para eliminar. El historial guarda cantidades y tamaño lógico estimado. El panel muestra hasta veinte nombres del análisis vigente. El tamaño lógico puede diferir del espacio recuperado.

Las rutinas permiten abrir aplicaciones autorizadas, abrir carpetas registradas, consultar agenda y activar Concentrado, con resultados por paso. Ejemplos de voz son «crea una rutina Desarrollo», «añade a la rutina Desarrollo consultar pendientes», «ejecuta la rutina Desarrollo» y «elimina la rutina Desarrollo». En **Sistema** se pueden renombrar y editar los pasos. Los tipos admitidos carecen de capacidad para borrar archivos o cerrar ventanas. Las sugerencias de poco espacio son opcionales, como máximo una por hora en modos compatibles, y se calculan localmente.

## Validación y rendimiento

Pasaron 185 comprobaciones automatizadas y 17 verificaciones flotantes, incluidas las 172 y 16 anteriores. La limpieza usó archivos sintéticos para verificar cambios, bloqueos, caducidad y cancelación. La sonda nativa verificó una junction excluida y la autorización, apertura y cierre normal de un portátil de prueba. El flujo flotante combina micrófono y Helena reales con texto reconocido y transporte de DeepSeek simulados. Falta comprobar las cinco frases con la voz del usuario.

En la sonda final de Windows, la indexación tardó 1.16 s, con 0.91 s de CPU y 54 MiB residentes en el proceso de diagnóstico. El análisis de solo lectura tardó unos milisegundos y el muestreo de CPU y procesos alrededor de 0.5 s. La rutina de prueba tardó 210 ms, con un incremento de 0.4 MiB. En reposo, Bali flotante midió 0.78 % de CPU total y 151.7 MiB residentes. Las cifras dependen de la carga del equipo.

Al reproducir una muestra sintética con «uve pe ene», Windows devolvió «qué es una VPN» en 0.38 s. Whisper devolvió «que es una uve bene» en 0.96 s, con un pico de 145.6 MiB en el proceso auxiliar. El ajuste posterior normaliza «uve bene» solo en preguntas como «qué es una» o «para qué sirve una». La pregunta breve de Whisper sigue sujeta a confirmación verbal. Pasaron 187 comprobaciones con este ajuste. La prueba no mide la precisión con voz humana.

Al pedir permiso para una aplicación, Windows usa una gramática centrada en la confirmación mientras conserva el dictado libre para distinguir frases ajenas. Acepta «sí» y «sí autorizo» con el umbral de autorización existente. Espera otra respuesta si la frase no confirma ni cancela. En la prueba sintética de Helena se aceptaron las tres afirmaciones y se rechazó autorización para las dos negativas y la frase ajena. Pasaron 188 comprobaciones. [Prueba de confirmación](../evidence/desktop-approval/approval.txt)

Para una apertura pendiente, Bali pregunta «¿Abro [aplicación]? Di sí o no». Si Windows no obtiene una confirmación clara, Bali revisa el mismo audio con Whisper. Un «sí» aislado se admite con actividad vocal detectada si Windows también entendió una afirmación o no devolvió texto. Una negativa reconocida prevalece. Este respaldo se limita a la apertura de la aplicación que Bali acaba de mencionar.

Si la autorización hablada sigue fallando, selecciona la aplicación en **Aplicaciones**, activa **Abrir por voz con el nombre exacto, sin volver a confirmar** y pulsa **Autorizar y guardar alias**. El ajuste se guarda por aplicación. Requiere una coincidencia única y exacta del nombre o alias, y conserva la comprobación de identidad al arrancar. Las coincidencias aproximadas siguen pidiendo confirmación. Pasaron 190 comprobaciones.

El catálogo actualiza la lista cuando concluye la indexación aunque el panel ya esté abierto. **Mostrar solo aplicaciones autorizadas** permite consultar los permisos activos, y el contador distingue permisos guardados de entradas disponibles. Cuando Windows publica el mismo ejecutable mediante la ruta del paquete y su identificador estable, Bali presenta una sola opción si la relación es verificable. Los permisos existentes conservan sus identificadores. Pasaron 191 comprobaciones.

### Comparación con Whisper Base

Whisper Base multilingüe q5_1 está disponible como motor local opcional. El usuario autorizó solo la descarga de Base. Se comprobó la huella oficial SHA-1 A3733EDA680EF76256DB5FC5DD9DE8629E62C5E7. Se conserva Tiny, y el motor seleccionado sigue siendo el anterior hasta cambiarlo en **Voz**. [Distribución oficial](https://huggingface.co/ggerganov/whisper.cpp)

En **Voz → Capturar y comparar Whisper Tiny / Base**, ambos reciben el mismo audio de prueba en RAM. Con Helena, Base mejoró la frase coloquial y empató con Tiny en «sí, autorizo». Ninguno de los dos reconoció bien «Abre Brave» y Base escribió «WPN» en la prueba de VPN deletreada. Base tardó cerca de dos segundos en esos casos y su proceso rondó los 211 MiB. Tiny tardó alrededor de un segundo y rondó los 145 MiB. Falta comparar con la voz del usuario. Pasaron 192 comprobaciones y se verificó la cancelación. [Resultados completos](../evidence/whisper-base/stt-comparison.txt)

Para Word, se admiten «Abre Microsoft Word», «Abre guord» y «Abre el editor de Microsoft» solo como comandos de apertura. Si Whisper detecta una orden de apertura sin un destino exacto y autorizado, Bali prueba Windows con el mismo audio y solo recupera el comando si obtiene una coincidencia exacta, autorizada y con confianza suficiente. Pasaron 193 comprobaciones.

[Catálogo, recursos y pruebas nativas](../evidence/desktop-15-release/desktop.txt) · [Flujo flotante](../evidence/floating-15-release/runtime.txt) · [Comparación técnica de voz](../evidence/desktop-15-release/stt-comparison.txt) · [Reposo](../evidence/desktop-15-release/idle.txt) · [Ejecutable 1.5](../dist/BaliDesktop.exe) · [Copia 1.4.1](../recovery/1.4.1-before-desktop/)

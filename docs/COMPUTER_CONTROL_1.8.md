# Bali Desktop 1.8 — Visual Intelligence 2.0 & Computer Control

## Línea base

La compilación instalada de 1.7 tenía 211 pruebas aprobadas. Conservaba las herramientas de escritorio de 1.5, el contexto y las iniciativas de 1.6, y lectura bajo demanda con UI Automation y Windows OCR. Se conservó una copia de ese ejecutable con sus dependencias.

Al repetir 1.7 en la misma laptop, UI Automation leyó un error C#, SQL, texto de documento y el botón Aceptar. Tardó 194 ms. La captura tardó 46 ms y Windows OCR 74 ms. El pico del proceso de diagnóstico fue 67.9 MiB. El texto era legible. La pérdida de estructura ocurría principalmente al convertir controles y OCR a listas de nombres y líneas sin coordenadas. La evidencia no permite atribuirla a la resolución ni a DeepSeek. [Línea base repetida](../evidence/control-18-baseline/vision.txt)

La laptop ejecutaba Windows 11 Home, con Ryzen AI 5 340 de seis núcleos y Radeon 840M integrada. En la muestra había aproximadamente 15.1 GiB de memoria visible y 3.0 GiB libres. DirectML requiere DirectX 12, un controlador compatible y un grafo exportado con operaciones admitidas. La GPU es una candidata para esa vía. No se midió aceleración de los modelos en este equipo.

## Tecnología seleccionada

| Opción | Evaluación | Decisión |
| --- | --- | --- |
| UI Automation | Expone tipo, posición, identidad de ejecución, nombre y estado de controles. La cobertura depende de la aplicación. | Motor de percepción estructurada y acciones accesibles verificadas. |
| Windows OCR | Conserva el texto y la geometría que informa cada línea. Ya funcionaba localmente en es-MX. | Apoyo para el texto de una captura. Las líneas no se convierten en controles. |
| OmniParser | Diseñado para iconos y elementos de pantalla mediante detector y etiquetado. Versiones de pesos con MIT o AGPL según el detector. Requiere sus pesos y ejecución adicional. | Evaluado por documentación. |
| PaddleOCR | El reconocedor latino móvil publicado ocupa unos 14 MB, aparte de detección y runtime. La licencia del proyecto es Apache 2.0. | Candidato futuro para OCR con idiomas y geometría adicionales. |
| Florence-2-base | Modelo MIT de 0.23 mil millones de parámetros con OCR por regiones, detección y descripción. Eso supone unos 460 MB de pesos en FP16 como cálculo base, antes del runtime y buffers. | Sin descarga ni inferencia medida. |
| Qwen2.5-VL 3B | Añade razonamiento sobre imágenes. Incluso una cuantización de cuatro bits presupone unos 1.5 GB para tres mil millones de pesos antes del proyector y buffers. | Fuera de la entrega. |
| ONNX Runtime / DirectML | Puede aprovechar GPU AMD compatible en Windows. Requiere exportación y verificación por modelo. | Sin dependencias adicionales en la compilación. |

[Patrones UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview) · [OmniParser](https://github.com/microsoft/OmniParser) · [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) · [Tamaños de PaddleOCR](https://paddlepaddle.github.io/PaddleX/latest/en/pipeline_usage/tutorials/ocr_pipelines/OCR.html) · [Florence-2](https://huggingface.co/microsoft/Florence-2-base) · [Qwen2.5-VL](https://huggingface.co/Qwen/Qwen2.5-VL-3B-Instruct) · [DirectML](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html)

El análisis mantiene la identidad, tipo, estado, posición y origen de cada elemento. Puede ordenar visualmente pestañas, resolver nombres exactos y detectar empates para «arriba», «abajo», «izquierda» y «derecha». Los elementos que UI Automation etiqueta como imágenes aparecen por su etiqueta accesible. La versión carece de clasificación inferida de iconos sin etiqueta. Frente a 1.7 se comprobó una segunda pestaña con posición e identidad y líneas OCR con coordenadas. No hay una medición de precisión general sobre un corpus de aplicaciones ni comparaciones de latencia local con modelos descargados.

## Control y dictado

En **Bandeja de Bali → Control de escritorio** puedes activar por separado ventanas, controles y dictado. Los permisos nuevos están apagados inicialmente.

- **Ventanas:** activar, regresar a la anterior, minimizar, maximizar, restaurar y colocar a la izquierda o a la derecha. Bali conserva ventanas observadas al interactuar. Si hay varias coincidencias de nombre, pide enfocar una. Windows puede limitar el cambio de foco. El resultado distingue una acción verificada de una petición sin confirmación.
- **Controles:** selección de pestañas, expansión o contracción de menús y desplazamiento cuando Windows expone el patrón correspondiente. Un control referenciado se busca en la observación y se vuelve a comprobar antes de actuar. La invocación genérica de botones, toggles con efectos desconocidos, apertura de archivos basada únicamente en una posición y clics por coordenadas quedan bloqueados.
- **Dictado:** en Bloc de notas enfocado, di «Escribe lo siguiente». Bali conserva un borrador temporal y admite «Corrige la última palabra», «Selecciona el último párrafo», «Agrega un punto y aparte», «Copia el borrador» y «Pega en el borrador». «Termina el dictado» solicita permiso antes de insertar al final del campo. Se verifica la ventana, el campo y el contenido original. Si cambia alguno, el borrador queda disponible y se suspende la escritura. La inserción real se verificó en un editor WPF sintético. La compatibilidad con el Bloc de notas instalado queda pendiente de prueba manual.

La ejecución de controles ignora texto de la pantalla como fuente de instrucciones. Una operación requiere una orden local del usuario, una capacidad habilitada y un destino válido. El registro guarda el tipo, resultado y latencia, sin almacenar contenido dictado. Si se pide **Detener control o dictado**, Bali deja de escuchar y cancela lo pendiente. Una solicitud que Windows ya recibió puede haber surtido efecto y no se repite. DeepSeek conserva su integración de texto ya aprobada en Visión. Aunque Flash documenta soporte de imágenes, el modo de control carece de transporte de imágenes y acceso externo a los controles. [DeepSeek Vision](https://api-docs.deepseek.com/guides/vision/)

## Pruebas interactivas

Con Visión ya habilitada, enfoca una ventana de prueba y di «¿Qué estoy viendo?» o «Explícame esta ventana». Habilita administración de ventanas y prueba «Minimiza esta aplicación». Tras usar Bali en dos ventanas, pide «Cambia a la ventana anterior». Habilita controles y di «Desplázate hacia abajo» en una ventana con una sola área desplazable. Para dictado, comienza en un documento nuevo de Bloc de notas y prueba «Escribe lo siguiente», «Corrige la última palabra» y «Termina el dictado».

La validación de voz usó audio sintético, no una grabación personal. Detectó que Base omitía la «a» de «Cambia a la ventana anterior». Se incorporó esa variante exacta sin ampliar la autorización de destinos. En las muestras sintéticas, Base usó alrededor de 211 MiB en su proceso auxiliar y demoró cerca de dos segundos. Se conservan los motores actuales, Helena y los visemas. La prueba de voz humana, ruido ambiental, las aplicaciones de uso diario y la inserción en el Bloc de notas siguen pendientes.

Pasaron 220 comprobaciones automatizadas, incluidas las 211 de 1.7, y 20 verificaciones flotantes. En las ventanas WPF sintéticas se verificaron cambios de ventana, selección, desplazamiento y dictado con escritura real. Los casos de foco cambiado, campo protegido, ventana excluida y control inexistente quedaron bloqueados. Tres operaciones seguidas de maximizar, minimizar y restaurar tardaron en total 54 ms. UI Automation tardó 175 ms, la captura 41 ms y el OCR 85 ms. El proceso de análisis visual alcanzó 67.9 MiB, igual que en la línea base repetida. Ninguna prueba generó cargos.

Se verificó que Bali 1.8 quedó flotando con todos sus paneles cerrados. La muestra breve en reposo registró 0.65 % de CPU del equipo y 157.2 MiB residentes. La agenda, los permisos de escritorio y Visión, la credencial y el registro de consumo conservaron sus huellas. [Medición de reposo](../evidence/control-18-final/idle.txt)

[Ventanas, controles y dictado sintético](../evidence/control-18-final/computer.txt) · [Análisis visual](../evidence/control-18-final/vision.txt) · [Reconocimiento sintético](../evidence/control-18-final/visual-speech.txt) · [Conversación flotante](../evidence/control-18-final/runtime.txt) · [Ejecutable 1.8](../dist/BaliDesktop.exe) · [Recuperación 1.7](../recovery/1.7-before-control/)

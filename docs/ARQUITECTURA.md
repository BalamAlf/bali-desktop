# Arquitectura

**Voice Intelligence 1.3** separa comandos locales, contexto autorizado, proveedor HTTP asíncrono, salida de voz y audio local. Los archivos de memoria conversacional, credencial DPAPI y consumo son independientes del perfil cognitivo.

**Cognitive.cs** integra estimación incremental, motivaciones y resultados con el motor existente. **Agents.cs** ejecuta perfiles separados en un reloj virtual, y **AgentPanel.cs** presenta la comparación y exporta los resultados. El esquema 2 conserva una copia de entrada `estado.1.1.json`.

**Adaptive.cs** integra decisiones ponderadas, hábitos por hora, memoria episódica y rasgos en BaliLifeEngine. **Lab.cs** presenta datos del motor. **Simulation.cs** crea perfiles independientes. El reloj de simulación y el redibujado tienen cadencias separadas. El esquema 1.1 conserva una copia exacta del perfil anterior, sincroniza el temporal antes de reemplazar el perfil y deja una versión anterior para recuperación.

- **Life.cs** contiene `BaliLifeEngine`, estado persistente, contrato de conversación y proveedor de reglas locales. El motor de vida no depende de WPF, del sistema de entrada ni de voz.
- **Face.cs** contiene `BaliExpressionEngine` y el elemento WPF que dibuja el rostro vectorial.
- **App.cs** coordina el reloj, la ventana transparente, la bandeja y los ajustes de cadencia. **Panel.cs** construye los controles.
- **Platform.cs** encapsula posición en píxeles, DPI, inactividad, persistencia JSON y los componentes instalados de System.Speech.
- **Tests.cs** contiene pruebas deterministas, un sondeo de voz y una validación por fases en ventanas WPF reales.

El SDK moderno de .NET no estaba disponible. El compilador x64 de .NET Framework y WPF estaban preinstalados. El script compila con referencias locales y el ejecutable usa el runtime del sistema. Un único perfil JSON es suficiente para el volumen de datos. La escritura usa un archivo temporal y un reemplazo.

## Integración futura con Bali Brain

`IBaliIntelligenceProvider` expresa el límite actual entre conversación y presentación. Recibe texto y estado de Bali y devuelve una respuesta con texto e intención. La aplicación controla la ejecución de esas intenciones locales. Para integrar Bali Brain, el contrato se ampliará con operaciones asíncronas, cancelación y una proyección mínima de los datos autorizados. Bali Brain decidirá y autorizará las operaciones inteligentes y externas. Bali Desktop presentará sus resultados y seguirá ejecutando su simulación visual local.

## Datos

El archivo de estado conserva preferencias, posición, tamaño, modo, tiempo activo, contadores, familiaridad y fecha de la última interacción. El registro técnico local anota problemas de componentes. La ventana consulta coordenadas del cursor y segundos desde la última entrada. Las consultas se usan en memoria para orientar los ojos o descansar. El reconocimiento se activa una vez por botón y procesa un comando con la gramática local.

[Referencia de System.Speech en Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.speech.recognition.speechrecognitionengine.-ctor?view=netframework-4.8.1)
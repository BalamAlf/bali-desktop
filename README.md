# Bali Desktop

**Bali Desktop 1.8 — Visual Intelligence 2.0 & Computer Control.** En **Visión**, revisa posiciones y estados de controles. En **Control de escritorio**, habilita por separado ventanas, controles y dictado. [Resultados, límites y prueba interactiva](docs/COMPUTER_CONTROL_1.8.md)

**Bali Desktop 1.7 — Visual Intelligence.** Activa **Visión** para leer bajo demanda ventanas visibles con UI Automation y, con permisos adicionales, capturar y usar OCR local. [Guía, pruebas y privacidad](docs/VISUAL_INTELLIGENCE_1.7.md)

**Bali Desktop 1.6 — Proactive Intelligence & Voice 2.0.** Bali puede recordar temporalmente el tema de una pregunta, aplicar correcciones aprobadas por contexto y sugerir acciones locales al habilitar sus controles. [Resultados y prueba interactiva](docs/PROACTIVE_VOICE_1.6.md)

**Bali Desktop 1.5 — Desktop Control.** Consulta aplicaciones y recursos, analiza temporales y ejecuta rutinas autorizadas desde Bali flotante. Los permisos, alias y el diccionario técnico se administran en **Aplicaciones** y **Sistema**. [Guía, pruebas y limitaciones](docs/DESKTOP_CONTROL_1.5.md)

**Bali Desktop 1.4.1 — Voice & Natural Understanding.** En **Voz**, prueba el micrófono y compara Windows Speech con Whisper Tiny usando una sola captura local. Desde Bali flotante puedes dictar recordatorios y completar el horario en un segundo turno. [Diagnóstico, resultados y prueba interactiva](docs/VOICE_NATURAL_1.4.1.md)

**Bali Desktop 1.3 — Voice Intelligence.** Abre **Inteligencia** para habilitar DeepSeek a petición, guardar la credencial y autorizar la prueba controlada. **Conversar** conserva los comandos locales, añade historial de sesión y voz, y permite aprobar recuerdos. [Guía de voz e inteligencia](docs/VOICE_INTELLIGENCE.md) · [Pruebas](docs/RESULTADOS_VOICE_INTELLIGENCE.md)

**Bali Desktop 1.2 — Cognitive Life.** En **Configuración → Vida artificial** puedes valorar iniciativas, consultar predicciones y restablecer asociaciones. En **Agentes** están los tres perfiles iniciales, la comparación acelerada de políticas y la exportación local de resultados. [Aprendizaje cognitivo](docs/APRENDIZAJE_COGNITIVO.md) · [Pruebas y rendimiento](docs/RESULTADOS_COGNITIVE_LIFE.md)

Bali Desktop 1.1 incorpora Adaptive Life. Abre **Configuración → Vida artificial** para consultar necesidades, rasgos, decisiones y hábitos o probar el simulador. Allí puedes ajustar la autonomía y desactivar o restablecer el aprendizaje.

Bali es un compañero animado para el escritorio de Windows. Abre [BaliDesktop.exe](dist/BaliDesktop.exe) con doble clic. Requiere Windows x64 con .NET Framework 4.8.1, que está instalado en el equipo de validación.

- Arrastra el rostro para moverlo. Haz clic derecho sobre él para abrir los controles.
- Usa el icono de Bali en la bandeja de Windows para mostrarlo, ocultarlo, cambiar de modo o salir. Si Windows agrupa los iconos, búscalo en el menú de iconos ocultos.
- En **General** puedes ajustar el tamaño y la opacidad o activar **Ignorar clics en Bali**. Puedes recuperar el control desde **Ignorar clics** en la bandeja.
- En **Diagnóstico**, selecciona una expresión o pulsa **Reproducir las 15**.
- En **Habla**, reproduce una frase local y compara la sincronización labial de los dos estilos. [Expressive Voice 1.3.1](docs/EXPRESSIVE_VOICE.md)
- Haz clic en Bali flotante para hablar y vuelve a hacer clic para cancelar. La bandeja permite hablar con los clics desactivados. [Floating Voice 1.3.2 y prueba interactiva](docs/FLOATING_VOICE.md)
- Crea recordatorios, consulta pendientes y abre aplicaciones autorizadas desde Bali. [Personal Intelligence 1.4](docs/PERSONAL_INTELLIGENCE.md)
- En **Configuración → Apariencia**, compara las dos vistas animadas y selecciona **Bali minimalista** o **Bali clásico**. El cambio se ve al momento en el escritorio y se conserva al reiniciar. Los perfiles creados antes de esta actualización comienzan con el estilo minimalista.
- En **Conversar**, escribe un comando o pulsa uno de los botones de ejemplo. En **Voz** puedes habilitar la escucha y después pulsar **Escuchar un comando**. La compatibilidad depende de los componentes locales de Windows.

Los ajustes y la memoria están en `%LOCALAPPDATA%\BaliDesktop\estado.json`. Puedes borrar la memoria de interacciones desde **Vida artificial**. El inicio automático está desactivado inicialmente.

## Compilar y probar

Desde PowerShell, en la carpeta del proyecto:

```powershell
.\Build.ps1 -Test
```

El script usa el compilador de Windows y ensambla `dist\BaliDesktop.exe`. Las comprobaciones automatizadas escriben sus resultados en `evidence\unit.txt`.

El repositorio incluye el código, la documentación y las dependencias portátiles de compilación. Los binarios generados, las copias de recuperación locales y la evidencia de cada equipo se excluyen de Git. Los enlaces de los informes a `evidence` se refieren a resultados generados localmente.

[Arquitectura](docs/ARQUITECTURA.md) · [Motor de vida](docs/MOTOR_VIDA_ARTIFICIAL.md) · [Identidad visual](docs/IDENTIDAD_VISUAL.md) · [Estado actual](docs/ESTADO_ACTUAL.md) · [Pruebas](docs/RESULTADOS_PRUEBAS.md) · [Evolución](docs/ROADMAP.md)

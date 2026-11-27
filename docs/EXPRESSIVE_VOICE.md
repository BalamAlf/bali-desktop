# Expressive Voice 1.3.1

## Demostración

Abre **Habla** desde la bandeja con **Demostración de voz** o desde **Diagnóstico → Sincronización labial**. Elige **Frase corta** o **Frase con pausas**. Las vistas clásica y minimalista muestran la misma señal; la mascota del escritorio y el rostro de Conversar también la usan. La pestaña muestra el visema actual y el siguiente, posición y duración de audio, recuentos de fonemas y palabras y apertura actual. Pausar y detener actúan sobre la reproducción de Windows.

Los siete botones de forma son una previsualización silenciosa que solo afecta a las dos tarjetas. **Seguir audio** vuelve a la señal real. Los controles de suavidad e intensidad ajustan la animación y se guardan como preferencias de presentación.

## Sincronización implementada

**WindowsAudio.cs** convierte `SpeakStarted`, `VisemeReached`, `PhonemeReached`, `SpeakProgress` y `SpeakCompleted` en señales para el reloj visual. Cada evento útil se marca con un reloj monotónico antes de publicarlo al hilo de WPF. Los callbacks de un sintetizador detenido o sustituido se descartan. Inicio, pausa, reanudación, cancelación y fallo se tratan por separado. El rostro se cierra inmediatamente al detener o completar la voz.

**LipSync.cs** trabaja con muestras temporales y figuras normalizadas. La primera muestra relaciona `AudioPosition` con el reloj monotónico. Las siguientes corrigen la diferencia de forma limitada. La duración marca el intervalo de forma. Se interpola la entrada y la salida, y se mezcla ligeramente el próximo visema cuando ambos tienen apertura. Los visemas de silencio y cierre bilabial quedan cerrados. Un gesto sin una muestra nueva retorna a cerrado al vencer su duración. Las pausas explícitas congelan el reloj activo y cierran la boca. [Eventos de voz .NET](https://learn.microsoft.com/en-us/dotnet/api/system.speech.synthesis.speechsynthesizer.speakasync?view=netframework-4.8.1) · [Mapa de visemas SAPI](https://learn.microsoft.com/en-us/previous-versions/office/developer/speech-technologies/jj127484%28v%3Dmsdn.10%29)

En la prueba de Helena, `SpeakStarted` llegó a 6 ms y la primera señal de audio aproximadamente a 582 ms. Helena comunicó un visema de silencio de 750 ms en la frase de prueba. Por eso, el inicio del sintetizador prepara la capa visual y el primer evento de audio fija la referencia. El progreso por palabra se muestra solo como diagnóstico. Si una voz ofrece fonemas legibles pero carece de visemas, el motor puede usar su posición y duración como respaldo. Si no hay señales fonéticas utilizables, la boca se mantiene cerrada. Helena produjo visemas adecuados durante las pruebas; el respaldo fonético se verificó con muestras sintéticas y no se añadió captura de amplitud del dispositivo. [Eventos reales de Helena](../evidence/helena-eventos-1.3.1.txt)

El render conserva los contornos, colores, parpadeos y dibujos originales. La capa de habla adapta ancho, abertura y sonrisa al grosor y escala de cada estilo. La mirada al cursor se atenúa durante el habla. Durante la escucha y el procesamiento de DeepSeek se dibuja el cierre. Al terminar, regresan las expresiones autónomas previas. La demo y los visemas no crean eventos de aprendizaje o memoria cognitiva. La integración HTTP con DeepSeek permanece intacta.

El temporizador de presentación existente usa un intervalo de 25 ms cuando hay audio activo y una vista visible. El ritmo de reposo se conserva. Las pruebas de sincronización observan eventos del dispositivo y el estado visual de WPF. No miden latencia acústica con un micrófono externo ni certifican otras voces.

## Validación y recuperación

Pasaron 112 comprobaciones, incluidas las 98 de antes de Expressive Voice y las 96 originales. Las nuevas incluyen cierre previo al audio, interpolación, silencio, pausa, reanudación, finalización, respaldo fonético y las siete figuras verificadas con los dos renderizadores WPF. [Pruebas](../evidence/unit.txt)

Se validó en un perfil aislado la reproducción real de Microsoft Helena. Se incluyeron la frase corta, la frase larga con pausas, la pausa manual, la reanudación, una cancelación con la boca abierta y una reproducción posterior. El caso de procesamiento simulado verificó cierre y ausencia de solicitudes de red. [Ejecución real](../evidence/expressive-voice-final/runtime.txt) · [Boca abierta](../evidence/expressive-voice-final/voz-abierta.png) · [Cierre al cancelar](../evidence/expressive-voice-final/voz-cancelada.png)

Con el escritorio y ambas vistas de Habla activos, la frase larga produjo 107 visemas y 25 eventos de palabra en 10,93 s. El proceso usó 1,17 % de CPU media, 174,2 MiB de memoria residente y 151,9 MiB de memoria privada. La frase corta usó 1,29 % y 169,4 MiB residentes en 2,83 s. Estas muestras incluyen la preparación de Windows y son instantáneas de memoria, no una prueba sostenida.

La copia recuperable incluye la corrección TLS de 1.3. [Recuperación de 1.3](../recovery/1.3/README.md)

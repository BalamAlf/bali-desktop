using System.IO;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void CheckSpokenQuestions(TextWriter log)
        {
            Check(SpokenQuestion.IsInformational("¿Qué es una VPN?") && !SpokenQuestion.IsInformational("abre la calculadora") &&
                !SpokenQuestion.IsInformational("sí"),"Distingue preguntas informativas de acciones y confirmaciones",log);
            Check(!SpokenQuestion.ClearWhisper("¿Qué es una VPN?") &&
                SpokenQuestion.ClearWhisper("¿Cómo protege una VPN mis datos?") &&
                !SpokenQuestion.ClearWhisper("¿Cómo protege protege una VPN mis datos?"),"Evalúa estructura de Whisper sin inventar confianza",log);
            var pending=new SpokenQuestion();
            pending.Ask("¿Qué es una VPN?",10);
            Check(pending.AwaitingAnswer(34) && !pending.Active(35) && SpokenQuestion.Answer("¡Sí!") == SpokenReply.Yes,
                "Confirmación verbal caduca y reconoce respuestas cortas",log);
            pending.Repeat(40);
            Check(!pending.AwaitingAnswer(41) && pending.Active(41) && pending.Text==null && SpokenQuestion.Answer("no")==SpokenReply.No &&
                SpokenQuestion.IsInformational(SpokenQuestion.Correction("No, ¿qué es DNS?")),"Una negativa descarta la pregunta y permite corregirla",log);
            pending.Clear();
            Check(!pending.HasSession && !pending.Active(41) && SpokenQuestion.Answer("cancela")==SpokenReply.Cancel &&
                SpokenQuestion.Answer("sí pero no lo envíes")==SpokenReply.Other,"Cancelación y respuestas ambiguas no confirman",log);
            Check(SpokenQuestion.ForSpeech("## Título\n**Una VPN** [guía](https://example.test/a) https://example.test/a")=="Título Una VPN guía",
                "La voz omite enlaces y marcas Markdown",log);
            var settings=new IntelligenceSettings();
            Check(settings.DirectVoiceOnline && settings.ConfirmVoiceOnline && !settings.AutoOpenVoiceChat,"Consulta hablada activa con el chat cerrado",log);
        }
    }
}

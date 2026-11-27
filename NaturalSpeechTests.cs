using System;
using System.IO;
using System.Linq;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void CheckNaturalSpeech(string folder,TextWriter log)
        {
            string path=Path.Combine(folder,"natural-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var data=new PersonalStore(path);
            var router=new BaliToolRouter(data,x=>false);
            var now=new DateTime(2026,11,27,9,0,0);
            string[] forms={"Recuérdame tomar agua en cinco minutos","Avísame dentro de cinco minutos que tome agua",
                "En cinco minutos dime que tome agua","Ponme un recordatorio para tomar agua en cinco minutos"};
            foreach(string form in forms)
            {
                router.Interpret(form,"voz",now);
                Check(data.Data.Items.Last().Date==now.AddMinutes(5),"Comprende "+form,log);
                now=now.AddMinutes(10);
            }
            int count=data.Data.Items.Count;
            router.Interpret("Bali, recuérdame revisar mis proyectos","voz",now);
            Check(router.AwaitsTime("voz",now) && data.Data.Items.Count==count,"Pide horario sin guardar",log);
            router.Interpret("Mañana a las diez de la mañana","texto",now);
            Check(data.Data.Items.Count==count,"Aclaración solo en el canal original",log);
            router.Interpret("Mañana, a las 10 de la mañana","voz",now);
            router.Interpret("Mañana a las diez de la mañana","voz",now);
            Check(data.Data.Items.Count==count+1 && data.Data.Items.Last().Date==now.Date.AddDays(1).AddHours(10),"Guarda una sola vez al completar horario",log);
            router.Interpret("Mejor mañana a las once de la mañana","voz",now);
            Check(data.Data.Items.Count==count+1 && data.Data.Items.Last().Date==now.Date.AddDays(1).AddHours(11),"Corrige el último horario sin duplicarlo",log);
            router.ReviewVoice=true;
            router.Interpret("Recuérdame llamar al taller","voz",now);
            router.ReviewVoice=false;
            router.Interpret("En cinco minutos","voz",now);
            Check(router.AwaitsApproval("voz",now) && data.Data.Items.Count==count+1,"Whisper conserva confirmación después de aclarar",log);
            router.Interpret("confirma","texto",now);
            Check(data.Data.Items.Count==count+1,"Otro canal no autoriza la acción",log);
            router.Interpret("confirma","voz",now);
            Check(data.Data.Items.Count==count+2 && !router.AwaitsApproval("voz",now),"Confirmación explícita ejecuta una vez",log);
            router.Interpret("Recuérdame revisar el papel","voz",now);
            router.Interpret("Cancela","voz",now);
            Check(!router.AwaitsTime("voz",now) && data.Data.Items.Count==count+2,"Cancelación del borrador de voz",log);
            byte[] pcm=new byte[96000];
            Check(SpeechAudio.Prepare(pcm,1.5,false).Length==64000,"Preparación conserva 500 ms previos a la voz",log);
            var weak=new byte[6400];
            for(int i=0;i<weak.Length;i+=2)
            {
                short value=(short)(Math.Sin(i*.19)*360);
                weak[i]=unchecked((byte)value); weak[i+1]=unchecked((byte)(value>>8));
            }
            var original=new VoiceSilenceDetector();original.Observe(weak,weak.Length);
            var boosted=new VoiceSilenceDetector();
            boosted.Observe(SpeechAudio.Prepare(weak,null,true,original.VoiceRms),weak.Length);
            Check(boosted.Rms>original.Rms*3 && boosted.Clipped==0,"Refuerza voz débil sin saturar ni recortar la captura manual",log);
            Check(WhisperRecognizer.Clean("[Silencio]")==null && WhisperRecognizer.Clean("Gracias por ver")==null,"Descarta ruido y texto de subtítulos común",log);
            Check(WhisperRecognizer.Clean("¡Hola!")=="¡Hola!" &&
                new LocalConversationProvider().Respond("¡Hola!",new Settings().Memory).Intent==Intent.Hello &&
                WhisperRecognizer.Clean("confirma")==null,"Admite un saludo breve sin habilitar confirmaciones de Whisper",log);
            router.Interpret("Crea un pendiente para hoy preparar la agenda","texto",now);
            string agenda=router.Interpret("Hola y ¿qué tengo hoy?","voz",now);
            Check(agenda.StartsWith("¡Hola!") && agenda.Contains("preparar la agenda") && agenda.Contains("llamar al taller"),
                "Responde a qué tengo hoy con pendientes y recordatorios",log);
            Check(router.Interpret("¿Qué tengo hoy?","voz",now.AddDays(2))=="No tienes pendientes ni recordatorios programados para hoy.",
                "Explica en lenguaje natural cuando la agenda del día está vacía",log);
            var partial=new SpeechResult {Text="abre calculadora",Confidence=.82,Rejected=1};
            Check(partial.HasText && partial.NeedsReview,"Conserva las palabras reconocidas pese a rechazos y exige revisión",log);
            partial.Text="confirma";
            Check(!partial.CanConfirm && !new SpeechResult{Text="confirma"}.CanConfirm &&
                new SpeechResult{Text="Confirma",Confidence=.9}.CanConfirm &&
                !new SpeechResult{Text="no confirma",Confidence=.9}.CanConfirm,"Solo una confirmación clara y con confianza autoriza",log);
            Check(!new SpeechResult{Rejected=1}.HasText,"Distingue rechazo total de reconocimiento parcial",log);
            var settings=new Settings();
            var profile=new Store(path);
            Check(settings.Intelligence.SttProvider=="windows","Windows Speech sigue como opción inicial",log);
            settings.Intelligence.SttProvider="whisper";settings.Intelligence.SttSensitivity=1.2;settings.Intelligence.SttEnhance=true;
            profile.Save(settings);
            var restored=profile.Load().Intelligence;
            Check(restored.SttProvider=="whisper" && restored.SttEnhance && restored.SttSensitivity==1.2,"Persiste la configuración local del reconocimiento",log);
            settings.Intelligence.SttProvider="whisper-base";profile.Save(settings);
            Check(profile.Load().Intelligence.SttProvider=="whisper-base","La selección opcional de Whisper Base persiste",log);
        }
    }
}

using System;
using System.IO;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void CheckVoiceAndProactive(TextWriter log)
        {
            var current=new VoiceSession();
            current.Remember("¿Qué es una VPN?",10,false);
            Check(current.Resolve("¿Y para qué sirve?",11,120).Contains("una VPN") &&
                current.Resolve("¿Qué es DNS?",11,120)=="¿Qué es DNS?","Reutiliza el último tema solo en seguimientos reconocidos",log);
            current.Remember("¿Y para qué sirve?",14,true);
            Check(current.Topic(15,120)=="¿Qué es una VPN?" && current.Topic(135,120)==null,"Un seguimiento mantiene el tema y el contexto caduca",log);
            current.Clear();
            Check(current.Resolve("Dame un ejemplo",20,120)=="Dame un ejemplo" && current.Topic(20,120)==null,"Cancelar descarta el contexto temporal",log);
            var words=new[]{
                new TechnicalWord{Spoken="guor",Written="Word",Context="Aplicaciones"},
                new TechnicalWord{Spoken="guor",Written="editor de texto",Context="Preguntas técnicas"},
                new TechnicalWord{Spoken="sitio alfa",Written="Proyecto Alfa",Context="Agenda"}};
            Check(TechnicalSpeech.Normalize("abre guor",words)=="abre Word" &&
                TechnicalSpeech.Normalize("qué es guor",words)=="qué es editor de texto" &&
                TechnicalSpeech.Normalize("recuérdame revisar sitio alfa",words)=="recuérdame revisar Proyecto Alfa" &&
                TechnicalSpeech.Normalize("me llamo guor",words)=="me llamo guor","Correcciones aprobadas se aplican solo en el contexto elegido",log);
            var chained=new[]{new TechnicalWord{Spoken="uno",Written="dos"},new TechnicalWord{Spoken="dos",Written="tres"}};
            Check(TechnicalSpeech.Normalize("abre uno",chained)=="abre dos","Una corrección aprendida no activa otra en cadena",log);
            var config=new ProactiveOptions();
            Check(!config.Enable && !config.Foreground && !config.Resume,"Privacidad proactiva inicialmente desactivada",log);
            Check(ProactivePolicy.CanNotify(Mode.Libre,false,true,10,.5) &&
                !ProactivePolicy.CanNotify(Mode.Concentrado,false,true,10,.5) &&
                !ProactivePolicy.CanNotify(Mode.Descanso,false,true,10,.5) &&
                !ProactivePolicy.CanNotify(Mode.Interactivo,true,true,10,.5),"Respeta modo, autonomía y conversaciones activas",log);
            var now=new DateTime(2026,11,27);
            var tips=new[]{new ProactiveCandidate{Id="rutina",Weight=.60},new ProactiveCandidate{Id="resumen",Weight=.50}};
            config.Shown["rutina"]="2026-11-27";
            Check(ProactivePolicy.Select(tips,config,now,false).Id=="resumen","Evita repetir la misma iniciativa en el día",log);
            config.Shown.Clear();config.Preference["rutina"]=-3;
            Check(ProactivePolicy.Select(tips,config,now,true).Id=="resumen" &&
                ProactivePolicy.Select(tips,config,now,false).Id=="rutina" &&
                ProactivePolicy.Feedback(4,true)==4 && ProactivePolicy.Feedback(-4,false)==-4,
                "La preferencia aprendida altera la selección solo con aprendizaje activo",log);
        }
    }
}

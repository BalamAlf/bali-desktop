using System;
using System.Text.RegularExpressions;

namespace BaliDesktop
{
    sealed class VoiceSession
    {
        string topic;
        double completed;
        public string Topic(double now,double lifetime)
        {return topic!=null && now-completed<lifetime?topic:null;}
        public void Remember(string question,double now,bool followUp)
        {
            if(!followUp)topic=question;
            completed=now;
        }
        public void Clear(){topic=null;completed=0;}
        public static bool IsFollowUp(string text)
        {
            return Regex.IsMatch(SpokenQuestion.Normalize(text),
                @"^(?:y\s+)?(?:para qu[eé] sirve|c[oó]mo funciona|qu[eé] ventajas tiene|dame (?:un |otro )?ejemplo|qu[eé] riesgos tiene|c[oó]mo se usa|dime m[aá]s)$");
        }
        public string Resolve(string question,double now,double lifetime)
        {
            string previous=Topic(now,lifetime);
            return previous==null || !IsFollowUp(question)?question:
                "Tema anterior que pregunté: "+previous+"\nMi pregunta de seguimiento: "+question;
        }
    }
}

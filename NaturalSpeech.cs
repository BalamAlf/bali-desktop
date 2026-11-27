using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace BaliDesktop
{
    sealed partial class BaliToolRouter
    {
        string missingTimeText, missingTimeOrigin, lastVoiceOrigin;
        DateTime missingUntil, lastVoiceUntil;
        Guid lastReminder;
        bool missingTimeReview;
        public bool AwaitsTime(string origin,DateTime now)
        { return missingTimeText!=null && missingTimeOrigin==origin && now<=missingUntil; }
        public void CancelNatural(){missingTimeText=null;}
        string AskWhen(string text,string origin,DateTime now)
        {
            missingTimeText=text;missingTimeOrigin=origin;missingUntil=now.AddSeconds(55);missingTimeReview=ReviewVoice;
            return "Entendí lo que quieres recordar. ¿Cuándo quieres que te lo recuerde?";
        }
        string CompleteNatural(string text,string origin,DateTime now)
        {
            if(origin!="voz" && origin!="texto" && origin!="panel") return null;
            if(AwaitsTime(origin,now))
            {
                if(Match(text,@"^(?:cancela|olvídalo|ya no|no gracias)$").Success)
                {CancelNatural();return "Recordatorio pendiente cancelado.";}
                DateTime when;
                if(TrySpokenTime(text,now,out when))
                {
                    string message=missingTimeText;CancelNatural();
                    bool previous=ReviewVoice;
                    try { ReviewVoice|=missingTimeReview; return SaveNatural(message,when,origin,now); }
                    finally { ReviewVoice=previous; }
                }
                if(Match(NormalizeNatural(text),@"^(?:recu[eé]rdame|abre|crea|guarda|cancela mi)\b").Success){CancelNatural();return null;}
                return "Aún necesito el horario. Puedes decir mañana a las diez, en cinco minutos, o cancela.";
            }
            var correction=Match(text,@"^(?:mejor|cambia(?: el horario)? a)\s+(.+)$");
            if(correction.Success && lastReminder!=Guid.Empty && lastVoiceOrigin==origin && now<=lastVoiceUntil)
            {
                DateTime at;
                var previous=store.Data.Items.FirstOrDefault(x=>x.Id==lastReminder && x.State=="Activo" && x.Kind=="Recordatorio");
                if(previous!=null && TrySpokenTime(correction.Groups[1].Value,now,out at))
                    return Execute(new ToolRequest{Id="editar",Item=previous.Id,Text=previous.Text,Repeat=previous.Repeat,Date=at,
                        Project=previous.Project,Tags=previous.Tags,Priority=previous.Priority,Related=previous.Related},origin,now).Message;
                return "Indica un horario futuro para el último recordatorio.";
            }
            return null;
        }
        string SaveNatural(string text,DateTime when,string origin,DateTime now)
        {
            var answer=Execute(new ToolRequest{Id="recordar",Text=text,Date=when},origin,now);
            if(answer.Ok) RememberLatest(origin,now);
            return answer.Message;
        }
        void RememberLatest(string origin,DateTime now)
        {
            var accepted=store.Data.Items.LastOrDefault(x=>x.Kind=="Recordatorio");
            if(accepted==null)return;
            lastReminder=accepted.Id;lastVoiceOrigin=origin;lastVoiceUntil=now.AddSeconds(60);
        }
        static bool TrySpokenTime(string text,DateTime now,out DateTime when)
        {
            when=default(DateTime);
            text=Regex.Replace(text,@",\s*"," ").Trim();
            var relative=Match(text,@"^(?:en|dentro de)\s+(\w+(?: y \w+)?)\s+(minutos?|horas?|d[ií]as?)$");
            if(relative.Success)
            {
                int number=SpokenNumber(relative.Groups[1].Value);
                if(number<1 || number>1440)return false;
                char unit=Char.ToLowerInvariant(relative.Groups[2].Value[0]);
                when=now.AddMinutes(number*(unit=='h'?60:unit=='d'?1440:1));return true;
            }
            var clock=Match(text,@"^(?:(hoy|mañana|pasado mañana)\s+)?a las?\s+(\w+(?: y (?!media\b|cuarto\b)\w+)?)(?::(\d{2})| y (media|cuarto))?(?: de la (mañana|tarde|noche))?$");
            if(!clock.Success)return false;
            int hour=SpokenNumber(clock.Groups[2].Value), minute=0;
            if(clock.Groups[3].Success)Int32.TryParse(clock.Groups[3].Value,out minute);
            else if(clock.Groups[4].Success) minute=clock.Groups[4].Value.Equals("media",StringComparison.OrdinalIgnoreCase)?30:15;
            if(hour<0 || hour>23 || minute>59)return false;
            var part=clock.Groups[5].Value.ToLowerInvariant();
            if(part.Length!=0 && (hour<1 || hour>12))return false;
            if(part=="mañana" && hour==12)hour=0;
            if((part=="tarde" || part=="noche") && hour<12)hour+=12;
            string day=clock.Groups[1].Value.ToLowerInvariant();
            when=now.Date.AddDays(day=="pasado mañana"?2:day=="mañana"?1:0).AddHours(hour).AddMinutes(minute);
            return when>now;
        }
        static int SpokenNumber(string text)
        {
            var pieces=text.ToLowerInvariant().Split(new[]{" y "},StringSplitOptions.None);
            if(pieces.Length==1)
            {
                switch(pieces[0]) {case "treinta":return 30;case "cuarenta":return 40;case "cincuenta":return 50;case "sesenta":return 60;}
                return Number(text);
            }
            if(pieces.Length!=2)return -1;
            int tens=SpokenNumber(pieces[0]), units=Number(pieces[1]);
            return tens>=30 && tens<=60 && units>=1 && units<=9?tens+units:-1;
        }
        static string NormalizeNatural(string raw)
        {
            string text=Regex.Replace(raw,@"^(?:oye\s+|por favor[,\s]+|hazme el favor de\s+)","",RegexOptions.IgnoreCase).Trim();
            text=Regex.Replace(text,@"^bali[,\s]+|[,\s]+por favor$","",RegexOptions.IgnoreCase).Trim();
            text=Regex.Replace(text,@"^habre(?=\s+(?:la\s+|el\s+)?(?:calculadora|explorador)$)","abre",RegexOptions.IgnoreCase);
            text=Regex.Replace(text,@"^(?:habisame|hablesame)(?=\s+(?:dentro de|en)\s+)","avísame",RegexOptions.IgnoreCase);
            Match m=Match(text,@"^(?:av[ií]same|dime|recu[eé]rdame)\s+(?:en|dentro de)\s+(\w+(?: y \w+)?)\s+(minutos?|horas?|d[ií]as?)\s+(?:que\s+)?(.+)$");
            if(!m.Success)m=Match(text,@"^(?:en|dentro de)\s+(\w+(?: y \w+)?)\s+(minutos?|horas?|d[ií]as?)\s+(?:av[ií]same|dime|recu[eé]rdame)\s+(?:que\s+)?(.+)$");
            if(m.Success)return "recuérdame en "+SpokenNumber(m.Groups[1].Value)+" "+m.Groups[2].Value+" "+m.Groups[3].Value;
            m=Match(text,@"^(?:recu[eé]rdame|(?:ponme|pon|programa)\s+(?:un\s+)?recordatorio\s+(?:para|de))\s+(?:(?:que|para)\s+)?(.+?)\s+(?:en|dentro de)\s+(\w+(?: y \w+)?)\s+(minutos?|horas?|d[ií]as?)$");
            if(m.Success)return "recuérdame en "+SpokenNumber(m.Groups[2].Value)+" "+m.Groups[3].Value+" "+m.Groups[1].Value;
            text=Regex.Replace(text,@"^(?:me abres|puedes abrir|ábreme|inicia|arranca)\s+(?:(?:la|el)\s+)?","abre ",RegexOptions.IgnoreCase);
            text=Regex.Replace(text,@"^abre\s+(?:la|el)\s+","abre ",RegexOptions.IgnoreCase);
            text=Regex.Replace(text,@"^(?:anota|agrega|apunta|pon)\s+(?:(?:en mis|a mis)\s+)?pendientes\s*:?\s+","crea un pendiente ",RegexOptions.IgnoreCase);
            text=Regex.Replace(text,@"^(?:apunta|anota|registra)\s+(una?\s+)?(nota|idea|decisi[oó]n|preferencia)\s+","guarda $1$2 ",RegexOptions.IgnoreCase);
            text=Regex.Replace(text,@"^(?:(?:qu[eé]|cu[aá]les)\s+(?:me toca|me falta|son mis)|qu[eé] tengo por hacer)\s*(hoy)?\s*$","qué pendientes tengo $1",RegexOptions.IgnoreCase);
            text=Regex.Replace(text,@"^(?:ch[eé]came|checa|ens[eé]ñame|dime|revisa)\s+(?=(?:mis\s+)?(?:pendientes|recordatorios))","consulta ",RegexOptions.IgnoreCase);
            return text.Trim();
        }
        static bool TryNaturalReminder(string text,DateTime now,out ToolRequest request)
        {
            request=null;
            const string schedule=@"((?:hoy|mañana|pasado mañana)?\s*,?\s*a las?\s+\w+(?::\d{2}| y (?:media|cuarto))? de la (?:mañana|tarde|noche))";
            var match=Match(text,@"^recu[eé]rdame\s+(.+?)\s+"+schedule+"$");
            if(!match.Success)return false;
            DateTime at;
            if(!TrySpokenTime(match.Groups[2].Value,now,out at))return false;
            request=new ToolRequest{Id="recordar",Text=match.Groups[1].Value,Date=at};
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BaliDesktop
{
    sealed partial class BaliToolRouter
    {
        public string Interpret(string phrase,string origin,DateTime now)
        {
            string text=Regex.Replace((phrase??"").Trim().TrimStart('¿','¡'),@"^bali[\s,]+","",RegexOptions.IgnoreCase).Trim(' ','¿','?','.','¡','!');
            LastIntent=LastParameters=null;
            string follow=CompleteNatural(text,origin,now);
            if(follow!=null)return follow;
            text=NormalizeNatural(text);
            var today=Match(text,@"^(?:(hola)(?:[\s,]+y)?[\s,¡!¿]+)?qu[eé] tengo hoy$");
            if(today.Success)
            {
                var agenda=Execute(new ToolRequest{Id="agenda",Kind="Hoy",Date=now.Date},origin,now);
                return (today.Groups[1].Success?"¡Hola! ":"")+agenda.Message;
            }
            if(text.Equals("confirma",StringComparison.OrdinalIgnoreCase))
            {
                if(pending==null || origin!=pendingOrigin || now>expires) return "No hay confirmación vigente para este canal.";
                var approved=pending; pending=null;
                var result=Execute(approved,origin,now,true);
                if(result.Ok && approved.Id=="recordar")RememberLatest(origin,now);
                return result.Message;
            }
            if(text.Equals("cancela",StringComparison.OrdinalIgnoreCase) && pending!=null)
            { if(pendingOrigin!=origin)return "No hay confirmación vigente para este canal."; Cancel(origin);return "Acción cancelada."; }
            var actions=Regex.Split(text,@"\s+y\s+(?=abre\s|crea\s|guarda\s|recu[eé]rdame\s|consulta\s)|;",RegexOptions.IgnoreCase);
            if(actions.Length>4) return "Indica hasta cuatro acciones.";
            var results=new List<string>();
            foreach(string action in actions)
            {
                var value=One(action.Trim(),origin,now,actions.Length>1);
                if(value==null && actions.Length==1) return null;
                results.Add(value??"No pude interpretar una de las partes.");
            }
            return String.Join(" ",results);
        }
        string One(string text,string origin,DateTime now,bool batch)
        {
            var r=new ToolRequest();
            Match m;
            if((m=Match(text,@"^abre\s+(.+)$")).Success) {r.Id="abrir";r.Text=m.Groups[1].Value;}
            else if((m=Match(text,@"^(?:crea|agrega|anota)\s+(?:un\s+)?pendiente(?: para (hoy|mañana))?\s*:?\s+(.+)$")).Success)
            {
                r.Id="pendiente";r.Text=m.Groups[2].Value;
                if(m.Groups[1].Success) r.Date=now.Date.AddDays(m.Groups[1].Value.Equals("hoy",StringComparison.OrdinalIgnoreCase)?0:1).AddHours(18);
            }
            else if((m=Match(text,@"^guarda\s+(?:una?\s+)?(nota|idea|decisi[oó]n|preferencia|recuerdo|proyecto)\s*:?\s+(.+)$")).Success)
            {
                r.Id="nota";r.Text=m.Groups[2].Value;
                r.Kind=Kinds.FirstOrDefault(k=>Match(k,"^"+m.Groups[1].Value+"$").Success)??"Decisión";
            }
            else if((m=Match(text,@"^(?:busca en mi conocimiento|qu[eé] decidimos sobre)\s+(.+)$")).Success)
            {r.Id="conocimiento";r.Text=m.Groups[1].Value;}
            else if((m=Match(text,@"^(?:busca en el documento|consulta el documento|resume el documento)(?:\s+(.+))?$")).Success)
            {r.Id="documentos";r.Text=m.Groups[1].Value;}
            else if(Match(text,@"^(?:mu[eé]strame|consulta|qu[eé]|mis)\b.*(?:pendientes|recordatorios)").Success)
            {
                r.Id="agenda";r.Kind=Match(text,"recordatorios").Success?"Recordatorio":"Pendiente";
                if(Match(text,@"\bhoy\b").Success) r.Date=now.Date;
                m=Match(text,@"\bproyecto\s+(.+)$"); if(m.Success) r.Text=m.Groups[1].Value;
            }
            else if((m=Match(text,@"^(cancela|posp[oó]n|completa|archiva)\s+(?:(?:mi|el|la|un)\s+)?(?:(último)\s+)?(recordatorio|pendiente|nota)(?:\s+(.+))?$")).Success)
            {
                var found=store.Data.Items.Where(x=>x.State=="Activo" && Same(x.Kind,m.Groups[3].Value) &&
                    (!m.Groups[4].Success || Matches(x,m.Groups[4].Value))).OrderByDescending(x=>x.Created).ToArray();
                if(found.Length==0) return "No encontré ese elemento activo.";
                if(found.Length>1 && !m.Groups[2].Success) return "Hay varias coincidencias. Indica el nombre exacto.";
                string verb=m.Groups[1].Value.ToLowerInvariant();
                if(verb=="cancela" && batch) return "Solicita esa cancelación por separado.";
                r.Id=verb=="cancela"?"borrar":"editar";r.Item=found[0].Id;
                r.Operation=verb=="completa"?"completar":verb=="archiva"?"archivar":"posponer";
                r.Date=now.AddMinutes(10);
            }
            else if(Match(text,@"^(?:recu[eé]rdame|mañana a|todos los|diariamente)\b").Success)
            {
                if((!TryNaturalReminder(text,now,out r) && !Reminder(text,now,out r)) || r.Date<=now)
                {
                    var incomplete=Match(text,@"^recu[eé]rdame\s+(.+)$");
                    if(!batch && incomplete.Success && !Match(incomplete.Groups[1].Value,@"^(?:en|el|mañana|hoy|dentro|a las)\b").Success)
                        return AskWhen(incomplete.Groups[1].Value,origin,now);
                    return "Indica cuándo y qué recordar. Por ejemplo, recuérdame en cinco minutos beber agua.";
                }
            }
            else if(Match(text,@"^(?:guarda|crea)\s+(?:una?\s+)?(?:nota|idea|pendiente|proyecto)\s*$").Success)
                return "Indica el texto que quieres guardar.";
            else return null;
            var answer=Execute(r,origin,now);
            if(r.Id=="recordar" && answer.Ok)RememberLatest(origin,now);
            return answer.Message;
        }
        static Match Match(string text,string pattern){return Regex.Match(text,pattern,RegexOptions.IgnoreCase);}
        static int Number(string value)
        {
            int number;
            if(Int32.TryParse(value,out number)) return number;
            value=value.ToLowerInvariant();
            if(value=="un" || value=="una") return 1;
            if(value.StartsWith("veinti"))
            {
                string normalized=value.Substring(6).Replace("ú","u").Replace("é","e").Replace("ó","o").Replace("í","i");
                int remainder=Number(normalized);
                if(remainder>=1 && remainder<=9)return 20+remainder;
            }
            return Array.IndexOf(new[]{"cero","uno","dos","tres","cuatro","cinco","seis","siete","ocho","nueve","diez","once","doce","trece","catorce","quince","dieciséis","diecisiete","dieciocho","diecinueve","veinte"},value);
        }
        public static bool Reminder(string text,DateTime now,out ToolRequest r)
        {
            r=new ToolRequest {Id="recordar"};
            var m=Match(text,@"^recu[eé]rdame en (\w+) (minutos?|horas?|d[ií]as?)\s+(.+)$");
            if(m.Success)
            {
                int amount=Number(m.Groups[1].Value);
                if(amount<1 || amount>10080) return false;
                char unit=Char.ToLowerInvariant(m.Groups[2].Value[0]);
                r.Date=now.AddMinutes(amount*(unit=='h'?60:unit=='d'?1440:1));r.Text=m.Groups[3].Value;return true;
            }
            DateTime absolute;
            m=Match(text,@"^recu[eé]rdame el (\d{4}-\d{2}-\d{2}) a las (\d{2}:\d{2})\s+(.+)$");
            if(m.Success && DateTime.TryParseExact(m.Groups[1].Value+" "+m.Groups[2].Value,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out absolute))
            {r.Date=absolute;r.Text=m.Groups[3].Value;return true;}
            m=Match(text,@"^(?:mañana a las? (\w+)(?::(\d{2}))?\s+recu[eé]rdame\s+(.+)|recu[eé]rdame mañana a las? (\w+)(?::(\d{2}))?\s+(.+))$");
            if(m.Success)
            {
                int i=m.Groups[1].Success?1:4;
                var time=ClockTime(m.Groups[i].Value,m.Groups[i+1].Value,9);
                if(!time.HasValue) return false;
                r.Date=now.Date.AddDays(1).Add(time.Value);r.Text=m.Groups[i+2].Value;return true;
            }
            m=Match(text,@"^(?:todos los (días|lunes|martes|miércoles|jueves|viernes|sábados?|domingos?)|diariamente)(?: a las? (\w+)(?::(\d{2}))?)?\s+recu[eé]rdame\s+(.+)$");
            if(!m.Success) return false;
            string day=m.Groups[1].Value.ToLowerInvariant();
            if(day=="domingos" || day=="sábados") day=day.Substring(0,day.Length-1);
            int index=Array.IndexOf(new[]{"domingo","lunes","martes","miércoles","jueves","viernes","sábado"},day);
            var at=ClockTime(m.Groups[2].Value,m.Groups[3].Value,9);
            if(!at.HasValue) return false;
            var next=now.Date.Add(at.Value).AddDays(index<0?0:(index-(int)now.DayOfWeek+7)%7);
            r.Date=next<=now?next.AddDays(index<0?1:7):next;r.Repeat=index<0?"Diario":"Semanal";r.Text=m.Groups[4].Value;
            return true;
        }
        static TimeSpan? ClockTime(string hour,string minute,int fallback)
        {
            int h=hour.Length==0?fallback:Number(hour), m=minute.Length==0?0:Number(minute);
            return h>=0 && h<24 && m>=0 && m<60?(TimeSpan?)new TimeSpan(h,m,0):null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace BaliDesktop
{
    [DataContract] sealed class TechnicalWord
    {
        [DataMember] public string Spoken, Written;
        [DataMember] public string Context;
        public override string ToString(){return Spoken+" → "+Written+(String.IsNullOrEmpty(Context)?"":" · "+Context);}
    }
    static class TechnicalSpeech
    {
        static readonly string[] Forms={
            "uve pe ene|ve pe ene|v p n:VPN","a pe i|a p i:API","ese cu ele|ese q ele|ese cu el:SQL",
            "erre a eme:RAM","ce pe u|c p u:CPU","ge pe u|g p u:GPU","ese ese de|s s d:SSD","u ese be|u s b:USB",
            "de ene ese|d n s:DNS","hache te te pe ese|h t t p s:HTTPS","hache te te pe|h t t p:HTTP",
            "equis eme ele|x m l:XML","yeison|yéi son|j son:JSON","hache te eme ele|h t m l:HTML","ce ese ese|c s s:CSS",
            "punto net:.NET","ce sharp|ce numeral|c sharp:C#","paiton:Python","gui jab|git jab:GitHub",
            "guit:Git","güindous:Windows","línux:Linux","visual estudio code|visual estudio código|vs code:Visual Studio Code",
            "visual estudio:Visual Studio","obsídian:Obsidian","dip sic|deep seek:DeepSeek","códex:Codex","open code:OpenCode"};
        public static string[] Phrases {get{return new[]{"qué es una VPN","para qué sirve una VPN","qué es una uve pe ene","qué es una ve pe ene",
            "qué es una API","qué es SQL","cuánta RAM estoy utilizando","qué aplicaciones tengo instaladas","analiza los archivos temporales","abre Visual Studio",
            "abre Word","abre guord","abre Microsoft Word","abre el editor de Microsoft","abre documentos de Microsoft","inicia Word",
            "qué es una red privada virtual","para qué sirve una red privada virtual","qué es una uve pé ene"};}}
        public static bool Valid(TechnicalWord row)
        {return row!=null && row.Spoken!=null && row.Written!=null &&
            Regex.IsMatch(row.Spoken,@"^[\p{L}\p{N} .#-]{2,48}$") && Regex.IsMatch(row.Written,@"^[\p{L}\p{N} .#-]{2,48}$") &&
            !String.IsNullOrWhiteSpace(row.Spoken) && !String.IsNullOrWhiteSpace(row.Written) &&
            (String.IsNullOrEmpty(row.Context) || new[]{"Aplicaciones","Preguntas técnicas","Agenda"}.Contains(row.Context));}
        static bool InContext(TechnicalWord word,string text)
        {
            if(String.IsNullOrEmpty(word.Context))return true;
            if(word.Context=="Aplicaciones")return Regex.IsMatch(text,@"(?i)^\s*(?:bali[,\s]+)?(?:abre|inicia|busca|cierra)\b");
            if(word.Context=="Agenda")return Regex.IsMatch(text,@"(?i)\b(?:agenda|recu[eé]rdame|pendientes?|recordatorio|proyecto)\b");
            return Regex.IsMatch(text,@"(?i)^[\s¿]*(?:bali[,\s]+)?(?:qu[eé]|c[oó]mo|para qu[eé]|expl[ií]came)\b");
        }
        public static bool IsAmbiguousVpn(string text)
        {
            return !String.IsNullOrWhiteSpace(text) && Regex.IsMatch(text,
                @"^[\s¿]*(?:bali[,\s]+)?(?:qu[eé] es|para qu[eé] sirve|c[oó]mo funciona)\s+(?:una?\s+)?(?:wpn|w\s+p\s+n|uve\s+bene)(?=[.!?¿\s]*$)",
                RegexOptions.IgnoreCase);
        }
        public static string Normalize(string text,IEnumerable<TechnicalWord> custom)
        {
            if(String.IsNullOrWhiteSpace(text))return text;
            bool context=Regex.IsMatch(text,@"(?i)\b(?:qu[eé]|c[oó]mo|abre|inicia|busca|ejecuta|cierra|memoria|disco|utiliza|explica|agenda|recu[eé]rdame|pendientes?|recordatorio|proyecto)\b");
            if(!context)return text;
            string original=text;
            var approved=custom.Where(x=>Valid(x) && InContext(x,original)).Take(100)
                .OrderByDescending(x=>!String.IsNullOrEmpty(x.Context)).ThenByDescending(x=>x.Spoken.Length).ToArray();
            if(approved.Length>0)
            {
                var forms=String.Join("|",approved.Select(x=>Regex.Escape(x.Spoken.Trim())).Distinct(StringComparer.OrdinalIgnoreCase));
                text=Regex.Replace(text,@"(?<![\p{L}\p{N}])(?:"+forms+@")(?![\p{L}\p{N}])",
                    hit=>approved.First(x=>x.Spoken.Trim().Equals(hit.Value,StringComparison.OrdinalIgnoreCase)).Written.Trim(),RegexOptions.IgnoreCase);
            }
            foreach(var pair in Forms)
            {
                int split=pair.IndexOf(':');
                text=Replace(text,pair.Substring(0,split),pair.Substring(split+1));
            }
            text=Regex.Replace(text,@"^([\s¿]*(?:bali[,\s]+)?(?:qu[eé] es|para qu[eé] sirve|c[oó]mo funciona)\s+(?:una?\s+)?)(?:uve\s+bene|wpn|w\s+p\s+n)(?=[.!?¿\s]*$)",
                "$1VPN",RegexOptions.IgnoreCase);
            text=Regex.Replace(text,@"^([\s¿]*(?:bali[,\s]+)?(?:qu[eé] es|para qu[eé] sirve|c[oó]mo funciona)\s+)(?:(?:una|la)\s+)?red privada virtual(?=[.!?¿\s]*$)",
                "$1una VPN",RegexOptions.IgnoreCase);
            text=Regex.Replace(text,@"^([\s¡¿]*(?:bali[,\s]+)?(?:abre|inicia)\s+)(?:el\s+)?(?:editor de Microsoft|documentos de Microsoft|(?:Microsoft\s+)?(?:word|guord|güord|uord))(?=[.!?¡¿\s]*$)",
                "$1Word",RegexOptions.IgnoreCase);
            return text;
        }
        static string Replace(string text,string forms,string replacement)
        {return Regex.Replace(text,@"(?<![\p{L}\p{N}])(?:"+forms+@")(?![\p{L}\p{N}])",_=>replacement,RegexOptions.IgnoreCase);}
    }
    [DataContract] enum DesktopStepKind
    {
        [EnumMember] Open, [EnumMember] Folder, [EnumMember] Agenda, [EnumMember] Focus
    }
    [DataContract] sealed class DesktopStep
    {
        [DataMember] public DesktopStepKind Kind;
        [DataMember] public string Target;
        public override string ToString()
        {
            switch(Kind)
            {
                case DesktopStepKind.Open: return "Abrir "+Target;
                case DesktopStepKind.Folder: return "Carpeta "+Target;
                case DesktopStepKind.Agenda: return "Consultar agenda";
                default: return "Modo Concentrado";
            }
        }
    }
    [DataContract] sealed class DesktopRoutine
    {
        [DataMember] public string Name;
        [DataMember] public List<DesktopStep> Steps=new List<DesktopStep>();
        public override string ToString(){return Name;}
    }
}

using System;
using System.Text.RegularExpressions;

namespace BaliDesktop
{
    enum SpokenReply { Yes, No, Cancel, Other }

    sealed class SpokenQuestion
    {
        public string Text { get; private set; }
        double until;
        public bool HasSession { get { return until>0; } }
        public bool Active(double now) { return now<until; }
        public bool AwaitingAnswer(double now) { return Text!=null && Active(now); }
        public void Ask(string text,double now) { Text=text;until=now+25; }
        public void Repeat(double now) { Text=null;until=now+15; }
        public void Clear() { Text=null;until=0; }

        public static string Normalize(string text)
        {
            return Regex.Replace((text??"").Trim(' ','¿','?','¡','!','.'),@"\s+"," ").ToLowerInvariant();
        }
        public static SpokenReply Answer(string text)
        {
            string value=Normalize(text);
            if(value=="sí" || value=="si" || value=="sí es correcto" || value=="sí, es correcto")return SpokenReply.Yes;
            if(value=="no" || value=="no es eso")return SpokenReply.No;
            if(value=="cancela" || value=="cancelar" || value=="olvídalo")return SpokenReply.Cancel;
            return SpokenReply.Other;
        }
        public static readonly string[] DesktopAnswers={"sí","sí autorizo","sí, autorizo","autorizo la aplicación","confirmo",
            "confirma","sí es correcto","no","no lo autorices","cancela","cancelar","olvídalo"};
        public static SpokenReply DesktopAnswer(string text)
        {
            string value=Normalize(text).Replace(",","");
            if(value=="sí autorizo" || value=="si autorizo" || value=="autorizo la aplicación" || value=="autorizo la aplicacion" ||
                value=="confirmo" || value=="confirma")return SpokenReply.Yes;
            if(value.StartsWith("no ",StringComparison.Ordinal))return SpokenReply.No;
            return Answer(value);
        }
        public static bool NamesAppToAuthorize(string text,string name)
        {
            if(String.IsNullOrWhiteSpace(name))return false;
            string answer=DesktopCatalog.Normalize(text);
            string target=DesktopCatalog.Normalize(name);
            return target.Length>1 && (answer=="autoriza "+target || answer=="autorizo "+target ||
                answer=="si autorizo "+target || answer=="si autoriza "+target);
        }
        public static SpokenReply OpeningFallback(SpeechResult windows,SpeechResult whisper,string name,bool heardVoice)
        {
            if(String.IsNullOrWhiteSpace(name) || whisper==null || !whisper.HasText || whisper.Rejected!=0)return SpokenReply.Other;
            var first=DesktopAnswer(windows.Text);
            if(first==SpokenReply.No || first==SpokenReply.Cancel)return first;
            var second=DesktopAnswer(whisper.Text);
            if(second==SpokenReply.No || second==SpokenReply.Cancel)return second;
            if(NamesAppToAuthorize(whisper.Text,name))return SpokenReply.Yes;
            string answer=Normalize(whisper.Text);
            if(heardVoice && (!windows.HasText || first==SpokenReply.Yes) && (answer=="sí" || answer=="si"))return SpokenReply.Yes;
            return windows.Rejected==0 && windows.Confidence.GetValueOrDefault()>=.55 &&
                first==SpokenReply.Yes && second==SpokenReply.Yes?SpokenReply.Yes:SpokenReply.Other;
        }
        public static string Correction(string text)
        {
            return Regex.Replace(text,@"^(?:no\s*[,.;:]\s*|(?:quise decir|pregunt[eé])\s+|no\s+(?=qu[eé]|c[oó]mo|cu[aá]l|por qu[eé]))","",RegexOptions.IgnoreCase);
        }
        public static bool IsInformational(string text)
        {
            string value=Normalize(text);
            return value.Length<=480 && VoiceSilenceDetector.IsQuestion(value) &&
                Regex.IsMatch(value,@"^(?:(?:bali|y)[,\s]+)*(?:qu[eé]|c[oó]mo|cu[aá]l(?:es)?|cu[aá]nt[oa]s?|qui[eé]n(?:es)?|d[oó]nde|cu[aá]ndo|por qu[eé]|para qu[eé]|expl[ií]came|dime qu[eé]|puedes explicarme)\s+\S");
        }
        public static bool ClearWhisper(string text)
        {
            return IsInformational(text) && Regex.Matches(Normalize(text),@"[\p{L}\p{N}]+").Count>=5 &&
                !Regex.IsMatch(text,@"\.\.\.|…|[\[\]]|\b(\w+)\s+\1\b|https?://",RegexOptions.IgnoreCase);
        }
        public static string ForSpeech(string answer)
        {
            string plain=Regex.Replace(answer??"",@"\[(?<text>[^\]]+)\]\(\S+?\)","$"+"{text}");
            plain=Regex.Replace(plain,@"\x60{3}[\s\S]*?(?:\x60{3}|$)"," Puedes ver el código en Conversar. ");
            plain=Regex.Replace(plain,@"(?:https?://|www\.)\S+","",RegexOptions.IgnoreCase);
            plain=Regex.Replace(plain,@"(?m)^\s*(?:#{1,6}|[-*+>])\s+","");
            plain=Regex.Replace(plain,@"[*_\x60#]","");
            return Regex.Replace(plain,@"\s+"," ").Trim();
        }
    }
}

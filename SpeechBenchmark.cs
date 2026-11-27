using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading;

namespace BaliDesktop
{
    static class SpeechBenchmark
    {
        public static int Vision(string folder)
        {
            Directory.CreateDirectory(folder);
            using(var log=new StreamWriter(Path.Combine(folder,"visual-speech.txt")))
            using(var synth=new SpeechSynthesizer())
            using(var voice=new Voice())
            {
                synth.SelectVoice(synth.GetInstalledVoices().First(x=>x.Enabled && x.VoiceInfo.Name.Contains("Helena")).VoiceInfo.Name);
                var engines=new ISpeechRecognizer[]{new WindowsRecognizer(voice),new WhisperRecognizer(true)}.Where(x=>x.Available).ToArray();
                foreach(string phrase in new[]{"Lee la ventana","Lee esta ventana","Mira la pantalla","Leer ventana","Abre la calculadora",
                    "Minimiza esta aplicación","Desplázate hacia abajo","Cambia a la ventana anterior","Escribe lo siguiente","Termina el dictado"})
                {
                    var pcm=Speak(synth,phrase);
                    log.WriteLine("Frase: "+phrase);
                    foreach(var engine in engines)
                    using(var stop=new CancellationTokenSource(TimeSpan.FromSeconds(13)))
                    {
                        var result=engine.Recognize(pcm,true,stop.Token).GetAwaiter().GetResult();
                        log.WriteLine(result.Provider+" · "+(result.Text??"sin texto")+" · "+result.Confidence+" · "+VisualSpeech.Parse(result.Text)+
                            " · control "+ComputerRequest.Parse(result.Text).Verb+" · "+result.DecodeSeconds.ToString("0.00")+" s · pico "+(result.PeakBytes/1048576d).ToString("0.0")+" MiB");
                    }
                    log.Flush();
                }
            }
            return 0;
        }
        public static int Approval(string folder)
        {
            Directory.CreateDirectory(folder);
            using(var log=new StreamWriter(Path.Combine(folder,"approval.txt")))
            using(var synth=new SpeechSynthesizer())
            using(var voice=new Voice())
            {
                synth.SelectVoice(synth.GetInstalledVoices().First(x=>x.Enabled && x.VoiceInfo.Name.Contains("Helena")).VoiceInfo.Name);
                var recognizer=new WindowsRecognizer(voice);
                bool valid=true;
                foreach(string phrase in new[]{"Sí","Sí, autorizo","Autorizo la aplicación","Cancela","La ventana está cerrada","No lo autorices"})
                {
                    var result=recognizer.Recognize(Speak(synth,phrase),true,CancellationToken.None,true).GetAwaiter().GetResult();
                    bool allowed=!result.NeedsReview && result.Confidence.Value>=.72 && SpokenQuestion.DesktopAnswer(result.Text)==SpokenReply.Yes;
                    bool expected=phrase=="Sí" || phrase=="Sí, autorizo" || phrase=="Autorizo la aplicación";
                    log.WriteLine(phrase+" · "+result.Text+" · confianza "+result.Confidence+" · rechazos "+result.Rejected+" · autorización "+allowed);
                    valid&=allowed==expected;
                }
                return valid?0:1;
            }
        }
        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            using(var log=new StreamWriter(Path.Combine(folder,"stt-comparison.txt")))
            using(var synth=new SpeechSynthesizer())
            {
                var helena=synth.GetInstalledVoices().First(x=>x.Enabled && x.VoiceInfo.Name.Contains("Helena"));
                synth.SelectVoice(helena.VoiceInfo.Name);
                var engines=new ISpeechRecognizer[]{new WindowsRecognizer(new Voice()),new WhisperRecognizer(),new WhisperRecognizer(true),new WhisperRecognizer("small")}
                    .Where(x=>x.Available).ToArray();
                log.WriteLine("Sintético en memoria · "+synth.Voice.Name);
                var shortPhrase="Abre calculadora";
                var longPhrase="Avísame dentro de cinco minutos que revise los pendientes del proyecto de mañana";
                var clockPhrase="Mañana a las diez de la mañana";
                byte[] clean=Speak(synth,shortPhrase), longer=Speak(synth,longPhrase), time=Speak(synth,clockPhrase);
                byte[] background=Speak(synth,"Ahora presentamos las noticias de esta tarde. Continuamos con el pronóstico de los próximos días.");
                Measure(log,engines,"limpia corta",shortPhrase,clean);
                Measure(log,engines,"limpia larga",longPhrase,longer);
                Measure(log,engines,"horario y pausa",clockPhrase,Speak(synth,"Mañana, a las diez de la mañana."));
                Measure(log,engines,"ruido continuo simulado",longPhrase,Mixed(longer,null,430));
                Measure(log,engines,"televisión simulada",clockPhrase,Mixed(time,background,0));
                Measure(log,engines,"voz superpuesta simulada",longPhrase,Mixed(longer,clean,120));
                const string mexican="Oye Bali, chécame los pendientes de hoy";
                Measure(log,engines,"expresión coloquial mexicana",mexican,Speak(synth,mexican));
                Measure(log,engines,"sigla técnica deletreada","qué es una uve pe ene",Speak(synth,"¿Qué es una uve pe ene?"));
                Measure(log,engines,"nombre de aplicación","abre Brave",Speak(synth,"Abre Brave."));
                Measure(log,engines,"permiso hablado","sí autorizo",Speak(synth,"Sí, autorizo."));
                if(engines.Any(x=>x.Name=="Whisper Small"))
                {
                    synth.Rate=4;
                    Measure(log,engines,"voz más rápida","abre el editor de Microsoft y consulta lo pendiente para hoy",
                        Speak(synth,"Abre el editor de Microsoft y consulta lo pendiente para hoy."));
                    synth.Rate=-3;
                    Measure(log,engines,"voz lenta con pausa",longPhrase,Speak(synth,"Avísame dentro de cinco minutos, que revise los pendientes del proyecto de mañana."));
                    synth.Rate=0;
                    var contextual=new ISpeechRecognizer[]{new WhisperRecognizer(true),new WhisperRecognizer("base",true)};
                    Measure(log,contextual,"contexto inicial A/B","abre Brave",Speak(synth,"Abre Brave."));
                    Measure(log,contextual,"contexto inicial A/B VPN","qué es una uve pe ene",Speak(synth,"¿Qué es una uve pe ene?"));
                    Measure(log,contextual,"contexto inicial A/B en silencio",null,new byte[32000]);
                }
                Measure(log,engines,"silencio",null,new byte[32000]);
                Measure(log,engines,"consecutiva",shortPhrase,clean);
                foreach(var engine in engines.Where(x=>x.Available))
                using(var stop=new CancellationTokenSource())
                {
                    var decode=engine.Recognize(longer,true,stop.Token);
                    stop.Cancel();
                    try { decode.GetAwaiter().GetResult(); log.WriteLine("FAIL cancelación "+engine.Name); return 1; }
                    catch(OperationCanceledException){log.WriteLine("PASS cancelación "+engine.Name);}
                }
            }
            return 0;
        }
        static byte[] Speak(SpeechSynthesizer synth,string text)
        {
            using(var stream=new MemoryStream())
            {
                synth.SetOutputToAudioStream(stream,new SpeechAudioFormatInfo(16000,AudioBitsPerSample.Sixteen,AudioChannel.Mono));
                synth.Speak(text);
                synth.SetOutputToNull();
                return stream.ToArray();
            }
        }
        static byte[] Mixed(byte[] source,byte[] background,int noise)
        {
            var output=(byte[])source.Clone();
            var random=new Random(172);
            for(int i=0;i<output.Length-1;i+=2)
            {
                int sample=unchecked((short)(source[i]|source[i+1]<<8))+random.Next(-noise,noise+1);
                if(background!=null && i+1<background.Length)
                    sample+=unchecked((short)(background[i]|background[i+1]<<8))/2;
                sample=Math.Max(-32768,Math.Min(32767,sample));
                output[i]=unchecked((byte)sample);output[i+1]=unchecked((byte)(sample>>8));
            }
            return output;
        }
        static void Measure(TextWriter log,ISpeechRecognizer[] engines,string scenario,string expected,byte[] pcm)
        {
            log.WriteLine(scenario+" · esperado: "+(expected??"(silencio)"));
            foreach(var engine in engines)
            using(var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using(var host=Process.GetCurrentProcess())
            {
                try
                {
                    if(!engine.Available){log.WriteLine(engine.Name+" | no disponible");continue;}
                    var before=host.TotalProcessorTime;
                    var result=engine.Recognize(pcm,true,limit.Token).GetAwaiter().GetResult();
                    host.Refresh();
                    log.WriteLine(String.Format(CultureInfo.InvariantCulture,
                        "{0} | {1:0.00} s | CPU anfitrión {2:0.000} s | RAM anfitrión {3:0.0} MiB | CPU Whisper {4:0.000} s | pico Whisper {5:0.0} MiB | confianza {6} | rechazos {7} | WER {8} | {9} | {10}",
                        result.Provider,result.DecodeSeconds,(host.TotalProcessorTime-before).TotalSeconds,host.WorkingSet64/1048576.0,
                        result.CpuSeconds,result.PeakBytes/1048576.0,result.Confidence,result.Rejected,
                        ErrorRate(expected,result.Text),result.Text??result.Rejection,result.Model));
                }
                catch(Exception ex){log.WriteLine(engine.Name+" | "+ex.GetType().Name+" | "+ex.Message);}
            }
            log.Flush();
        }
        internal static string ErrorRate(string expected,string actual)
        {
            if(expected==null)return String.IsNullOrWhiteSpace(actual)?"silencio correcto":"falso positivo";
            var a=Regex.Matches(expected.ToLowerInvariant(),@"[\p{L}\p{N}]+").Cast<Match>().Select(x=>x.Value).ToArray();
            var b=Regex.Matches((actual??"").ToLowerInvariant(),@"[\p{L}\p{N}]+").Cast<Match>().Select(x=>x.Value).ToArray();
            var edits=new int[a.Length+1,b.Length+1];
            for(int i=0;i<=a.Length;i++)edits[i,0]=i;
            for(int j=0;j<=b.Length;j++)edits[0,j]=j;
            for(int i=1;i<=a.Length;i++)
                for(int j=1;j<=b.Length;j++)
                    edits[i,j]=Math.Min(edits[i-1,j-1]+(a[i-1]==b[j-1]?0:1),1+Math.Min(edits[i-1,j],edits[i,j-1]));
            return a.Length==0?"sin palabras de referencia":(edits[a.Length,b.Length]/(double)a.Length).ToString("P0",CultureInfo.InvariantCulture);
        }
    }
}

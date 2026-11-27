using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;

namespace BaliDesktop
{
    static class SpeechTimingProbe
    {
        public static int Run(string path)
        {
            using(var log=new StreamWriter(path) { AutoFlush=true })
            using(var synth=new SpeechSynthesizer())
            using(var done=new ManualResetEventSlim())
            {
                synth.SelectVoice("Microsoft Helena Desktop");
                synth.SetOutputToDefaultAudioDevice();
                synth.Volume=40;
                var clock=Stopwatch.StartNew();
                Action<string> line=s=>{ lock(log) log.WriteLine("{0:0} ms · {1}",clock.Elapsed.TotalMilliseconds,s); };
                synth.SpeakStarted+=(s,e)=>line("inicio");
                synth.VisemeReached+=(s,e)=>line(String.Format("visema {0} → {1} · audio {2:0} ms · duración {3:0} ms",e.Viseme,e.NextViseme,e.AudioPosition.TotalMilliseconds,e.Duration.TotalMilliseconds));
                synth.PhonemeReached+=(s,e)=>line(String.Format("fonema {0} · audio {1:0} ms · duración {2:0} ms",
                    String.Join(" ",e.Phoneme.Select(c=>"U+"+((int)c).ToString("X4"))),e.AudioPosition.TotalMilliseconds,e.Duration.TotalMilliseconds));
                synth.SpeakProgress+=(s,e)=>line("palabra · audio "+e.AudioPosition.TotalMilliseconds.ToString("0")+" ms");
                synth.SpeakCompleted+=(s,e)=>{ line("fin · cancelado="+e.Cancelled+" · error="+(e.Error==null?"ninguno":e.Error.GetType().Name)); done.Set(); };
                synth.SpeakAsync("Hola. Una pausa, por favor. Sí, puedo continuar.");
                if(!done.Wait(20000)) { synth.SpeakAsyncCancelAll(); return 1; }
                return 0;
            }
        }
    }
}

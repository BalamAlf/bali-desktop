using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void CheckLipSync(TextWriter log)
        {
            var lips=new LipSync();
            lips.Viseme(2,8,0,.12,4);
            Check(lips.Sample(4.05).Opening==0,"Visemas fuera de reproducción no abren la boca",log);
            lips.Start();
            Check(lips.Sample(4.5).Opening==0,"Preparación y escucha mantienen la boca cerrada",log);
            lips.Viseme(0,2,0,.10,4.57);
            Check(lips.Sample(4.64).Opening==0,"Silencio inicial respeta el reloj del primer audio",log);
            lips.Viseme(2,8,.10,.12,4.67);
            double early=lips.Sample(4.68).Opening, later=lips.Sample(4.73).Opening;
            Check(early>0 && later>early && later<=.82 && lips.CurrentShape==MouthShape.Amplia,"Apertura interpolada con tiempos reales",log);
            lips.Viseme(8,0,.22,.10,4.79);
            Check(lips.Sample(4.86).Width<.85,"Visema redondeado conserva su silueta",log);
            lips.Viseme(0,0,.32,.75,4.89);
            Check(lips.Sample(5.2).Opening==0 && lips.Sample(5.61).Opening==0,"Pausa de puntuación mantiene el cierre",log);
            lips.Viseme(2,0,1.07,.21,5.64);
            lips.Pause(5.72);
            Check(lips.Sample(6.0).Opening==0,"Pausa de reproducción cierra la boca",log);
            lips.Resume(6.2);
            Check(lips.Sample(6.25).Opening>0 && lips.Sample(7).Opening==0,"Reanudación conserva la temporización y caducidad",log);
            lips.End();
            lips.Viseme(2,0,1.2,.2,7.1);
            Check(lips.Sample(7.2).Opening==0,"Fin y cancelación descartan eventos tardíos",log);
            lips.Start();
            lips.Phoneme("o",0,.12,8);
            Check(lips.Sample(8.06).Opening>0 && lips.CurrentShape==MouthShape.Redondeada,"Respaldo fonético con posición real",log);
            lips.Viseme(21,0,0,.13,8.02);
            lips.Phoneme("a",.05,.12,8.07);
            Check(lips.HasVisemes && lips.Sample(8.10).Opening==0,"Los visemas prevalecen y el cierre bilabial se respeta",log);
            lips.End(); lips.Start();
            lips.Intensity=0;
            lips.Viseme(2,0,0,.13,9);
            Check(lips.Sample(9.06).Opening==0 && lips.LastViseme==2,"Intensidad cero y reproducción consecutiva limpia",log);
            var e=new BaliExpressionEngine(1);
            foreach(FaceStyle style in Enum.GetValues(typeof(FaceStyle)))
            {
                var face=new Face(e,style);
                var hash=Enum.GetValues(typeof(MouthShape)).Cast<MouthShape>().Select(shape=>
                {
                    face.SpeechMouth=MouthPose.Shape(shape);
                    face.InvalidateVisual();
                    using(var sha=System.Security.Cryptography.SHA256.Create())
                        return Convert.ToBase64String(sha.ComputeHash(Render(face)));
                }).Distinct().Count();
                Check(hash==7,"Siete formas diferenciadas en render WPF "+style,log);
            }
        }
    }

    sealed partial class BaliApp
    {
        int lipCase;
        bool lipOpened, lipSilence, lipPaused, lipResumed, lipFailure;
        double lipCaseTime, lipPausedAt, lastRunCpu;

        void LipValidationTick(double now)
        {
            if(lipCase==0)
            {
                if(now<1) return;
                busyConversation=true;
                UpdateSpeechMouth();
                bool processingClosed=face.SpeechMouth.HasValue && face.SpeechMouth.Value.Opening==0 &&
                    !speaking && usageLedger.Rows.Count==0 && chatService==null;
                busyConversation=false;
                lipFailure|=!processingClosed;
                WriteResult((processingClosed?"PASS":"FAIL")+" Procesamiento simulado: boca cerrada y ninguna solicitud de red.");
                settings.VoiceName="Microsoft Helena Desktop";
                settings.Volume=32;
                lipCase=1;
                BeginLipCase(now,LipShort);
                return;
            }
            if(now-lipCaseTime>24)
            {
                WriteResult("FAIL tiempo límite en caso "+lipCase);
                StopPlayback();
                Shutdown(1);
                return;
            }

            var mouth=face.SpeechMouth;
            bool open=mouth.HasValue && mouth.Value.Opening>.13;
            if(speechPending || speaking && visemeCount==0 && phonemeCount==0)
                lipFailure|=!mouth.HasValue || mouth.Value.Opening!=0;
            if(open && !lipOpened)
            {
                lipOpened=true;
                if(lipCase==1)
                {
                    nextLipRead=0;
                    UpdateSpeechMouth();
                    Capture(panel,"voz-abierta.png");
                }
            }
            if(speaking && lips.LastViseme==0 && lastVisemeDuration>=.32 && !open) lipSilence=true;

            if(lipCase==2 && open && !lipPaused)
            {
                ToggleSpeechPause();
                lipPaused=lips.Paused;
                lipPausedAt=now;
                return;
            }
            if(lipCase==2 && lipPaused && !lipResumed && now-lipPausedAt>.35)
            {
                lipFailure|=!mouth.HasValue || mouth.Value.Opening!=0;
                ToggleSpeechPause();
                lipResumed=!lips.Paused;
                return;
            }
            if(lipCase==3 && open)
            {
                StopPlayback();
                bool closed=face.SpeechMouth.HasValue && face.SpeechMouth.Value.Opening==0 && !lips.Playing;
                LogLipCase("Cancelación durante un visema",closed);
                Capture(panel,"voz-cancelada.png");
                lipCase=4;
                lipCaseTime=now;
                return;
            }
            if(lipCase==4)
            {
                if(now-lipCaseTime<.45) { lipFailure|=open; return; }
                lipCase=5;
                BeginLipCase(now,LipShort);
                return;
            }

            if(speechPending || speaking || now-lipCaseTime<.12) return;
            bool ended=visemeCount>0 && lipOpened && !open && lipStatus=="Reproducción finalizada.";
            if(lipCase==2) ended=ended && lipPaused && lipResumed && lipSilence;
            var scenario=lipCase==1?"Frase corta y cierre":
                lipCase==2?"Frase larga con silencio, pausa manual, reanudación y cierre":"Reproducción posterior a cancelación";
            LogLipCase(scenario,ended);
            if(lipCase==1) { lipCase=2; BeginLipCase(now,LipLong); }
            else if(lipCase==2) { lipCase=3; BeginLipCase(now,LipLong); }
            else { Shutdown(lipFailure?1:0); }
        }
        void BeginLipCase(double now,string phrase)
        {
            lipOpened=lipSilence=lipPaused=lipResumed=false;
            lipCaseTime=now;
            using(var process=Process.GetCurrentProcess()) lastRunCpu=process.TotalProcessorTime.TotalSeconds;
            Play(phrase);
        }
        void LogLipCase(string name,bool ok)
        {
            lipFailure|=!ok;
            using(var process=Process.GetCurrentProcess())
            {
                double seconds=Math.Max(.01,clock.Elapsed.TotalSeconds-lipCaseTime);
                double cpu=100*(process.TotalProcessorTime.TotalSeconds-lastRunCpu)/seconds/Environment.ProcessorCount;
                WriteResult(String.Format("{0} {1} · {2} visemas · {3} fonemas · {4} palabras · {5:0.00} s · CPU {6:0.00}% · RAM {7:0.0} MiB · privada {8:0.0} MiB",
                    ok?"PASS":"FAIL",name,visemeCount,phonemeCount,wordCount,seconds,cpu,process.WorkingSet64/1048576.0,process.PrivateMemorySize64/1048576.0));
            }
        }
    }
}

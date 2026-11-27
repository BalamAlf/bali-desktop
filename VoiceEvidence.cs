using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Threading;
using System.Windows;

namespace BaliDesktop
{
    static partial class Tests
    {
        static string Sample(string label,Action action)
        {
            using(var process=Process.GetCurrentProcess())
            {
                var timer=Stopwatch.StartNew();
                double cpu=process.TotalProcessorTime.TotalSeconds;
                action();
                process.Refresh();
                return String.Format("{0} · {1:0.00} s · CPU {2:0.00} % · trabajo {3:0.0} MiB · privada {4:0.0} MiB",
                    label,timer.Elapsed.TotalSeconds,100*(process.TotalProcessorTime.TotalSeconds-cpu)/Math.Max(.01,timer.Elapsed.TotalSeconds)/Environment.ProcessorCount,
                    process.WorkingSet64/1048576.0,process.PrivateMemorySize64/1048576.0);
            }
        }
        public static int AudioEvidence(string folder)
        {
            using(var report=new StreamWriter(Path.Combine(folder,"audio-windows.txt")))
            using(var audio=new Voice())
            {
                foreach(var r in SpeechRecognitionEngine.InstalledRecognizers()) report.WriteLine("Reconocedor: "+r.Culture.Name);
                foreach(var device in MicrophoneCapture.Devices()) report.WriteLine("Entrada: "+device.Name);
                var names=audio.Names;
                foreach(var name in names) report.WriteLine("Voz válida: "+name);
                try
                {
                    int bytes=0;
                    report.WriteLine(Sample("Captura física sin persistir ni transcribir",()=>
                    {
                        using(var capture=new MicrophoneCapture(UInt32.MaxValue))
                        {
                            Thread.Sleep(8000);
                            var pcm=capture.Finish();
                            bytes=pcm.Length;
                            Array.Clear(pcm,0,pcm.Length);
                        }
                    }));
                    report.WriteLine((bytes>128000?"PASS ":"NO VERIFICADA ")+"captura y liberación del micrófono · "+bytes+" bytes de PCM");
                }
                catch(InvalidOperationException ex) { report.WriteLine("Captura no disponible: "+ex.Message); }
                catch(Exception) { report.WriteLine("Captura no disponible en esta sesión."); }
                if(names.Length==0) return 0;
                try
                {
                    using(var synth=new SpeechSynthesizer())
                    {
                        synth.SelectVoice(names[0]);
                        synth.Volume=40;
                        synth.SetOutputToDefaultAudioDevice();
                        report.WriteLine(Sample("Síntesis y reproducción por dispositivo local",()=>synth.Speak(
                            "Hola, soy Bali. Esta es una comprobación breve de mi voz en Windows. La reproducción termina al final de esta frase.")));
                        report.WriteLine("PASS salida de voz completada por Windows.");
                    }
                    using(var synth=new SpeechSynthesizer())
                    using(var pcm=new MemoryStream())
                    {
                        synth.SelectVoice(names[0]);
                        synth.SetOutputToAudioStream(pcm,new SpeechAudioFormatInfo(16000,AudioBitsPerSample.Sixteen,AudioChannel.Mono));
                        synth.Speak("Hoy quiero hablar de música y de libros.");
                        synth.SetOutputToNull();
                        byte[] generated=pcm.ToArray();
                        using(var limit=new CancellationTokenSource(TimeSpan.FromSeconds(12)))
                        {
                            var read=audio.Transcribe(generated,limit.Token).GetAwaiter().GetResult();
                            report.WriteLine(String.IsNullOrEmpty(read)?"SIN RESULTADO en dictado con audio de prueba":"Dictado Windows con audio generado: "+read);
                        }
                        report.WriteLine("Síntesis PCM de prueba: "+generated.Length+" bytes, solo en memoria.");
                    }
                }
                catch(Exception ex) { report.WriteLine("Síntesis o dictado no completado: "+ex.GetType().Name); }
            }
            return 0;
        }
    }

    sealed partial class BaliApp
    {
        int voicePhase=-1;
        double voiceStarted, voiceCpu, nextText;
        bool intelCaptured;
        void VoiceValidationTick(double now)
        {
            if(!intelCaptured && now>1)
            {
                Capture(panel,"inteligencia.png");
                intelCaptured=true;
                panel.Close();
            }
            if(!intelCaptured) return;
            int phase=(int)((now-1)/16);
            if(phase==voicePhase)
            {
                if(phase==1 && now>=nextText)
                {
                    nextText=now+4;
                    Respond("Bali, ¿cuánta energía tienes?");
                }
                return;
            }
            if(voicePhase>=0)
            {
                using(var p=Process.GetCurrentProcess())
                    WriteResult(String.Format("{0} · {1:0.0} s · CPU {2:0.00} % · trabajo {3:0.0} MiB · privada {4:0.0} MiB",
                        new[]{"Reposo","Conversación por texto","Laboratorio","Modo local sin proveedor"}[voicePhase],
                        now-voiceStarted,100*(p.TotalProcessorTime.TotalSeconds-voiceCpu)/(now-voiceStarted)/Environment.ProcessorCount,
                        p.WorkingSet64/1048576.0,p.PrivateMemorySize64/1048576.0));
            }
            if(phase>=4)
            {
                bool local=usageLedger.Rows.Count==0 && chatService==null && settings.Memory.Interactions>=5;
                WriteResult((local?"PASS":"FAIL")+" WPF: comandos locales activos y cero peticiones de red.");
                Save();
                Shutdown(local?0:1);
                return;
            }
            voicePhase=phase;
            switch(phase)
            {
                case 1:
                    OpenPanel(); Respond("Bali, ¿cuánta energía tienes?");
                    Capture(panel,"conversacion-local.png");
                    nextText=now+4;
                    break;
                case 2: OpenPanel(AgentsTab); break;
                case 3:
                    settings.Intelligence.Enabled=false;
                    OpenPanel(); Respond("Bali, ¿qué objetivo tienes ahora?");
                    break;
            }
            using(var p=Process.GetCurrentProcess()) voiceCpu=p.TotalProcessorTime.TotalSeconds;
            voiceStarted=now;
        }
    }
}
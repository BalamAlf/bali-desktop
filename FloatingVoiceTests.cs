using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using Drawing=System.Drawing;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void CheckFloatingVoice(TextWriter log)
        {
            var detector=new VoiceSilenceDetector();
            var quiet=new byte[2560];
            var speech=new byte[2560];
            for(int i=0;i<speech.Length;i+=2)
            {
                short sample=(short)(Math.Sin(i*.17)*1800);
                speech[i]=(byte)sample; speech[i+1]=(byte)(sample>>8);
            }
            for(int i=0;i<15;i++) detector.Observe(quiet,quiet.Length);
            detector.Observe(speech,speech.Length);
            detector.Observe(quiet,quiet.Length);
            Check(!detector.HeardVoice && detector.NoSpeech(1.2),"Un impulso aislado no inicia un turno",log);
            for(int i=0;i<5;i++) detector.Observe(speech,speech.Length);
            for(int i=0;i<10;i++) detector.Observe(quiet,quiet.Length);
            Check(detector.HeardVoice && !detector.Ready(1.15),"Detecta voz y conserva una pausa breve",log);
            for(int i=0;i<5;i++) detector.Observe(quiet,quiet.Length);
            Check(detector.Ready(1.15),"Detecta el silencio final configurado",log);
            detector.Observe(speech,speech.Length);
            Check(!detector.Ready(1.15),"La continuación de la frase reinicia el silencio",log);
            Check(!VoiceSilenceDetector.IsQuestion("  ") && !VoiceSilenceDetector.IsQuestion("mmm") &&
                VoiceSilenceDetector.IsQuestion("¿Qué hora es?"),"Vacíos y fragmentos no autorizan consultas",log);
            var start=new Drawing.Point(100,200);
            Check(!BaliApp.IsDrag(start,new Drawing.Point(102,201),5,5) &&
                BaliApp.IsDrag(start,new Drawing.Point(100,205),5,5),"Distingue clic de arrastre en ambos ejes",log);
            var config=new IntelligenceSettings();
            Check(!config.ContinuousVoice && config.VoiceIdle==8 && config.VoiceSilence==1.15,"Continuidad desactivada por defecto",log);
        }

        public static int FloatingInput(string path)
        {
            using(var log=new StreamWriter(path))
            using(var voice=new Voice())
            using(var process=Process.GetCurrentProcess())
            {
                log.WriteLine(voice.Status);
                foreach(var input in MicrophoneCapture.Devices()) log.WriteLine("Entrada: "+input.Name);
                var time=Stopwatch.StartNew();
                double cpu=process.TotalProcessorTime.TotalSeconds;
                int bytes=0, finalBytes=0, frames=0;
                try
                {
                    using(var capture=new MicrophoneCapture(UInt32.MaxValue,true))
                    {
                        for(int i=0;i<60;i++)
                        {
                            Thread.Sleep(45);
                            bytes+=capture.Pump((_,count)=>frames++);
                        }
                        var data=capture.Finish();
                        finalBytes=data.Length;
                        Array.Clear(data,0,data.Length);
                    }
                    using(var capture=new MicrophoneCapture(UInt32.MaxValue,true)) { }
                    process.Refresh();
                    bool ok=frames>15 && bytes>60000 && finalBytes>=bytes && finalBytes-bytes<8192;
                    log.WriteLine(String.Format("{0} captura física por bloques y reapertura · {1} bloques · {2} bytes observados · {3} bytes PCM totales descartados",
                        ok?"PASS":"FAIL",frames,bytes,finalBytes));
                    log.WriteLine(String.Format("{0:0.00}s · CPU {1:0.00}% · RAM {2:0.0} MiB · privada {3:0.0} MiB",
                        time.Elapsed.TotalSeconds,100*(process.TotalProcessorTime.TotalSeconds-cpu)/time.Elapsed.TotalSeconds/Environment.ProcessorCount,
                        process.WorkingSet64/1048576.0,process.PrivateMemorySize64/1048576.0));
                    return ok?0:1;
                }
                catch(InvalidOperationException e) { log.WriteLine(e.Message); return 1; }
            }
        }
    }

    sealed partial class BaliApp
    {
        int floatStep;
        double floatStarted, floatCpu;
        bool floatObservedMouth, floatFailed;
        FloatingTestProvider floatingTestProvider;
        long floatDesktopMemory;
        string floatRoutineReport;
        sealed class FloatingTestProvider : IStreamingConversationProvider
        {
            public int Calls;
            public bool Fail;
            public string LastQuestion;
            public Task<ProviderUsage> StreamAsync(string key,IntelligenceSettings config,IList<ChatLine> messages,
                Action<string> chunk,Action<ProviderUsage> usage,CancellationToken cancel)
            {
                Interlocked.Increment(ref Calls);
                LastQuestion=messages[messages.Count-1].Text;
                if(Fail)throw new ConversationError("DeepSeek no está disponible en esta prueba.");
                chunk("**Una respuesta de prueba.** https://example.test/details");
                return Task.FromResult(new ProviderUsage{Input=20,Output=8,Total=28});
            }
        }
        void FloatingValidationTick(double now)
        {
            if(now<.75) return;
            if(floatStep==0)
            {
                settings.VoiceName="Microsoft Helena Desktop";
                settings.Volume=25;
                settings.Intelligence.Enabled=false;
                settings.Intelligence.ContinuousVoice=true;
                ChangeAppearance(FaceStyle.Minimalista);
                ToggleFloatingVoice();
                floatStarted=now;
                floatStep=1;
                return;
            }
            if(now-floatStarted>16) { WriteResult("FAIL tiempo límite del flujo flotante."); CancelConversation(); Shutdown(1); return; }
            if(floatStep==1 && now-floatStarted>.45)
            {
                bool capturing=capture!=null && floatingDetector!=null && panel==null;
                ToggleFloatingVoice();
                FloatResult("Minimalista: inicia y cancela captura real con el panel cerrado",capturing && capture==null && !floatingActive);
                ChangeAppearance(FaceStyle.Clásico);
                ToggleFloatingVoice();
                floatStep=2;
                floatStarted=now;
                return;
            }
            if(floatStep==2 && now-floatStarted>.45)
            {
                bool capturing=capture!=null && floatingDetector!=null && panel==null;
                ToggleFloatingVoice();
                FloatResult("Clásico: inicia y cancela captura real con el panel cerrado",capturing && capture==null && !floatingActive);
                floatingActive=true;
                floatObservedMouth=false;
                using(var process=Process.GetCurrentProcess()) floatCpu=process.TotalProcessorTime.TotalSeconds;
                RouteFloating("Bali, ¿cuánta energía tienes?");
                floatStep=3;
                floatStarted=now;
                return;
            }
            if(floatStep==3)
            {
                if(speaking || speechPending)
                {
                    floatFailed|=capture!=null;
                    floatObservedMouth|=face.SpeechMouth.HasValue && face.SpeechMouth.Value.Opening>.12;
                }
                if(capture==null) return;
                bool ready=followUp && floatObservedMouth && visemeCount>0 && panel==null && chatService==null &&
                    usageLedger.Rows.Count==0 && !speaking && !speechPending;
                if(!ready) WriteResult(String.Format("Detalle · seguimiento={0} boca={1} visemas={2} panel={3} red={4} uso={5} voz={6} espera={7}",
                    followUp,floatObservedMouth,visemeCount,panel!=null,chatService!=null,usageLedger.Rows.Count,speaking,speechPending));
                using(var p=Process.GetCurrentProcess())
                    WriteResult(String.Format("Turno local WPF · {0:0.0}s · CPU {1:0.00}% · RAM {2:0.0} MiB · privada {3:0.0} MiB",
                        now-floatStarted,100*(p.TotalProcessorTime.TotalSeconds-floatCpu)/(now-floatStarted)/Environment.ProcessorCount,
                        p.WorkingSet64/1048576.0,p.PrivateMemorySize64/1048576.0));
                FloatResult("Texto reconocido simulado → respuesta local y visemas de Helena → micrófono solo después del audio",ready);
                // Substitute an accepted transcript while preserving the live session and release order.
                capture.Dispose();
                capture=null;
                floatingDetector=null;
                ChangeAppearance(FaceStyle.Minimalista);
                floatObservedMouth=false;
                RouteFloating("Bali, ¿cuántos días tienes?");
                floatStep=4;
                floatStarted=now;
                return;
            }
            if(floatStep==4)
            {
                if(speaking || speechPending)
                {
                    floatFailed|=capture!=null;
                    floatObservedMouth|=face.SpeechMouth.HasValue && face.SpeechMouth.Value.Opening>.12;
                }
                if(capture==null) return;
                FloatResult("Segundo turno sin clic: conserva cuatro mensajes y mueve la boca del estilo minimalista",
                    followUp && floatObservedMouth && visemeCount>0 && session.Count==4 && panel==null &&
                    chatService==null && !speaking && !speechPending);
                ToggleFloatingVoice();
                FloatResult("Cancelación del turno de seguimiento libera entrada",capture==null && !floatingActive);
                settings.Intelligence.ContinuousVoice=false;
                floatingActive=true;
                RouteFloating("¿Qué es una VPN?");
                FloatResult("DeepSeek desactivado no abre el chat ni crea un proveedor",panel==null && chatService==null);
                CancelConversation();
                settings.Intelligence.Enabled=settings.Intelligence.Verified=true;
                vault.Store("credential-for-local-simulation");
                floatingTestProvider=new FloatingTestProvider();
                chatService=new ConversationService(usageLedger,floatingTestProvider);
                floatObservedMouth=false;
                SimulateQuestion("¿Qué es una VPN?",.9);
                FloatResult("Windows fiable inicia la consulta sin confirmar ni abrir Conversar",busyConversation && panel==null && !spokenQuestion.HasSession);
                floatStep=5;floatStarted=now;
                return;
            }
            if(floatStep==5)
            {
                if(speaking || speechPending)
                {floatObservedMouth|=face.SpeechMouth.HasValue && face.SpeechMouth.Value.Opening>.12;floatFailed|=capture!=null;return;}
                if(busyConversation || floatingActive)return;
                FloatResult("Respuesta hablada con Helena y visemas, chat cerrado",floatingTestProvider.Calls==1 &&
                    panel==null && visemeCount>0 && floatObservedMouth);
                lastOnlineVoiceTime=clock.Elapsed.TotalSeconds;
                SimulateQuestion("¿Qué es una VPN?",.9);
                FloatResult("Una pregunta repetida dentro del intervalo no se duplica",!busyConversation && floatingTestProvider.Calls==1);
                CancelConversation();
                SimulateQuestion("¿Cómo protege una VPN mis datos?",null);
                FloatResult("Whisper con una pregunta clara consulta directamente",busyConversation && panel==null && !spokenQuestion.HasSession);
                floatStep=6;floatStarted=now;
                return;
            }
            if(floatStep==6 && !busyConversation && !floatingActive)
            {
                FloatResult("Dos preguntas consecutivas sin abrir Conversar",floatingTestProvider.Calls==2 && panel==null);
                SimulateQuestion("¿Qué es una WPN?",null);
                FloatResult("Pregunta breve con Whisper pide confirmación, el transporte sigue inactivo",spokenQuestion.Text=="¿Qué es una VPN?" &&
                    !busyConversation && (speaking || speechPending) && panel==null && floatingTestProvider.Calls==2);
                floatStep=7;floatStarted=now;
                return;
            }
            if(floatStep==7 && capture!=null)
            {
                FloatResult("Reabre el micrófono para confirmar después de hablar, incluso con continuidad apagada",
                    followUp && !speaking && !speechPending && panel==null && spokenQuestion.HasSession);
                capture.Dispose();capture=null;floatingDetector=null;
                SimulateQuestion("No, qué es una red local",.88);
                FloatResult("Una corrección sustituye la pregunta original",busyConversation && !spokenQuestion.HasSession && panel==null);
                floatStep=8; floatStarted=now;
                return;
            }
            if(floatStep==8 && !busyConversation && !floatingActive)
            {
                SimulateQuestion("¿Qué es DNS?",null);
                StopPlayback();
                floatingActive=true;
                spokenQuestion.Ask("¿Qué es DNS?",now);
                floatingTestProvider.Fail=true;
                SimulateQuestion("sí",null);
                FloatResult("Sí hablado consume la confirmación una sola vez",busyConversation && !spokenQuestion.HasSession);
                floatStep=9; floatStarted=now;
                return;
            }
            if(floatStep==9 && !busyConversation && !floatingActive)
            {
                FloatResult("Proveedor simulado inaccesible deja el chat cerrado",panel==null && floatingTestProvider.Calls==4 &&
                    usageLedger.Rows.Count==4 && session.Count>=11);
                var routine=new DesktopRoutine{Name="Validación"};
                routine.Steps.Add(new DesktopStep{Kind=DesktopStepKind.Open,Target="aplicación ausente"});
                routine.Steps.Add(new DesktopStep{Kind=DesktopStepKind.Agenda});
                routine.Steps.Add(new DesktopStep{Kind=DesktopStepKind.Focus});
                using(var process=Process.GetCurrentProcess()){floatCpu=process.TotalProcessorTime.TotalSeconds;floatDesktopMemory=process.WorkingSet64;}
                StartRoutine(routine,"panel",false,message=>floatRoutineReport=message);
                floatStep=10;floatStarted=now;
                return;
            }
            if(floatStep==10 && floatRoutineReport!=null)
            {
                FloatResult("Rutina parcial: omite destino ausente, consulta agenda y activa Concentrado sin red",
                    floatRoutineReport.Contains("omitida") && floatRoutineReport.Contains("programados para hoy") &&
                    settings.Mode==Mode.Concentrado && floatingTestProvider.Calls==4 && panel==null);
                using(var process=Process.GetCurrentProcess()) WriteResult(String.Format("Rutina · {0:0.000} s · CPU {1:0.000} s · RAM {2:0.0} → {3:0.0} MiB",
                    now-floatStarted,process.TotalProcessorTime.TotalSeconds-floatCpu,floatDesktopMemory/1048576d,process.WorkingSet64/1048576d));
                floatingTestProvider.Fail=false;
                SimulateQuestion("¿Y para qué sirve?",.9);
                FloatResult("Pregunta de seguimiento conserva el flujo completamente hablado",busyConversation && panel==null);
                floatStep=11;floatStarted=now;
                return;
            }
            if(floatStep==11 && !busyConversation && !floatingActive)
            {
                FloatResult("El transporte recibe el último tema exitoso y la pregunta actual",
                    floatingTestProvider.Calls==5 && floatingTestProvider.LastQuestion.Contains("una red local") &&
                    floatingTestProvider.LastQuestion.Contains("para qué sirve") && panel==null);
                CancelConversation();
                SimulateQuestion("¿Y para qué sirve?",.9);
                FloatResult("Cancelar elimina el tema y evita una consulta ambigua",!busyConversation && floatingTestProvider.Calls==5);
                CancelConversation();Shutdown(floatFailed?1:0);
            }
        }
        void SimulateQuestion(string text,double? confidence)
        {
            floatingActive=true;
            lastSpeech=new SpeechResult{Text=text,Confidence=confidence};
            sttNeedsReview=lastSpeech.NeedsReview;
            RouteFloating(text);
        }
        void FloatResult(string scenario,bool ok)
        {
            floatFailed|=!ok;
            WriteResult((ok?"PASS ":"FAIL ")+scenario);
        }
    }
}

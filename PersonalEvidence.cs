using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        int assistantPhase=-1;
        double assistantStart,assistantCpu;
        bool assistantFailure, assistantLips, assistantOpened;
        Task<Reading> sampleReading;
        void PersonalValidationTick(double now)
        {
            if(now<1)return;
            if(assistantPhase==2 && speaking)
                assistantLips|=face.SpeechMouth.HasValue && face.SpeechMouth.Value.Opening>.11;
            if(assistantPhase>=0 && now-assistantStart<8)return;
            if(assistantPhase>=0)
                using(var process=Process.GetCurrentProcess())
                    WriteResult(String.Format("{0} · {1:0.0}s · CPU {2:0.00}% · RAM {3:0.0} MiB · privada {4:0.0} MiB",
                        new[]{"Reposo","Agenda","Turno de voz local inyectado","Extracción PDF","Laboratorio","Síntesis"}[assistantPhase],
                        now-assistantStart,100*(process.TotalProcessorTime.TotalSeconds-assistantCpu)/(now-assistantStart)/Environment.ProcessorCount,
                        process.WorkingSet64/1048576.0,process.PrivateMemorySize64/1048576.0));
            if(assistantPhase==2)
                AssistantResult("Turno flotante sin panel, recordatorio guardado y Helena con visemas",
                    assistantLips && panel==null && personal.Data.Items.Any(x=>x.Kind=="Recordatorio") && usageLedger.Rows.Count==0);
            if(assistantPhase==3)
            {
                bool read=sampleReading!=null && sampleReading.Status==TaskStatus.RanToCompletion;
                AssistantResult("PDF sintético recuperado por página",read && sampleReading.Result.Find("Agenda").Contains("Página 1"));
                if(read)WriteResult("Proceso PDF · "+sampleReading.Result.WorkerMetrics);
            }
            if(assistantPhase==4) AssistantResult("Laboratorio WPF conserva los tres agentes",agentCards.Count==3);
            if(assistantPhase==5)
            {
                AssistantResult("Síntesis local finalizada con visemas",visemeCount>0 && !speechPending && !speaking);
                AssistantResult("Cero solicitudes DeepSeek",chatService==null && usageLedger.Rows.Count==0);
                if(testCalculator) AssistantResult("Windows inició Calculadora desde el catálogo",assistantOpened);
                Shutdown(assistantFailure?1:0);
                return;
            }
            assistantPhase++;
            using(var process=Process.GetCurrentProcess())assistantCpu=process.TotalProcessorTime.TotalSeconds;
            assistantStart=now;
            switch(assistantPhase)
            {
                case 0:
                    settings.Intelligence.Enabled=false;settings.VoiceName="Microsoft Helena Desktop";settings.Volume=28;
                    settings.Intelligence.ContinuousVoice=false;break;
                case 1:
                    AssistantResult("Pendiente creado y consultado sin proveedor",TryPersonal("Crea un pendiente para hoy revisar la agenda de prueba",false) &&
                        TryPersonal("Qué pendientes tengo hoy",false) && lastSpoken.Contains("revisar la agenda"));
                    OpenPanel(AgendaTab);Capture(panel,"agenda.png");break;
                case 2:
                    panel.Close();floatingActive=true;
                    RouteFloating("Bali recuérdame en cinco minutos beber agua");break;
                case 3:
                    OpenPanel(AgendaTab+2);
                    string fixture=Path.Combine(testFolder,"fixture.pdf");
                    Tests.MakePdf(fixture,true);
                    sampleReading=Task.Run(()=>PersonalDocuments.Load(fixture,CancellationToken.None));
                    break;
                case 4: OpenPanel(AgentsTab);break;
                case 5:
                    panel.Close();Play("Prueba final de voz de Bali.");
                    if(testCalculator)assistantOpened=toolsRouter.Execute(new ToolRequest{Id="abrir",Text="calculadora"},"panel",DateTime.Now).Ok;
                    break;
            }
        }
        void AssistantResult(string label,bool ok)
        {
            assistantFailure|=!ok;
            WriteResult((ok?"PASS ":"FAIL ")+label);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BaliDesktop
{
    [DataContract] sealed class ProactiveOptions
    {
        [DataMember] public bool Enable, Foreground, Greeting, Daily, Routines, Reminders, Resume, Memory;
        [DataMember] public Dictionary<string,int> Preference=new Dictionary<string,int>();
        [DataMember] public Dictionary<string,string> Shown=new Dictionary<string,string>();
        [DataMember] public Dictionary<string,int> Uses=new Dictionary<string,int>();
        [OnDeserializing] void Defaults(StreamingContext _) {
            Preference=new Dictionary<string,int>();Shown=new Dictionary<string,string>();Uses=new Dictionary<string,int>();
        }
    }
    sealed class ProactiveCandidate
    {
        public string Id, Message, Reason;
        public double Weight;
    }
    static class ProactivePolicy
    {
        public static bool CanNotify(Mode mode,bool busy,bool visible,double idle,double autonomy)
        {return (mode==Mode.Libre || mode==Mode.Interactivo) && !busy && visible && idle<90 && autonomy>=.20;}
        public static ProactiveCandidate Select(IEnumerable<ProactiveCandidate> choices,ProactiveOptions options,DateTime now,bool learning)
        {
            var key=now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            return choices.Where(c=>!options.Shown.ContainsKey(c.Id) || options.Shown[c.Id]!=key)
                .Select(c=>new {Item=c,Score=c.Weight+(learning && options.Preference.ContainsKey(c.Id)?options.Preference[c.Id]*.13:0)})
                .Where(x=>x.Score>=.30).OrderByDescending(x=>x.Score).Select(x=>x.Item).FirstOrDefault();
        }
        public static int Feedback(int previous,bool yes){return Math.Max(-4,Math.Min(4,previous+(yes?1:-1)));}
    }
    sealed partial class BaliApp
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        double nextProactive=18, lastProactive=-1000, lastIdle, activeBlock;
        double freeMemoryFraction=1;
        long freeSystemDisk=long.MaxValue;
        int powerPercent=-1;
        string observedApp, resumedApp;
        ProactiveCandidate proposed;
        string lastProactiveReason="Sin propuestas.";
        DateTime proposedUntil;
        bool environmentProbeRunning;
        bool ProactiveBusy {get{return floatingActive || busyConversation || speaking || speechPending || capture!=null || transcription!=null || desktopWork!=null;}}
        string ForegroundApp()
        {
            try
            {
                uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);
                if(pid==0)return null;
                using(var process=Process.GetProcessById((int)pid))
                {
                    string path=process.MainModule.FileName;
                    return VisibleDesktopApps().Where(x=>!x.Packaged && CanUseDesktopApp(x))
                        .Where(x=>String.Equals(x.Path,path,StringComparison.OrdinalIgnoreCase)).Select(x=>x.Name).FirstOrDefault();
                }
            }
            catch(Exception){return null;}
        }
        void ProactiveTick(double now)
        {
            var options=desktopConfig.Proactive;
            if(!options.Enable || now<nextProactive)return;
            double step=Math.Min(18,Math.Max(0,now-nextProactive+18));
            nextProactive=now+18;
            double idle=Desktop.Idle();
            bool returned=lastIdle>=300 && idle<30;
            if(idle<120)activeBlock=Math.Min(7200,activeBlock+step); else activeBlock=0;
            if(options.Foreground && idle<120)
            {
                string current=ForegroundApp();
                if(returned)resumedApp=observedApp;
                if(current!=null)observedApp=current;
            }
            else if(!options.Foreground)observedApp=resumedApp=null;
            lastIdle=idle;
            freeMemoryFraction=DesktopMetrics.AvailableRamFraction();
            var charge=System.Windows.Forms.SystemInformation.PowerStatus;
            powerPercent=charge.BatteryLifePercent>=0 && charge.BatteryLifePercent<=1?(int)(charge.BatteryLifePercent*100):-1;
            if(desktopConfig.Suggestions && !environmentProbeRunning && now%180<18)
            {
                environmentProbeRunning=true;
                Task.Run(()=>{try{return new DriveInfo(Path.GetPathRoot(DesktopMaintenance.UserRoot())).AvailableFreeSpace;}catch(Exception){return long.MaxValue;}})
                    .ContinueWith(t=>Dispatcher.BeginInvoke(new Action(()=>{freeSystemDisk=t.Result;environmentProbeRunning=false;})));
            }
            if(now-lastProactive<900 || !ProactivePolicy.CanNotify(settings.Mode,ProactiveBusy,pet.IsVisible,idle,settings.Autonomy))return;
            var local=DateTime.Now;
            var pending=personal.Data.Items.Where(x=>x.State=="Activo" && x.Date.HasValue).ToArray();
            int today=pending.Count(x=>x.Date.Value.Date==local.Date);
            var soon=pending.FirstOrDefault(x=>x.Kind=="Recordatorio" && x.Date>local && x.Date<=local.AddMinutes(20));
            var frequent=desktopConfig.Routines.Where(r=>options.Uses.ContainsKey(r.Name) && options.Uses[r.Name]>=2)
                .OrderByDescending(r=>options.Uses[r.Name]).FirstOrDefault();
            var observed=life.Profile.Hours[local.Hour];
            var choices=new List<ProactiveCandidate>();
            if(options.Greeting && (returned || activeBlock>=18))
                choices.Add(new ProactiveCandidate{Id="saludo",Message=local.Hour<12?"Buenos días.":"Hola, aquí estoy.",
                    Reason=returned?"Regresaste tras al menos cinco minutos de inactividad.":"Detecté actividad en esta sesión.",Weight=.48+life.Profile.InteractionNeed*.12});
            if(options.Daily && activeBlock>=36)
                choices.Add(new ProactiveCandidate{Id="resumen",Message="Tienes "+today+" elementos activos programados para hoy.",
                    Reason="Agenda local de hoy y actividad reciente.",Weight=today>0?.66:.38});
            if(options.Reminders && soon!=null)
                choices.Add(new ProactiveCandidate{Id="recordatorio",Message="Tienes un recordatorio próximo en Agenda.",
                    Reason="Hay un recordatorio activo para los próximos veinte minutos.",Weight=.85});
            if(options.Routines && frequent!=null && activeBlock>=60 && observed.Active>0)
                choices.Add(new ProactiveCandidate{Id="rutina",Message="Puedes pedir la rutina "+frequent.Name+".",
                    Reason="Rutina ejecutada al menos dos veces en el equipo y actividad observada a esta hora.",
                    Weight=.61+life.Profile.Activation*.08});
            if(options.Resume && options.Foreground && returned && resumedApp!=null)
                choices.Add(new ProactiveCandidate{Id="reanudar",Message="Puedes retomar "+resumedApp+" cuando quieras.",
                    Reason="Regreso tras una pausa y aplicación previamente observada con tu permiso.",Weight=.64});
            if(options.Memory && freeMemoryFraction<.10)
                choices.Add(new ProactiveCandidate{Id="memoria",Message="Queda menos del diez por ciento de RAM disponible.",
                    Reason="Medición local de memoria física.",Weight=.90});
            if(desktopConfig.Suggestions && freeSystemDisk<5L*1073741824)
                choices.Add(new ProactiveCandidate{Id="disco",Message="Quedan menos de cinco gigabytes libres. Puedes pedir un análisis de temporales.",
                    Reason="Espacio libre medido en la unidad del usuario.",Weight=.94});
            var next=ProactivePolicy.Select(choices,options,local,settings.Learning);
            if(next==null)return;
            lastProactive=now;proposed=next;proposedUntil=local.AddMinutes(10);
            options.Shown[next.Id]=local.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            lastProactiveReason=next.Id+" · "+next.Reason;
            try{SaveDesktop();}catch(IOException){store.Log("No se pudo guardar el límite diario de iniciativas.");}
            life.Cue(Expression.Curiosidad,1.2,2);
            tray.ShowBalloonTip(3000,"Bali",next.Message,System.Windows.Forms.ToolTipIcon.Info);
        }
        bool TryProactiveReply(string text,bool floating)
        {
            var value=SpokenQuestion.Normalize(text);
            if(value!="acepto sugerencia" && value!="rechazo sugerencia")return false;
            DesktopReply(RecordProactiveFeedback(value=="acepto sugerencia"),floating);
            return true;
        }
        string RecordProactiveFeedback(bool yes)
        {
            if(proposed==null || DateTime.Now>proposedUntil)return "No hay una sugerencia reciente para valorar.";
            var options=desktopConfig.Proactive;
            if(settings.Learning)
            {
                int previous;options.Preference.TryGetValue(proposed.Id,out previous);
                options.Preference[proposed.Id]=ProactivePolicy.Feedback(previous,yes);
                SaveDesktop();
            }
            lastProactiveReason+=" · "+(yes?"Aceptada":"Rechazada");
            proposed=null;
            return yes?"Gracias. Tendré en cuenta tu preferencia.":"Entendido. Reduciré ese tipo de sugerencia.";
        }
        void AddProactiveLab(Panel page)
        {
            var options=desktopConfig.Proactive;
            Heading(page,"Iniciativas locales");
            Toggle(page,"Permitir sugerencias proactivas",options.Enable,n=>{options.Enable=n;SaveDesktop();});
            Toggle(page,"Observar en RAM la aplicación autorizada en primer plano",options.Foreground,
                n=>{options.Foreground=n;if(!n)observedApp=resumedApp=null;SaveDesktop();});
            Toggle(page,"Saludos contextuales",options.Greeting,n=>{options.Greeting=n;SaveDesktop();});
            Toggle(page,"Resumen diario",options.Daily,n=>{options.Daily=n;SaveDesktop();});
            Toggle(page,"Sugerencias de rutinas",options.Routines,n=>{options.Routines=n;SaveDesktop();});
            Toggle(page,"Recordatorios contextuales",options.Reminders,n=>{options.Reminders=n;SaveDesktop();});
            Toggle(page,"Sugerir retomar la aplicación anterior",options.Resume,n=>{options.Resume=n;SaveDesktop();});
            Toggle(page,"Avisos de presión de memoria",options.Memory,n=>{options.Memory=n;SaveDesktop();});
            Toggle(page,"Avisos de poco espacio",desktopConfig.Suggestions,n=>{desktopConfig.Suggestions=n;SaveDesktop();});
            var diagnostic=Label("");page.Children.Add(diagnostic);
            var updates=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
            Action refresh=()=>diagnostic.Text=String.Format("Inactividad {0:0} s · actividad de sesión {1:0} min · batería {2}\nPrimer plano {3}\nTema de voz {4}\n{5}\nPreferencias {6}\nCPU {7:0.00}% · RAM {8} MiB",
                Desktop.Idle(),activeBlock/60,powerPercent<0?"sin muestra":powerPercent+" %",options.Foreground?observedApp??"sin aplicación reconocida":"desactivado",
                voiceSession.Topic(clock.Elapsed.TotalSeconds,settings.Intelligence.VoiceTopicSeconds)??"sin tema",lastProactiveReason,
                String.Join(", ",options.Preference.Select(x=>x.Key+" "+x.Value)),currentCpu,currentRam);
            updates.Tick+=(s,e)=>{if(tabs.SelectedIndex==LifeTab)refresh();};
            updates.Start();refresh();
            panel.Closed+=(s,e)=>updates.Stop();
            ActionButton(page,"Aceptar sugerencia reciente",()=>SetDialogueStatus(diagnostic,RecordProactiveFeedback(true)));
            ActionButton(page,"Rechazar sugerencia reciente",()=>SetDialogueStatus(diagnostic,RecordProactiveFeedback(false)));
        }
    }
}

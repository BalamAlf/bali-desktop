using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace BaliDesktop
{
    sealed partial class BaliToolRouter
    {
        public void RegisterVision()
        {
            Add("vision-local","Observar la pantalla","alcance autorizado",ToolRisk.Consulta,_=>No("Usa el módulo Visión."));
            Add("vision-compartir","Interpretar texto visual con DeepSeek","fragmento aprobado",ToolRisk.Confirmación,_=>No("Usa el módulo Visión."));
        }
    }
    sealed partial class BaliApp
    {
        VisualSettings visualSettings;
        readonly IVisualOcr visualOcr=new WindowsVisualOcr();
        readonly VisualShareGrant visualGrant=new VisualShareGrant();
        VisualContext visualContext;
        VisualWindow lastVisualWindow;
        DateTime lastVisualAt;
        CancellationTokenSource visualWork;
        string visualPending;
        DateTime visualPendingUntil;
        string visualShared="Nada compartido.";
        string visualOnlineResult="";
        bool visualTurnRunning;
        Action refreshVision;
        void InitVision()
        {
            var path=Path.Combine(store.Folder,"vision.json");
            try{visualSettings=(File.Exists(path)?LocalJson.Read<VisualSettings>(path):null)??new VisualSettings();}
            catch(Exception){visualSettings=new VisualSettings();}
            if(visualSettings.Exclusions==null)visualSettings.Exclusions=new System.Collections.Generic.List<string>();
            toolsRouter.RegisterVision();
        }
        void SaveVision(){LocalJson.Write(Path.Combine(store.Folder,"vision.json"),visualSettings);}
        void RememberVisualForeground()
        {
            if(visualSettings==null || !visualSettings.Enabled || !visualSettings.Identify || !VisualWindows.Interactive())return;
            var active=VisualWindows.Inspect(VisualWindows.GetForegroundWindow());
            if(active==null)return;
            lastVisualWindow=VisualWindows.Excluded(active,visualSettings.Exclusions)?null:active;
            lastVisualAt=DateTime.UtcNow;
        }
        VisualWindow VisualTarget()
        {
            if(!VisualWindows.Interactive())return null;
            var active=VisualWindows.Inspect(VisualWindows.GetForegroundWindow());
            if(active!=null)return VisualWindows.Excluded(active,visualSettings.Exclusions)?null:active;
            if(lastVisualWindow==null || DateTime.UtcNow-lastVisualAt>TimeSpan.FromSeconds(45))return null;
            var previous=VisualWindows.Inspect(lastVisualWindow.Handle);
            return lastVisualWindow.Same(previous) && !VisualWindows.Excluded(previous,visualSettings.Exclusions)?previous:null;
        }
        bool VisualAwaitsReply()
        {return visualPending!=null && DateTime.UtcNow<visualPendingUntil && visualContext!=null && visualContext.Valid(DateTime.UtcNow);}
        void ClearVision()
        {
            if(visualWork!=null)visualWork.Cancel();
            if(visualTurnRunning && turn!=null)turn.Cancel();
            visualContext=null; visualPending=null; visualGrant.Clear(); lastVisualWindow=null;
            visualShared="Nada compartido en el contexto actual.";
            visualOnlineResult="";
            if(refreshVision!=null)refreshVision();
        }
        void ExpireVision()
        {
            if(visualContext!=null && !visualContext.Valid(DateTime.UtcNow))ClearVision();
            if(lastVisualWindow!=null && DateTime.UtcNow-lastVisualAt>TimeSpan.FromSeconds(45))lastVisualWindow=null;
        }
        bool HasVisualContext(bool fromPanel=false)
        {
            if(!visualSettings.Enabled || !visualSettings.Identify || visualContext==null || !visualContext.Valid(DateTime.UtcNow) ||
                visualContext.Protected || visualContext.Window==null || !VisualWindows.Interactive())
                return false;
            var current=VisualTarget();
            var original=VisualWindows.Inspect(visualContext.Window.Handle);
            return (visualContext.Window.Same(current) || fromPanel && VisualWindows.OwnForeground()) && visualContext.Window.Same(original) &&
                String.Equals(visualContext.Window.Title,original.Title,StringComparison.Ordinal) &&
                !VisualWindows.Excluded(original,visualSettings.Exclusions);
        }
        void VisualReply(string message,bool floating)
        {
            if(floating)SpeakFloatingNotice(message); else SetDialogueStatus(null,message);
        }
        void ReadVisualFromTray()
        {
            if(visualWork!=null || busyConversation || desktopWork!=null || capture!=null || transcription!=null || speaking || speechPending)return;
            RememberVisualForeground();
            if(visualSettings.Enabled && visualSettings.Identify && VisualTarget()==null && VisualWindows.Interactive() &&
                VisualWindows.Inspect(VisualWindows.GetForegroundWindow())==null)
            {
                var candidate=VisualWindows.List().FirstOrDefault();
                if(candidate!=null && !VisualWindows.Excluded(candidate,visualSettings.Exclusions))
                {lastVisualWindow=candidate;lastVisualAt=DateTime.UtcNow;}
            }
            floatingActive=true;
            TryVisual("lee la ventana",true);
        }
        bool TryVisual(string phrase,bool floating)
        {
            var text=VisualSpeech.Normalize(phrase);
            var intent=VisualSpeech.Parse(phrase);
            var reference=Regex.Match(text,@"^(?:qu[eé] dice|describe|d[oó]nde est[aá])\s+((?:el |la )?(?:bot[oó]n|pesta[ñn]a|opci[oó]n|cuadro|elemento)\s+.+)$");
            bool list=intent==VisualIntent.List, resources=intent==VisualIntent.Resources, again=intent==VisualIntent.Refresh, follow=intent==VisualIntent.Follow;
            if(floating && visualPending!=null)
            {
                var response=SpokenQuestion.Answer(text);
                if(response!=SpokenReply.Other)
                {
                    var previous=visualPending;
                    if(response!=SpokenReply.Yes)
                    {visualPending=null;VisualReply("El texto se queda en Bali.",true);return true;}
                    if(DateTime.UtcNow>=visualPendingUntil || !HasVisualContext())
                    {visualPending=null;VisualReply("Cambió la ventana o venció el permiso. Vuelve a pedir el análisis.",true);return true;}
                    if(lastSpeech==null || lastSpeech.NeedsReview || lastSpeech.Confidence.GetValueOrDefault()<.70)
                    {VisualReply("No pude confirmar el envío. Puedes revisar el fragmento en Visión.",true);return true;}
                    visualPending=null;
                    visualGrant.Allow(visualContext,DateTime.UtcNow);
                    SendVisual(previous,true,false);
                    return true;
                }
                visualPending=null;
            }
            if(intent==VisualIntent.None && !reference.Success)return false;
            if(floating && (spokenQuestion.HasSession || toolsRouter.AwaitsApproval("voz",DateTime.Now) || DesktopAwaitsAnswer()))
            {
                spokenQuestion.Clear();
                toolsRouter.Cancel("voz");
                ClearDesktopPending("voz");
            }
            if(!visualSettings.Enabled || !visualSettings.Identify || !toolsRouter.Allowed("vision-local"))
            {VisualReply("Activa Visión e identificación de ventanas en el panel Visión.",floating);return true;}
            if(reference.Success)
            {
                if(!HasVisualContext()){VisualReply("Primero lee la ventana para identificar sus controles.",floating);return true;}
                string query=reference.Groups[1].Value;
                string kind=query.Contains("botón") || query.Contains("boton")?"Button":query.Contains("pestaña")?"TabItem":null;
                var matches=VisualResolver.Resolve(visualContext.Elements,query,kind);
                VisualReply(matches.Length==1?"Identifiqué "+matches[0].Kind+" "+matches[0].Name+
                    " mediante "+matches[0].Source+".":matches.Length==0?"No veo un elemento con esa referencia.":"Encontré varios. Di el nombre exacto.",floating);
                return true;
            }
            if(list)
            {
                var windows=VisualWindows.List().Where(x=>!VisualWindows.Excluded(x,visualSettings.Exclusions)).ToArray();
                var apps=windows.Select(x=>x.App).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
                toolsRouter.Audit("vision-local",floating?"voz":"texto","autorizada","ventanas enumeradas");
                VisualReply(apps.Length==0?"No encontré ventanas disponibles.":"Hay "+windows.Length+" ventanas disponibles. Veo "+String.Join(", ",apps)+".",floating);
                return true;
            }
            if(follow && HasVisualContext())
            {
                OfferVisualInterpretation(text,floating,false);
                return true;
            }
            if(follow && text.Contains("solucionarlo"))
            {VisualReply("No tengo un contexto visual vigente. Pídeme revisar la ventana.",floating);return true;}
            var target=VisualTarget();
            if(target==null){VisualReply("No hay una ventana disponible para analizar. Vuelve a la aplicación y prueba otra vez.",floating);return true;}
            BeginVisual(target,"ventana",Rectangle.Empty,false,false,text,floating,again,null,resources);
            return true;
        }
        async void BeginVisual(VisualWindow target,string scope,Rectangle area,bool captureImage,bool preview,string request,bool floating,bool compare,Action<string> report,bool resources=false)
        {
            if(visualWork!=null || busyConversation || capture!=null || transcription!=null || desktopWork!=null || speaking || speechPending)return;
            if(!toolsRouter.Allowed("vision-local")){if(report!=null)report("La herramienta está desactivada.");return;}
            var previous=visualContext;
            visualPending=null;visualGrant.Clear();visualContext=null;visualShared="Nada compartido en esta observación.";visualOnlineResult="";
            var owned=new CancellationTokenSource(TimeSpan.FromSeconds(12)); visualWork=owned;
            var snapshot=LocalJson.Clone(visualSettings);
            var watch=Stopwatch.StartNew();
            BeginVisualIndicator();
            life.Cue(Expression.Procesamiento,10,9);
            string summary;
            try
            {
                var analysis=new VisualAnalysis(visualOcr);
                var observation=await Task.Run(()=>analysis.Observe(snapshot,target,scope,area,captureImage,preview,owned.Token));
                owned.Token.ThrowIfCancellationRequested();
                if(!visualSettings.Enabled)return;
                visualContext=observation;
                summary=VisualText.Explain(observation,request);
                if(resources && !observation.Protected)
                    summary+=" "+await Task.Run(()=>DesktopMetrics.Read("lento",owned.Token));
                if(compare && previous!=null && previous.Valid(DateTime.UtcNow) && previous.Window!=null && previous.Window.Same(observation.Window) &&
                    !previous.Protected && !observation.Protected && !String.IsNullOrWhiteSpace(previous.Text) && !String.IsNullOrWhiteSpace(observation.Text))
                    summary=(previous.Text==observation.Text?"El texto extraído coincide con el anterior. ":"El texto extraído cambió. ")+summary;
                toolsRouter.Audit("vision-local",floating?"voz":"panel","autorizada","observación local "+watch.ElapsedMilliseconds+" ms");
            }
            catch(OperationCanceledException){summary="Análisis visual cancelado o agotado.";}
            catch(InvalidOperationException ex){summary=ex.Message;}
            catch(Exception ex){store.Log("Visión · "+ex.GetType().Name);summary="Windows no pudo analizar el área.";}
            finally{visualWork=null;owned.Dispose();EndVisualIndicator();life.ReleaseVisualCue();}
            if(refreshVision!=null)refreshVision();
            if(report!=null)report(summary);
            else if(floating && floatingActive && visualContext!=null && !visualContext.Protected &&
                !compare && Regex.IsMatch(request,@"(?i)explica|solucion|significa|opci[oó]n|sql|ay[uú]dame") &&
                visualSettings.Share && toolsRouter.Allowed("vision-compartir") && settings.Intelligence.Enabled && settings.Intelligence.Verified && vault.Exists)
                OfferVisualInterpretation(request,true,false);
            else VisualReply(summary,floating);
        }
        void OfferVisualInterpretation(string request,bool floating,bool fromPanel)
        {
            if(!HasVisualContext()){VisualReply("La ventana cambió o el análisis venció. Vuelve a revisarla.",floating);return;}
            var excerpt=VisualText.ForProvider(visualContext.Text);
            if(!visualSettings.Share || !toolsRouter.Allowed("vision-compartir") || !settings.Intelligence.Enabled ||
                !settings.Intelligence.Verified || !vault.Exists || String.IsNullOrWhiteSpace(excerpt))
            {VisualReply(VisualText.Explain(visualContext,request),floating);return;}
            if(visualGrant.Allows(visualContext,DateTime.UtcNow))
            {SendVisual(request,floating,fromPanel);return;}
            if(VisualText.MayContainCode(excerpt) || visualContext.Method.Contains("OCR") || excerpt.Length>440)
            {VisualReply("Puedo leerlo aquí. Para enviar el fragmento, revisa y confirma el texto exacto en Visión. "+VisualText.Explain(visualContext,request),floating);return;}
            visualPending=request;visualPendingUntil=DateTime.UtcNow.AddSeconds(30);
            VisualReply("Leí: "+SpokenQuestion.ForSpeech(excerpt)+". ¿Envío solo este texto a DeepSeek? Di sí o no.",floating);
        }
        void SendVisual(string request,bool floating,bool panelRequest)
        {
            if(!HasVisualContext(panelRequest) || !visualSettings.Share || !toolsRouter.Allowed("vision-compartir") ||
                !visualGrant.Allows(visualContext,DateTime.UtcNow))return;
            var excerpt=VisualText.ForProvider(visualContext.Text);
            if(String.IsNullOrWhiteSpace(excerpt))return;
            ChatContext context;
            try{context=VisualText.ForQuestion(request,excerpt,settings.Intelligence.Snapshot());}
            catch(InvalidOperationException ex){VisualReply(ex.Message,floating);return;}
            visualShared=excerpt;
            visualOnlineResult="Solicitud autorizada. Consultando…";
            toolsRouter.Audit("vision-compartir",floating?"voz":"panel","confirmada","fragmento textual "+excerpt.Length+" caracteres");
            if(refreshVision!=null)refreshVision();
            RunOnline(request,false,null,floating,null,context);
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BaliDesktop
{
    [DataContract] sealed class ComputerSettings
    {
        [DataMember] public bool Windows, Controls, Dictation;
    }
    sealed partial class BaliToolRouter
    {
        public void RegisterComputer()
        {
            Add("control-ventana","Administrar ventana","operación tipada y ventana",ToolRisk.Acción,_=>No("Usa Control de escritorio."));
            Add("control-semantico","Interactuar con un control","elemento UI Automation",ToolRisk.Acción,_=>No("Usa Control de escritorio."));
            Add("dictado","Insertar dictado confirmado","campo y borrador vigentes",ToolRisk.Confirmación,_=>No("Usa Control de escritorio."));
        }
    }
    sealed partial class BaliApp
    {
        const int ComputerTab=AgendaTab+7;
        ComputerSettings computerSettings;
        CancellationTokenSource computerWork;
        VisualWindow computerWindow,computerPrevious;
        DateTime computerObserved;
        DateTime computerUntil;
        Func<CancellationToken,ComputerResult> computerPending;
        string computerPendingTool,computerSummary="Sin acciones.";
        string lastComputerKey;
        DateTime lastComputerAt;
        DictationDraft dictation;
        DictationTarget dictationTarget;
        bool dictationActive;
        double dictationUntil;
        Action refreshComputer;
        bool ComputerEnabled
        {get{return computerSettings!=null && (computerSettings.Windows || computerSettings.Controls || computerSettings.Dictation);}}
        bool ComputerAwaiting
        {get{return computerPending!=null && DateTime.UtcNow<computerUntil;}}
        bool ComputerContinuation
        {get{return ComputerAwaiting || dictationActive && clock.Elapsed.TotalSeconds<dictationUntil;}}
        bool DictationFocusLost
        {get{return dictationActive && (dictationTarget==null || VisualWindows.GetForegroundWindow()!=dictationTarget.Window.Handle ||
            clock.Elapsed.TotalSeconds>=dictationUntil);}}
        void InitComputer()
        {
            var file=Path.Combine(store.Folder,"control-interaccion.json");
            try{computerSettings=(File.Exists(file)?LocalJson.Read<ComputerSettings>(file):null)??new ComputerSettings();}
            catch(Exception){computerSettings=new ComputerSettings();}
            toolsRouter.RegisterComputer();
        }
        void RememberComputerWindow()
        {
            if(!ComputerEnabled || !VisualWindows.Interactive())return;
            var candidate=VisualWindows.Inspect(VisualWindows.GetForegroundWindow());
            if(candidate==null)return;
            if(VisualWindows.Excluded(candidate,visualSettings.Exclusions) || ComputerActions.Restricted(candidate))
            {computerWindow=null;return;}
            if(computerWindow!=null && !computerWindow.Same(candidate))computerPrevious=computerWindow;
            computerWindow=candidate;computerObserved=DateTime.UtcNow;
        }
        VisualWindow ComputerTarget()
        {
            if(!VisualWindows.Interactive())return null;
            var live=VisualWindows.Inspect(VisualWindows.GetForegroundWindow());
            if(live!=null)return VisualWindows.Excluded(live,visualSettings.Exclusions) || ComputerActions.Restricted(live)?null:live;
            return DateTime.UtcNow-computerObserved<TimeSpan.FromSeconds(35)?computerWindow:null;
        }
        void StopComputer(bool discard)
        {
            if(computerWork!=null)computerWork.Cancel();
            computerPending=null;dictationActive=false;dictationTarget=null;
            if(discard)dictation=null;
            if(refreshComputer!=null)refreshComputer();
        }
        void ReportComputer(string message,bool spoken)
        {
            computerSummary=message;
            if(refreshComputer!=null)refreshComputer();
            if(spoken)SpeakFloatingNotice(message);
            else SetDialogueStatus(null,message);
        }
        void AskComputer(string tool,string prompt,Func<CancellationToken,ComputerResult> action)
        {
            computerPending=action;computerPendingTool=tool;computerUntil=DateTime.UtcNow.AddSeconds(35);
            ReportComputer(prompt+" Di sí o no.",true);
        }
        bool ComputerAllows(string tool)
        {
            return toolsRouter.Allowed(tool) && (tool=="dictado"?computerSettings.Dictation:
                tool=="control-ventana"?computerSettings.Windows:computerSettings.Controls);
        }
        async void RunComputer(string tool,string origin,string key,Func<CancellationToken,ComputerResult> action,bool confirmed=false)
        {
            bool speakingTurn=origin=="voz";
            if(!ComputerAllows(tool) || computerWork!=null || busyConversation || visualWork!=null || desktopWork!=null ||
                capture!=null || transcription!=null)
            {ReportComputer("La acción está bloqueada o Bali está ocupado.",speakingTurn);return;}
            if(lastComputerKey==key && DateTime.UtcNow-lastComputerAt<TimeSpan.FromSeconds(4))
            {ReportComputer("Ya procesé esa acción hace unos segundos.",speakingTurn);return;}
            lastComputerKey=key;lastComputerAt=DateTime.UtcNow;
            var owned=new CancellationTokenSource(TimeSpan.FromSeconds(6));computerWork=owned;
            var clock=Stopwatch.StartNew();
            life.Cue(Expression.Procesamiento,7,9);
            ComputerResult result;
            try{result=await Task.Run(()=>action(owned.Token),owned.Token);}
            catch(OperationCanceledException){result=new ComputerResult(ComputerStatus.Cancelled,"Acción cancelada.");}
            catch(Exception){result=new ComputerResult(ComputerStatus.Failed,"Windows no devolvió un resultado verificable. No repetí la acción.");}
            finally{computerWork=null;owned.Dispose();life.ReleaseVisualCue();}
            toolsRouter.Audit(tool,origin,confirmed?"confirmada":"autorizada",result.Status+" · "+clock.ElapsedMilliseconds+" ms");
            if(tool=="dictado" && result.Status==ComputerStatus.Verified){dictation=null;dictationTarget=null;}
            ReportComputer(result.Message,speakingTurn);
        }
        bool TryComputer(string text,bool fromVoice)
        {
            if(fromVoice && dictationActive){HandleDictation(text);return true;}
            if(fromVoice && ComputerAwaiting)
            {
                var yes=SpokenQuestion.DesktopAnswer(text);
                if(yes==SpokenReply.No || yes==SpokenReply.Cancel)
                {computerPending=null;ReportComputer("Acción cancelada.",true);return true;}
                if(yes==SpokenReply.Yes)
                {
                    if(lastSpeech==null || lastSpeech.NeedsReview || lastSpeech.Confidence.GetValueOrDefault()<.72)
                    {ReportComputer("No pude confirmar. Di sí, no o cancela.",true);return true;}
                    var pending=computerPending;var tool=computerPendingTool;computerPending=null;
                    RunComputer(tool,"voz",tool+"|confirmada",pending,true);return true;
                }
                computerPending=null;
            }
            var normalized=DesktopCatalog.Normalize(VisualSpeech.Normalize(text));
            if(normalized=="deten el control" || normalized=="cancela el control")
            {StopComputer(false);ReportComputer("Control detenido.",fromVoice);return true;}
            var request=ComputerRequest.Parse(text);
            if(request.Verb==ComputerVerb.None)return false;
            if(!ComputerEnabled)
            {ReportComputer("Activa el permiso correspondiente en Control de escritorio.",fromVoice);return true;}
            if(request.Verb==ComputerVerb.Dictate)
            {
                if(!fromVoice){ReportComputer("Inicia el dictado desde Bali flotante con Bloc de notas enfocado.",false);return true;}
                BeginDictation();return true;
            }
            bool semantic=request.Verb>=ComputerVerb.ScrollDown && request.Verb<=ComputerVerb.Invoke;
            string toolId=semantic?"control-semantico":"control-ventana";
            if(!ComputerAllows(toolId)){ReportComputer("Activa ese permiso en Control de escritorio.",fromVoice);return true;}
            if(request.Verb==ComputerVerb.Invoke)
            {ReportComputer("Ese botón podría confirmar una acción externa o irreversible. Pulsa el control manualmente.",fromVoice);return true;}
            var window=ComputerTarget();
            if(request.Verb==ComputerVerb.Previous)window=computerPrevious;
            else if(!semantic && !String.IsNullOrWhiteSpace(request.Target) &&
                !Regex.IsMatch(DesktopCatalog.Normalize(request.Target),@"^(?:(?:esta|la|actual) )?(?:ventana|aplicacion)(?: actual)?$|^esto$"))
            {
                string wanted=DesktopCatalog.Normalize(request.Target);
                var apps=VisibleDesktopApps().Where(x=>DesktopCatalog.Exact(x,request.Target)).ToArray();
                var found=VisualWindows.List(true).Where(x=>!VisualWindows.Excluded(x,visualSettings.Exclusions) && !ComputerActions.Restricted(x))
                    .Where(x=>DesktopCatalog.Normalize(x.App)==wanted || DesktopCatalog.Normalize(x.Title)==wanted ||
                        apps.Length==1 && apps[0].Path!=null && Path.GetFileNameWithoutExtension(apps[0].Path).Equals(x.App,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(found.Length!=1){ReportComputer(found.Length>1?"Hay varias ventanas con ese nombre. Enfoca la que quieres usar.":"No encontré una ventana exacta con ese nombre.",fromVoice);return true;}
                window=found[0];
            }
            if(window==null){ReportComputer("No encontré una ventana válida para esa acción.",fromVoice);return true;}
            VisualElement element=null;
            if(semantic && request.Verb!=ComputerVerb.ScrollUp && request.Verb!=ComputerVerb.ScrollDown)
            {
                if(!HasVisualContext()){ReportComputer("Primero lee la ventana para identificar el control.",fromVoice);return true;}
                var candidates=VisualResolver.Resolve(visualContext.Elements.Where(x=>x.Source=="UI Automation"),
                    request.Target,request.Verb==ComputerVerb.Select?"TabItem":null);
                if(candidates.Length!=1){ReportComputer(candidates.Length==0?"No encontré un control con esa referencia.":"Hay varias coincidencias. Di su nombre exacto.",fromVoice);return true;}
                element=candidates[0];
            }
            if(request.Verb==ComputerVerb.Activate || request.Verb==ComputerVerb.Previous)
            {
                var previous=ComputerTarget();
                if(previous!=null && !previous.Same(window))computerPrevious=previous;
            }
            var exclusions=visualSettings.Exclusions.ToArray();
            Func<CancellationToken,ComputerResult> run=token=>semantic?ComputerActions.Control(window,element,request.Verb,exclusions,token):
                ComputerActions.Window(window,request.Verb,exclusions,token);
            string key=request.Verb+"|"+window.Handle+"|"+(element==null?"":element.Runtime);
            bool clearLocal=lastSpeech!=null && !lastSpeech.Confidence.HasValue && lastSpeech.Rejected==0 &&
                ComputerRequest.ClearLowRisk(request,text);
            if(fromVoice && sttNeedsReview && !clearLocal)
                AskComputer(toolId,"¿Aplico "+(element!=null?"el cambio en "+element.Name+" dentro de "+window.App:
                    "el cambio de ventana en "+window.App)+"?",run);
            else RunComputer(toolId,fromVoice?"voz":"texto",key,run);
            return true;
        }
        async void BeginDictation()
        {
            if(!ComputerAllows("dictado") || computerWork!=null)
            {ReportComputer("Activa el permiso de dictado en Control de escritorio.",true);return;}
            var target=ComputerTarget();
            var ex=visualSettings.Exclusions.ToArray();
            var owned=new CancellationTokenSource(TimeSpan.FromSeconds(4));computerWork=owned;
            DictationTarget field=null;
            try { field=await Task.Run(()=>DictationWriter.Prepare(target,ex),owned.Token); owned.Token.ThrowIfCancellationRequested(); }
            catch(Exception){}
            finally{computerWork=null;owned.Dispose();}
            if(!floatingActive)return;
            if(field==null){ReportComputer("Abre Bloc de notas y deja enfocado un campo de texto editable.",true);return;}
            dictationTarget=field;dictationActive=true;dictation=new DictationDraft();dictationUntil=clock.Elapsed.TotalSeconds+120;
            ReportComputer("Modo dictado. Habla y, cuando acabes, di termina el dictado.",true);
        }
        void HandleDictation(string text)
        {
            if(clock.Elapsed.TotalSeconds>=dictationUntil || VisualWindows.GetForegroundWindow()!=dictationTarget.Window.Handle)
            {dictationActive=false;dictationTarget=null;ReportComputer("Se suspendió el dictado. El borrador quedó en Control de escritorio.",true);return;}
            string key=DesktopCatalog.Normalize(VisualSpeech.Normalize(text));
            if(key=="cancela el dictado" || key=="cancela dictado")
            {StopComputer(true);ReportComputer("Dictado descartado.",true);return;}
            if(key=="termina el dictado" || key=="finaliza el dictado")
            {
                var target=dictationTarget;var draft=dictation.Text;var ex=visualSettings.Exclusions.ToArray();
                dictationActive=false;dictationTarget=null;
                if(String.IsNullOrWhiteSpace(draft)){ReportComputer("El borrador está vacío.",true);return;}
                AskComputer("dictado","¿Inserto el borrador en Bloc de notas?",t=>DictationWriter.Insert(target,draft,ex,t));
                return;
            }
            if(key=="corrige la ultima palabra" || key=="selecciona la ultima palabra" || key=="selecciona el ultimo parrafo")
            {ReportComputer(dictation.SelectLast(key.EndsWith("parrafo"))?"Texto seleccionado en el borrador. Puedes dictar el reemplazo.":"No hay texto que seleccionar.",true);return;}
            if(key=="copia el texto seleccionado" || key=="copia el borrador")
            {
                var value=key=="copia el borrador"?dictation.Text:dictation.Selected;
                if(String.IsNullOrWhiteSpace(value)){ReportComputer("No hay texto seleccionado.",true);return;}
                try{Clipboard.SetText(value);ReportComputer("Copié el texto del borrador.",true);}
                catch(Exception){ReportComputer("El portapapeles no está disponible.",true);}
                return;
            }
            if(key=="pega en el borrador" || key=="pega el texto")
            {
                try{ReportComputer(dictation.Add(Clipboard.GetText())?"Texto pegado en el borrador.":"El texto está vacío o excede el límite.",true);}
                catch(Exception){ReportComputer("El portapapeles no está disponible.",true);}
                return;
            }
            var punctuation=Regex.Replace(key,@"^agrega (?:un |una )?","");
            if(punctuation=="punto y aparte" || punctuation=="punto" || punctuation=="coma" || punctuation=="dos puntos")
            {ReportComputer(dictation.Punctuation(punctuation)?"Puntuación agregada.":"Primero dicta texto.",true);return;}
            var replacement=Regex.Match(text,@"^(?:sustituye|reemplaza) (?:por|lo seleccionado por)\s+(.+)$",RegexOptions.IgnoreCase);
            if(replacement.Success && dictation.SelectionLength>0)
            {ReportComputer(dictation.ReplaceSelection(replacement.Groups[1].Value)?"Texto reemplazado en el borrador.":"El borrador excede el límite.",true);return;}
            if(!dictation.Add(text)){ReportComputer("El borrador alcanzó el límite.",true);return;}
            ReportComputer("Anotado.",true);
        }
        void ComputerPage(Panel page)
        {
            Heading(page,"Control de escritorio");
            page.Children.Add(Label("Acciones por ventana y controles de UI Automation. El dictado trabaja en un borrador hasta confirmar la inserción."));
            Action save=()=>{StopComputer(false);LocalJson.Write(Path.Combine(store.Folder,"control-interaccion.json"),computerSettings);};
            Toggle(page,"Permitir administrar ventanas",computerSettings.Windows,n=>{computerSettings.Windows=n;save();});
            Toggle(page,"Permitir pestañas, menús y desplazamiento",computerSettings.Controls,n=>{computerSettings.Controls=n;save();});
            Toggle(page,"Permitir dictado en Bloc de notas",computerSettings.Dictation,n=>{computerSettings.Dictation=n;save();});
            var state=Label("");page.Children.Add(state);
            var draft=Label("");page.Children.Add(draft);
            var audit=Label("");page.Children.Add(audit);
            refreshComputer=()=>
            {
                state.Text=computerSummary+"\nCPU "+currentCpu.ToString("0.00")+" % · RAM "+currentRam+" MiB"+
                    "\nÚltima voz: "+(lastSpeech==null?"sin muestra":lastSpeech.Provider+" · "+lastSpeech.DecodeSeconds.ToString("0.00")+" s");
                draft.Text=dictation==null?"Sin borrador.":dictation.Text;
                audit.Text=String.Join("\n",personal.Data.Audit.Where(x=>x.Contains(" · control-") || x.Contains(" · dictado · ")).Reverse().Take(8));
            };
            ActionButton(page,"Detener interacción",()=>{StopComputer(false);ReportComputer("Control detenido.",false);});
            ActionButton(page,"Copiar borrador",()=>{if(dictation!=null && dictation.Text.Length>0)Clipboard.SetText(dictation.Text);});
            ActionButton(page,"Descartar borrador",()=>{StopComputer(true);ReportComputer("Borrador descartado.",false);});
            ActionButton(page,"Actualizar diagnóstico",refreshComputer);
            panel.Closed+=(s,e)=>refreshComputer=null;
            refreshComputer();
        }
    }
}

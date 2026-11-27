using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BaliDesktop
{
    sealed partial class BaliToolRouter
    {
        public void RegisterDesktop()
        {
            Add("sistema","Consultar recursos del sistema","métrica",ToolRisk.Consulta,_=>No("Usa el módulo Sistema."));
            Add("catalogo","Consultar aplicaciones instaladas","nombre",ToolRisk.Consulta,_=>No("Usa el módulo Aplicaciones."));
            Add("abrir-catalogo","Abrir aplicación del catálogo","identificador autorizado",ToolRisk.Acción,_=>No("Usa el módulo Aplicaciones."));
            Add("analizar-temporales","Analizar temporales","ubicación local fija",ToolRisk.Consulta,_=>No("Usa el módulo Sistema."));
            Add("limpiar-temporales","Limpiar temporales","análisis vigente",ToolRisk.Confirmación,_=>No("Usa el módulo Sistema."));
            Add("cerrar-aplicacion","Solicitar cierre normal","ventana autorizada",ToolRisk.Confirmación,_=>No("Usa el módulo Sistema."));
            Add("rutina","Gestionar rutina local","nombre y pasos autorizados",ToolRisk.Confirmación,_=>No("Usa el módulo Sistema."));
        }
    }
    sealed partial class BaliApp
    {
        DesktopConfig desktopConfig;
        DesktopApp[] desktopApps=new DesktopApp[0];
        CancellationTokenSource desktopWork, catalogWork;
        double nextCatalog=3;
        bool catalogLoaded;
        event Action DesktopCatalogChanged;
        TempPlan desktopAnalysis;
        string desktopProfile;
        Func<CancellationToken,string> desktopPending;
        string desktopPendingTool, desktopPendingOrigin;
        string desktopPendingApp;
        DateTime desktopExpires;
        DesktopApp[] desktopChoices;
        string desktopChoiceOrigin, desktopChoiceAction;
        string desktopLastKey;
        DateTime desktopLastTime;
        void InitDesktop()
        {
            desktopProfile=Path.Combine(store.Folder,"control-escritorio.json");
            try{desktopConfig=(File.Exists(desktopProfile)?LocalJson.Read<DesktopConfig>(desktopProfile):null)??new DesktopConfig();}
            catch(Exception){desktopConfig=new DesktopConfig();store.Log("Control de escritorio: preferencias no disponibles.");}
            if(desktopConfig.Proactive==null)desktopConfig.Proactive=new ProactiveOptions{Enable=desktopConfig.Suggestions};
            toolsRouter.RegisterDesktop();
        }
        void SaveDesktop(){LocalJson.Write(desktopProfile,desktopConfig);}
        DesktopPermission Permission(DesktopApp app)
        {return desktopConfig.Permissions.FirstOrDefault(x=>String.Equals(x.Id,app.Id,StringComparison.OrdinalIgnoreCase));}
        void DesktopTick(double now)
        {
            if(now>=nextCatalog && catalogWork==null && desktopWork==null && !floatingActive && !speaking)
            {nextCatalog=now+1800; RefreshDesktopCatalog();}
            ProactiveTick(now);
        }
        async void RefreshDesktopCatalog(Action<string> result=null)
        {
            if(catalogWork!=null){if(result!=null)result("Ya estoy actualizando el catálogo.");return;}
            var cancel=new CancellationTokenSource();
            catalogWork=cancel;
            var roots=desktopConfig.Folders.ToArray();
            try
            {
                var observed=await Task.Run(()=>DesktopCatalog.Scan(roots,cancel.Token),cancel.Token);
                cancel.Token.ThrowIfCancellationRequested();
                desktopApps=observed.Where(x=>!x.Portable || desktopConfig.Folders.Contains(x.Root,StringComparer.OrdinalIgnoreCase)).ToArray();
                ApplyDesktopPermissions();
                catalogLoaded=true;
                if(DesktopCatalogChanged!=null)DesktopCatalogChanged();
                if(result!=null)result("Aplicaciones detectadas: "+desktopApps.Length+".");
            }
            catch(OperationCanceledException){}
            catch(Exception ex){store.Log("Catálogo: "+ex.GetType().Name);if(result!=null)result("No pude actualizar el catálogo.");}
            finally{catalogWork=null;cancel.Dispose();}
        }
        void ApplyDesktopPermissions()
        {
            foreach(var app in desktopApps)
            {
                var permission=Permission(app);
                if(permission==null)continue;
                if(!String.IsNullOrWhiteSpace(permission.Alias))app.Alias=permission.Alias;
                app.Project=permission.Project;
            }
            voice.DesktopVocabulary=desktopApps.Where(x=>
                    (!x.Portable || Permission(x)!=null) && (Permission(x)==null || !Permission(x).Excluded))
                .SelectMany(x=>new[]{x.Name,x.Alias}).Where(x=>!String.IsNullOrWhiteSpace(x) && x.Length<=80)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).Select(x=>"abre "+x).ToArray();
        }
        DesktopApp[] VisibleDesktopApps()
        {
            var packages=desktopApps.Where(x=>x.Packaged).ToArray();
            var aliases=desktopApps.Where(x=>!x.Packaged).Select(x=>new {Classic=x,Matches=packages.Where(p=>DesktopCatalog.IsPackagedAlias(p,x)).ToArray()})
                .Where(x=>x.Matches.Length==1).ToArray();
            var hidden=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var alias in aliases)
            {
                var package=alias.Matches[0];
                if(Permission(alias.Classic)!=null && !Permission(alias.Classic).Excluded && Permission(package)==null)
                    hidden.Add(package.Id);
                else
                {
                    hidden.Add(alias.Classic.Id);
                    if(String.IsNullOrWhiteSpace(package.Alias))package.Alias=Path.GetFileNameWithoutExtension(alias.Classic.Path);
                }
            }
            return desktopApps.Where(x=>!hidden.Contains(x.Id)).ToArray();
        }
        void DesktopReply(string text,bool floating)
        {
            lastSpoken=text;
            AddLine("assistant",text,true);RefreshTranscript();SetDialogueStatus(null,text);
            if((floating && floatingActive) || (!floating && settings.Voice))Play(text);
        }
        void ClearDesktopPending(string origin=null)
        {
            if(origin==null || origin==desktopPendingOrigin){desktopPending=null;desktopPendingApp=null;}
            if(origin==null || origin==desktopChoiceOrigin)desktopChoices=null;
        }
        bool DesktopAwaitsAnswer()
        {return (desktopPending!=null && desktopPendingOrigin=="voz" || desktopChoices!=null && desktopChoiceOrigin=="voz") && DateTime.UtcNow<desktopExpires;}
        bool DesktopRequiresReliableReply()
        {return desktopPending!=null && desktopPendingOrigin=="voz" && DesktopAwaitsAnswer();}
        static bool IsDesktopOpenCommand(string text)
        {return Regex.IsMatch(SpokenQuestion.Normalize(text),@"^(?:bali[,\s]+)?(?:abre|inicia)\s+\S");}
        DesktopApp ExactAuthorizedApp(string text)
        {
            var clean=SpokenQuestion.Normalize(TechnicalSpeech.Normalize(text,desktopConfig.Words));
            var match=Regex.Match(clean,@"^(?:bali[,\s]+)?(?:abre|inicia)\s+(?:(?:la|el)\s+)?(.+)$");
            if(!match.Success)return null;
            var found=DesktopCatalog.Match(VisibleDesktopApps().Where(x=>Permission(x)==null || !Permission(x).Excluded),match.Groups[1].Value);
            return found.Length==1 && DesktopCatalog.Exact(found[0],match.Groups[1].Value) && CanUseDesktopApp(found[0])?found[0]:null;
        }
        string AskDesktop(string tool,string prompt,string origin,Func<CancellationToken,string> action,string appName=null)
        {
            ClearDesktopPending();
            desktopPendingTool=tool;desktopPendingOrigin=origin;desktopPending=action;desktopExpires=DateTime.UtcNow.AddSeconds(35);
            desktopPendingApp=appName;
            toolsRouter.Audit(tool,origin,"pendiente","confirmación");
            return prompt+(appName!=null?" Di sí o no.":" Di sí o cancela.");
        }
        async void RunDesktop(string tool,string origin,Func<CancellationToken,string> action,bool confirmed=false,string key=null,Action<string> callback=null)
        {
            Action<string> reply=callback??(value=>DesktopReply(value,origin=="voz"));
            if(!toolsRouter.Allowed(tool)){reply("La herramienta está desactivada.");return;}
            if(desktopWork!=null){reply("Ya hay una operación de escritorio en curso.");return;}
            if(key!=null && desktopLastKey==key && DateTime.UtcNow-desktopLastTime<TimeSpan.FromSeconds(6))
            {reply("Esa operación ya se solicitó hace unos segundos.");return;}
            if(key!=null){desktopLastKey=key;desktopLastTime=DateTime.UtcNow;}
            var cancel=new CancellationTokenSource();desktopWork=cancel;
            life.Cue(Expression.Procesamiento,10,9);
            string message;
            try{message=await Task.Run(()=>action(cancel.Token),cancel.Token);}
            catch(OperationCanceledException){message="Operación cancelada.";}
            catch(Exception ex){store.Log("Control de escritorio · "+tool+" · "+ex.GetType().Name);message="Windows no pudo completar la operación.";}
            finally{desktopWork=null;life.ReleaseVisualCue();cancel.Dispose();}
            toolsRouter.Audit(tool,origin,confirmed?"confirmada":"autorizada",message.StartsWith("Windows no pudo",StringComparison.Ordinal)?"error":"finalizada");
            reply(message);
            if(origin=="voz" && floatingActive && !speaking && !speechPending)EndFloating();
        }
        bool TryDesktop(string input,bool floating,bool normalized=false)
        {
            string origin=floating?"voz":"texto";
            string text=Regex.Replace((input??"").Trim(' ','¿','?','¡','!','.'),@"^bali[,\s]+","",RegexOptions.IgnoreCase);
            if(!normalized)text=TechnicalSpeech.Normalize(text,desktopConfig.Words);
            if(TryProactiveReply(text,floating))return true;
            var decision=SpokenQuestion.DesktopAnswer(text);
            if(origin==desktopPendingOrigin && SpokenQuestion.NamesAppToAuthorize(text,desktopPendingApp))decision=SpokenReply.Yes;
            if(desktopPending!=null && origin==desktopPendingOrigin)
            {
                if(DateTime.UtcNow>=desktopExpires)
                {
                    ClearDesktopPending(origin);
                    if(decision!=SpokenReply.Other){DesktopReply("La confirmación venció.",floating);return true;}
                }
                else if(decision!=SpokenReply.Other)
                {
                    if(floating && decision!=SpokenReply.Cancel && decision!=SpokenReply.No &&
                        (lastSpeech==null || !lastSpeech.ConfirmedDesktopOpening && (lastSpeech.NeedsReview || lastSpeech.Confidence.Value<.72)))
                    {DesktopReply(DesktopApprovalRetry(),true);return true;}
                    var action=desktopPending;var tool=desktopPendingTool;ClearDesktopPending(origin);
                    if(decision==SpokenReply.No || decision==SpokenReply.Cancel)DesktopReply("Operación cancelada.",floating);
                    else RunDesktop(tool,origin,action,true,tool);
                    return true;
                }
                else
                {
                    DesktopReply(DesktopApprovalRetry(),floating);
                    return true;
                }
            }
            var lower=SpokenQuestion.Normalize(text);
            if(desktopChoices!=null && desktopChoiceOrigin==origin && DateTime.UtcNow<desktopExpires)
            {
                var list=desktopChoices;var action=desktopChoiceAction;
                ClearDesktopPending(origin);
                if(decision==SpokenReply.Cancel){DesktopReply("Operación cancelada.",floating);return true;}
                int index=Array.IndexOf(new[]{"primera","segunda","tercera","cuarta","quinta"},lower);
                int number;
                if(Int32.TryParse(lower,out number))index=number-1;
                var matches=index>=0 && index<list.Length?new[]{list[index]}:DesktopCatalog.Match(list,text);
                if(matches.Length==1){SelectDesktopApp(matches[0],action,origin,floating);return true;}
            }
            if(lower.Contains("temporales") && Regex.IsMatch(lower,@"^(?:analiza|analizar|limpia|limpiar|borra)\b"))
            {
                bool clean=!lower.StartsWith("analiz",StringComparison.Ordinal);
                string root=DesktopMaintenance.UserRoot();
                RunDesktop("analizar-temporales",origin,t=>
                {
                    var plan=DesktopMaintenance.Analyze(root,DateTime.UtcNow,t);
                    return Dispatcher.Invoke(()=>{
                        t.ThrowIfCancellationRequested();
                        desktopAnalysis=plan;
                        var description="Encontré "+plan.Files.Length+" candidatos en la carpeta temporal del usuario. Tamaño estimado: "+DesktopMetrics.Size(plan.Bytes)+".";
                        AddMaintenance(description);
                        return clean && plan.Files.Length>0 && toolsRouter.Allowed("limpiar-temporales")?
                            AskDesktop("limpiar-temporales",description+" ¿Quieres limpiarlos?",origin,ct=>CleanDesktopPlan(plan,ct)):description;
                    });
                });
                return true;
            }
            string metric=null;
            if(Regex.IsMatch(lower,@"\b(?:lenta|lento|lentitud)\b"))metric="lento";
            else if(Regex.IsMatch(lower,@"(?:consum|procesos).*(?:memoria|ram)"))metric="procesos-ram";
            else if(Regex.IsMatch(lower,@"^(?:cu[aá]nta|uso|memoria|ram|cu[aá]nto).*\b(?:ram|memoria)\b|^(?:ram|memoria)$"))metric="ram";
            else if(Regex.IsMatch(lower,@"(?:espacio|capacidad).*(?:disco|unidad)|^espacio disponible$"))metric="disco";
            else if(Regex.IsMatch(lower,@"\b(?:cpu|procesador)\b") && !lower.StartsWith("qué es",StringComparison.Ordinal))metric="cpu";
            else if(Regex.IsMatch(lower,@"tiempo.*(?:activo|actividad|encendido)|cu[aá]nto .*encendido"))metric="actividad";
            else if(lower.Contains("batería") || lower.Contains("bateria"))metric="batería";
            if(metric!=null){RunDesktop("sistema",origin,t=>DesktopMetrics.Read(metric,t));return true;}
            if(TryDesktopRoutine(text,origin,floating))return true;
            if(Regex.IsMatch(lower,@"qu[eé] aplicaciones (?:tengo )?(?:instaladas|abiertas)|lista (?:las )?aplicaciones"))
            {
                if(!toolsRouter.Allowed("catalogo")){DesktopReply("La herramienta está desactivada.",floating);return true;}
                var apps=VisibleDesktopApps().Where(x=>Permission(x)==null || !Permission(x).Excluded).ToArray();
                if(lower.Contains("abiertas"))
                    RunDesktop("catalogo",origin,t=>SummarizeApps(apps.Where(x=>DesktopCatalog.Running(x).Length>0).ToArray(),true));
                else DesktopReply(catalogLoaded?SummarizeApps(apps,false):"Estoy preparando el catálogo. Pregunta de nuevo en unos segundos.",floating);
                return true;
            }
            var appMatch=Regex.Match(text,@"^(abre|inicia|cierra|busca(?: una)? aplicaci[oó]n(?: llamada)?|muestra la carpeta de)\s+(?:(?:la|el)\s+)?(.+)$",RegexOptions.IgnoreCase);
            if(appMatch.Success)
            {
                string action=appMatch.Groups[1].Value.ToLowerInvariant(), name=appMatch.Groups[2].Value;
                if(conversation.Respond(text,settings.Memory).Intent!=Intent.Unknown)return false;
                if((action=="abre" || action=="inicia") && personal.Data.Apps.Any(x=>DesktopCatalog.Normalize(x.Name)==DesktopCatalog.Normalize(name) ||
                    (x.Alias??"").Split(',').Any(a=>DesktopCatalog.Normalize(a)==DesktopCatalog.Normalize(name))))return false;
                if(!toolsRouter.Allowed("catalogo")){DesktopReply("La herramienta está desactivada.",floating);return true;}
                var available=VisibleDesktopApps().Where(x=>Permission(x)==null || !Permission(x).Excluded);
                var found=DesktopCatalog.Match(available,name);
                if(found.Length==0 && DesktopCatalog.Normalize(name)=="mi editor de codigo")
                    found=available.Where(x=>Regex.IsMatch(x.Name,@"Visual Studio Code|Notepad\+\+|Sublime Text",RegexOptions.IgnoreCase)).Take(5).ToArray();
                if(found.Length==1)
                {
                    bool exact=DesktopCatalog.Exact(found[0],name);
                    SelectDesktopApp(found[0],action,origin,floating,!exact,exact && (action=="abre" || action=="inicia"));
                }
                else if(found.Length>1)
                {
                    ClearDesktopPending();desktopChoices=found.Take(5).ToArray();desktopChoiceOrigin=origin;desktopChoiceAction=action;
                    desktopExpires=DateTime.UtcNow.AddSeconds(30);
                    DesktopReply("Encontré varias. Di el nombre exacto o su número: "+String.Join(". ",desktopChoices.Select((x,i)=>(i+1)+" "+x.Name+" "+x.Origin))+".",floating);
                }
                else DesktopReply(catalogLoaded?"No encontré esa aplicación en el catálogo.":"El catálogo aún está actualizándose.",floating);
                return true;
            }
            return false;
        }
        string DesktopApprovalRetry()
        { return desktopPendingApp==null?"No entendí con claridad. Di sí o cancela.":"No entendí con claridad. Di sí o no."; }
        static string SummarizeApps(DesktopApp[] apps,bool running)
        {
            if(apps.Length==0)return running?"No vi ventanas abiertas de aplicaciones verificables.":"El catálogo está vacío.";
            return "Encontré "+apps.Length+" aplicaciones"+(running?" con ventana abierta": " en el catálogo")+
                ": "+String.Join(", ",apps.Take(8).Select(x=>x.Name))+(apps.Length>8?". Consulta Aplicaciones para ver el resto.":".");
        }
        void AddMaintenance(string message)
        {
            desktopConfig.Maintenance.Insert(0,DateTime.Now.ToString("g")+" · "+message);
            if(desktopConfig.Maintenance.Count>40)desktopConfig.Maintenance.RemoveAt(40);
            SaveDesktop();RefreshMaintenanceHistory();
        }
        string CleanDesktopPlan(TempPlan plan,CancellationToken token)
        {
            if(!String.Equals(plan.Root,DesktopMaintenance.UserRoot(),StringComparison.OrdinalIgnoreCase))return "La ubicación ya no está autorizada.";
            var result=DesktopMaintenance.Clean(plan,DateTime.UtcNow,token).ToString();
            Dispatcher.Invoke(()=>AddMaintenance(result));return result;
        }
        void SelectDesktopApp(DesktopApp app,string action,string origin,bool floating,bool approximate=false,bool exactCommand=false)
        {
            var permission=Permission(app);
            if(action.StartsWith("busca",StringComparison.Ordinal))
            {DesktopReply(app.Name+" · "+app.Origin+(permission==null?" · requiere autorización.":"."),floating);return;}
            if(action=="cierra")
            {
                string own;
                using(var current=Process.GetCurrentProcess())own=current.MainModule.FileName;
                if(!toolsRouter.Allowed("cerrar-aplicacion") || permission==null || permission.Excluded || !DesktopCatalog.CanClose(app.Path,own))
                {DesktopReply("Esa aplicación está protegida o no se puede cerrar de forma verificable.",floating);return;}
                var targets=DesktopCatalog.Running(app).Select(id=>GetWindowIdentity(id)).Where(x=>x!=null).ToArray();
                if(targets.Length==0){DesktopReply("No encontré una ventana verificable de "+app.Name+".",floating);return;}
                DesktopReply(AskDesktop("cerrar-aplicacion","¿Solicito el cierre normal de "+app.Name+"? Windows puede pedir guardar.",origin,
                    t=>DesktopCatalog.Fingerprint(app.Path)!=permission.Hash?"La aplicación cambió. Revisa su autorización.":CloseDesktopWindows(app,targets,t)),floating);
                return;
            }
            if(action.StartsWith("muestra",StringComparison.Ordinal))
            {
                RunDesktop("abrir-catalogo",origin,t=>permission==null || permission.Excluded || app.Path==null?
                    "Autoriza la aplicación antes de abrir su carpeta.":OpenDesktopFolder(Path.GetDirectoryName(app.Path)),key:app.Id+"|carpeta");
                return;
            }
            if(permission==null || permission.Excluded)
            {
                if(floating && DesktopCatalog.CanAuthorizeByVoice(app,permission))
                {
                    DesktopReply(AskDesktop("abrir-catalogo","¿Autorizas abrir "+app.Name+"?",origin,t=>AuthorizeInstalledApp(app,t),app.Name),true);
                    return;
                }
                DesktopReply("Autoriza "+app.Name+" en Aplicaciones antes de abrirla.",floating);return;
            }
            Func<CancellationToken,string> launch=t=>DesktopCatalog.Launch(app,permission);
            if(approximate || floating && sttNeedsReview && !DesktopCatalog.CanOpenByExactVoice(permission,lastSpeech,exactCommand))
                DesktopReply(AskDesktop("abrir-catalogo","¿Abro "+app.Name+"?",origin,launch,app.Name),floating);
            else RunDesktop("abrir-catalogo",origin,launch,key:app.Id);
        }
        string AuthorizeInstalledApp(DesktopApp app,CancellationToken token)
        {
            var hash=app.Packaged?null:DesktopCatalog.Fingerprint(app.Path);
            if(app.Packaged?!DesktopCatalog.ValidId(app.Id):hash==null)return "La aplicación ya no está disponible.";
            var permission=Dispatcher.Invoke(()=>
            {
                token.ThrowIfCancellationRequested();
                if(!DesktopCatalog.CanAuthorizeByVoice(app,Permission(app)) ||
                    !desktopApps.Any(x=>x.Id==app.Id && !x.Portable) || !toolsRouter.Allowed("abrir-catalogo"))return null;
                var value=new DesktopPermission{Id=app.Id,Name=app.Name,Hash=hash};
                desktopConfig.Permissions.Add(value);
                try {SaveDesktop();}
                catch {desktopConfig.Permissions.Remove(value);throw;}
                ApplyDesktopPermissions();
                return value;
            });
            token.ThrowIfCancellationRequested();
            return permission==null?"La autorización cambió. Vuelve a solicitar la apertura.":DesktopCatalog.Launch(app,permission);
        }
        static string OpenDesktopFolder(string path)
        {
            if(!Directory.Exists(path) || !DesktopPaths.Local(path))return "No se encontró una carpeta local válida.";
            using(var process=Process.Start(new ProcessStartInfo(path){UseShellExecute=true}))
                return "Windows recibió la solicitud de abrir la carpeta.";
        }
        internal sealed class WindowIdentity {public int Id;public DateTime Start;public IntPtr Window;}
        internal static WindowIdentity GetWindowIdentity(int id)
        {
            try{using(var p=Process.GetProcessById(id))return new WindowIdentity{Id=id,Start=p.StartTime,Window=p.MainWindowHandle};}
            catch(Exception){return null;}
        }
        internal static string CloseDesktopWindows(DesktopApp app,WindowIdentity[] targets,CancellationToken token)
        {
            int closed=0,pending=0,omitted=0;
            foreach(var target in targets)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using(var p=Process.GetProcessById(target.Id))
                    {
                        if(p.StartTime!=target.Start || target.Window==IntPtr.Zero || p.MainWindowHandle!=target.Window ||
                            !String.Equals(p.MainModule.FileName,app.Path,StringComparison.OrdinalIgnoreCase) || !p.Responding || !p.CloseMainWindow())
                        {omitted++;continue;}
                        if(p.WaitForExit(500))closed++; else pending++;
                    }
                }
                catch(Exception){omitted++;}
            }
            return "Cierres confirmados: "+closed+". Solicitudes pendientes, incluidos avisos de guardado: "+pending+". Ventanas omitidas: "+omitted+".";
        }
    }
}

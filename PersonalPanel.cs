using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        const int AgendaTab=10;
        PersonalStore personal;
        BaliToolRouter toolsRouter;
        Reading reading;
        CancellationTokenSource readingCancellation;
        TextBox toolPrompt;
        string stagedToolPrompt;
        double nextPersonalCheck, nextSuggestion=90;
        void InitPersonal()
        {
            personal=new PersonalStore(store.Folder);
            toolsRouter=new BaliToolRouter(personal,app=>
                (desktopConfig==null || !desktopConfig.Permissions.Any(p=>p.Excluded && String.Equals(p.Id,app.Path,StringComparison.OrdinalIgnoreCase))) &&
                DesktopPaths.Executable(app.Path) && BaliToolRouter.Launch(app));
            toolsRouter.DocumentQuery=query=>reading==null?"Selecciona un documento en Documentos.":reading.Find(query);
            if(!personal.Available || personal.Data.Initialized) return;
            try
            {
                // These two inbox Windows programs receive no command-line arguments.
                string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var defaults=new[]{
                    new PersonalApp {Name="Calculadora",Alias="calc",Path=Path.Combine(Environment.SystemDirectory,"calc.exe")},
                    new PersonalApp {Name="Explorador de Windows",Alias="explorador",Path=Path.Combine(windows,"explorer.exe")}
                };
                personal.Change(x=>{x.Initialized=true;x.Apps.AddRange(defaults.Where(a=>BaliToolRouter.SafeExecutable(a.Path)));});
            }
            catch(Exception e){store.Log("Asistente personal · "+e.GetType().Name);}
        }
        bool TryPersonal(string text,bool floating)
        {
            string result;
            toolsRouter.ReviewVoice=floating && sttNeedsReview;
            try { result=toolsRouter.Interpret(text,floating?"voz":"texto",DateTime.Now); }
            finally { toolsRouter.ReviewVoice=false; }
            speechIntent=toolsRouter.LastIntent; speechParameters=toolsRouter.LastParameters;
            if(result==null) return false;
            AddLine("user",text,true); AddLine("assistant",result,true);
            lastSpoken=result; RefreshTranscript(); SetDialogueStatus(null,result);
            if(floating || settings.Voice) Play(result);
            return true;
        }
        void PersonalTick(double now)
        {
            if(now<nextPersonalCheck) return;
            nextPersonalCheck=now+2;
            try
            {
                var local=DateTime.Now;
                var due=personal.TakeDue(local);
                if(due.Length==0)
                {
                    if(personal.Data.Suggest && now>=nextSuggestion && (settings.Mode==Mode.Libre || settings.Mode==Mode.Interactivo) &&
                        !floatingActive && capture==null && transcription==null && !busyConversation && !speechPending && !speaking &&
                        Desktop.Idle()<120 && personal.Data.Items.Any(x=>x.Kind=="Recordatorio" && x.State=="Activo" && x.Date>local && x.Date<=local.AddMinutes(15)))
                    {
                        nextSuggestion=now+1800;
                        life.Cue(Expression.Curiosidad,1,1);
                        tray.ShowBalloonTip(2500,"Bali","Tienes un recordatorio próximo. Puedes consultar la Agenda.",System.Windows.Forms.ToolTipIcon.None);
                    }
                    return;
                }
                string title=due.Length==1?"Recordatorio de Bali":due.Length+" recordatorios de Bali";
                string message=due[0].Text;
                tray.ShowBalloonTip(4500,title,message.Substring(0,Math.Min(180,message.Length)),System.Windows.Forms.ToolTipIcon.Info);
                bool audible=settings.Mode==Mode.Concentrado?personal.Data.SpeakFocus:settings.Mode==Mode.Descanso?personal.Data.SpeakRest:true;
                if(audible && !floatingActive && capture==null && transcription==null && !busyConversation && !speechPending && !speaking)
                {
                    life.Cue(Expression.Escucha,1.5,4);
                    Play(due.Length==1?"Recordatorio: "+message:"Tienes "+due.Length+" recordatorios. El primero: "+message);
                }
            }
            catch(Exception e){nextPersonalCheck=now+30;store.Log("Recordatorios · "+e.GetType().Name);}
        }
        void StageToolPrompt(string recognized)
        {
            if(MemoryBook.LooksSensitive(recognized)) return;
            if(toolPrompt!=null) toolPrompt.Text=recognized;
            else stagedToolPrompt=recognized;
        }
        void AddPersonalPages()
        {
            var pages=new[]{Page("Agenda"),Page("Aplicaciones"),Page("Documentos"),Page("Conocimiento"),Page("Herramientas"),Page("Sistema")};
            var built=new bool[pages.Length];
            tabs.SelectionChanged+=(sender,args)=>
            {
                int index=tabs.SelectedIndex-AgendaTab;
                if(args.Source!=tabs || index<0 || index>=pages.Length || built[index]) return;
                built[index]=true;
                switch(index)
                {
                    case 0: ItemPage(pages[index],false); break;
                    case 1: DesktopCatalogPage(pages[index]);AppPage(pages[index]); break;
                    case 2: DocumentPage(pages[index]); break;
                    case 3: ItemPage(pages[index],true); break;
                    case 4: ToolPage(pages[index]); break;
                    case 5: DesktopSystemPage(pages[index]); break;
                }
            };
            panel.Closed+=(s,e)=>{ if(readingCancellation!=null) readingCancellation.Cancel(); };
        }
        TextBox Field(Panel page,string caption,string text="")
        {
            page.Children.Add(Label(caption));
            var field=new TextBox {Text=text,Margin=new Thickness(0,0,0,7)};
            page.Children.Add(field);return field;
        }
        ComboBox Choice(Panel page,string caption,string[] choices)
        {
            page.Children.Add(Label(caption));
            var input=new ComboBox {ItemsSource=choices,SelectedIndex=0,Margin=new Thickness(0,0,0,7)};
            page.Children.Add(input);return input;
        }
        void ItemPage(Panel page,bool knowledge)
        {
            Heading(page,knowledge?"Conocimiento":"Agenda");
            var search=Field(page,"Buscar por texto, proyecto o etiqueta");
            var rows=new ListBox {Height=125,Margin=new Thickness(0,0,0,7)};
            page.Children.Add(rows);
            var text=Field(page,"Texto"); text.MaxLength=1500;
            var kind=Choice(page,"Tipo",knowledge?BaliToolRouter.Kinds.Where(k=>k!="Recordatorio" && k!="Pendiente").ToArray():
                new[]{"Pendiente","Recordatorio"});
            var project=Field(page,"Proyecto"); project.MaxLength=80;
            var tags=Field(page,"Etiquetas"); tags.MaxLength=240;
            var date=Field(page,"Fecha y hora local (AAAA-MM-DD HH:mm)");
            var priority=Choice(page,"Prioridad",new[]{"Alta","Normal","Baja"});priority.SelectedIndex=1;
            var repeat=Choice(page,"Repetición",new[]{"Una vez","Diario","Semanal"});
            var relation=new ComboBox {Margin=new Thickness(0,0,0,7)};
            if(knowledge){page.Children.Add(Label("Relacionar con un elemento"));page.Children.Add(relation);}
            var status=Label("");
            Action refresh=()=>
            {
                rows.ItemsSource=personal.Data.Items.Where(x=>(knowledge?x.Kind!="Recordatorio"&&x.Kind!="Pendiente":x.Kind=="Recordatorio"||x.Kind=="Pendiente") &&
                    BaliToolRouter.Matches(x,search.Text)).OrderByDescending(x=>x.Created).ToArray();
                relation.Items.Clear();
                relation.Items.Add(new ComboBoxItem {Content="Sin relación",Tag=Guid.Empty});
                foreach(var item in personal.Data.Items) relation.Items.Add(new ComboBoxItem {Content=item.ToString(),Tag=item.Id});
                relation.SelectedIndex=0;
            };
            search.TextChanged+=(s,e)=>refresh();
            refresh();
            rows.SelectionChanged+=(s,e)=>
            {
                var item=rows.SelectedItem as PersonalItem;if(item==null)return;
                text.Text=item.Text;kind.SelectedItem=item.Kind;project.Text=item.Project??"";tags.Text=item.Tags??"";
                date.Text=item.Date.HasValue?item.Date.Value.ToString("yyyy-MM-dd HH:mm"):"";
                repeat.SelectedItem=item.Repeat??"Una vez";priority.SelectedIndex=Math.Max(0,Math.Min(2,item.Priority-1));
                relation.SelectedItem=relation.Items.Cast<ComboBoxItem>().FirstOrDefault(x=>(Guid)x.Tag==item.Related);
                status.Text=item.State+(item.LastNotice==null?"":" · Último aviso "+item.LastNotice);
            };
            Action<bool> save=edit=>
            {
                var selected=rows.SelectedItem as PersonalItem;
                if(edit && selected==null){status.Text="Selecciona un elemento.";return;}
                DateTime parsed;
                DateTime? timestamp=null;
                if(date.Text.Trim().Length!=0)
                {
                    if(!DateTime.TryParseExact(date.Text.Trim(),"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed))
                    {status.Text="Formato de fecha inválido.";return;}
                    timestamp=parsed;
                }
                string type=(string)kind.SelectedItem;
                string id=edit?"editar":knowledge?"nota":type=="Recordatorio"?"recordar":"pendiente";
                var linked=relation.SelectedItem as ComboBoxItem;
                var action=new ToolRequest {Id=id,Item=edit?selected.Id:Guid.Empty,Text=text.Text,Kind=type,Date=timestamp,
                    Project=project.Text,Tags=tags.Text,Priority=priority.SelectedIndex+1,Repeat=repeat.SelectedIndex==0?null:(string)repeat.SelectedItem,
                    Related=linked==null?Guid.Empty:(Guid)linked.Tag};
                status.Text=toolsRouter.Execute(action,"panel",DateTime.Now).Message;
                refresh();
            };
            var actions=new WrapPanel();
            page.Children.Add(actions);
            ActionButton(actions,"Crear",()=>save(false));
            ActionButton(actions,"Guardar cambios",()=>save(true));
            foreach(var op in knowledge?new[]{"archivar"}:new[]{"completar","posponer","archivar"})
            {
                string operation=op;
                ActionButton(actions,op=="posponer"?"Posponer 10 min":Char.ToUpperInvariant(op[0])+op.Substring(1),()=>
                {
                    var selected=rows.SelectedItem as PersonalItem;if(selected==null)return;
                    status.Text=toolsRouter.Execute(new ToolRequest {Id="editar",Item=selected.Id,Operation=operation},"panel",DateTime.Now).Message;
                    refresh();
                });
            }
            ActionButton(actions,knowledge?"Eliminar":"Eliminar / cancelar",()=>
            {
                var selected=rows.SelectedItem as PersonalItem;
                if(selected==null || MessageBox.Show(panel,"¿Confirmas eliminar o cancelar el elemento seleccionado?","Agenda y conocimiento",MessageBoxButton.YesNo,
                    MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                status.Text=toolsRouter.Execute(new ToolRequest {Id="borrar",Item=selected.Id},"panel",DateTime.Now,true).Message;refresh();
            });
            if(knowledge) ActionButton(actions,"Exportar Markdown",()=>
            {
                var selected=rows.SelectedItem as PersonalItem;if(selected==null)return;
                var dialog=new SaveFileDialog {Filter="Markdown (*.md)|*.md",FileName="bali-nota.md",AddExtension=true,OverwritePrompt=true};
                if(dialog.ShowDialog(panel)!=true || MessageBox.Show(panel,"¿Guardar una copia local de este elemento?","Exportar Markdown",MessageBoxButton.YesNo,
                    MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                status.Text=toolsRouter.Execute(new ToolRequest {Id="exportar",Item=selected.Id,Path=dialog.FileName},"panel",DateTime.Now,true).Message;
            });
            else
            {
                Toggle(page,"Leer recordatorios explícitos en Concentrado",personal.Data.SpeakFocus,n=>personal.Change(x=>x.SpeakFocus=n));
                Toggle(page,"Leer recordatorios explícitos en Descanso",personal.Data.SpeakRest,n=>personal.Change(x=>x.SpeakRest=n));
                ActionButton(actions,"Actualizar",refresh);
                page.Children.Add(Label("Bali entrega avisos al estar en ejecución. Windows puede silenciar las notificaciones."));
            }
            page.Children.Add(status);
        }
        void AppPage(Panel page)
        {
            Heading(page,"Aplicaciones autorizadas");
            page.Children.Add(Label("Registra un ejecutable instalado en este equipo. Bali lo inicia sin argumentos."));
            var apps=new ListBox {Height=100,ItemsSource=personal.Data.Apps.ToArray()};
            page.Children.Add(apps);
            var name=Field(page,"Nombre");name.MaxLength=60;
            var alias=Field(page,"Alias separados por comas");alias.MaxLength=240;
            var status=Label("");
            var detected=new ComboBox {Margin=new Thickness(0,0,0,7)};
            page.Children.Add(Label("Aplicaciones detectadas en App Paths de Windows"));
            page.Children.Add(detected);
            Action registerDetected=()=>
            {
                var candidate=detected.SelectedItem as PersonalApp;
                if(candidate==null)return;
                if(MessageBox.Show(panel,"¿Autorizar "+candidate.Name+"?\n"+candidate.Path,"Aplicaciones",MessageBoxButton.YesNo,
                    MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                try
                {
                    personal.Change(x=>
                    {
                        x.Apps.RemoveAll(a=>String.Equals(a.Name,candidate.Name,StringComparison.OrdinalIgnoreCase));
                        x.Apps.Add(candidate);
                    });
                    apps.ItemsSource=personal.Data.Apps.ToArray();
                    toolsRouter.Audit("catálogo","panel","confirmada","registro detectado");
                    status.Text="Aplicación autorizada.";
                }
                catch(Exception){status.Text="No se pudo guardar la autorización.";}
            };
            ActionButton(page,"Detectar aplicaciones",()=>
            {
                detected.ItemsSource=DiscoverApps();
                detected.SelectedIndex=0;
                status.Text=detected.Items.Count==0?"No encontré entradas admitidas. Puedes registrar un ejecutable manualmente.":"Selecciona una aplicación para autorizarla.";
            });
            ActionButton(page,"Autorizar aplicación detectada",registerDetected);
            ActionButton(page,"Elegir ejecutable y autorizar",()=>
            {
                var dialog=new OpenFileDialog {Filter="Aplicación Windows (*.exe)|*.exe",CheckFileExists=true};
                if(dialog.ShowDialog(panel)!=true)return;
                var title=name.Text.Trim();
                if(title.Length==0 || !BaliToolRouter.SafeExecutable(dialog.FileName))
                {status.Text="Indica un nombre y elige un ejecutable local permitido.";return;}
                if(MessageBox.Show(panel,"¿Autorizar esta aplicación para abrirla por voz?\n"+dialog.FileName,"Aplicaciones",
                    MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                try
                {
                    personal.Change(x=>
                    {
                        x.Apps.RemoveAll(a=>String.Equals(a.Name,title,StringComparison.OrdinalIgnoreCase));
                        x.Apps.Add(new PersonalApp {Name=title,Alias=alias.Text,Path=dialog.FileName});
                    });
                    apps.ItemsSource=personal.Data.Apps.ToArray();
                    toolsRouter.Audit("catálogo","panel","confirmada","registrada");
                    status.Text="Aplicación autorizada.";
                }
                catch(Exception){status.Text="No se pudo guardar la autorización.";}
            });
            ActionButton(page,"Abrir selección",()=>
            {
                var app=apps.SelectedItem as PersonalApp;
                if(app!=null)status.Text=toolsRouter.Execute(new ToolRequest {Id="abrir",Text=app.Name},"panel",DateTime.Now).Message;
            });
            ActionButton(page,"Revocar autorización",()=>
            {
                var app=apps.SelectedItem as PersonalApp;
                if(app==null || MessageBox.Show(panel,"¿Revocar el acceso de Bali a esta aplicación?","Aplicaciones",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                personal.Change(x=>x.Apps.RemoveAll(a=>a.Name==app.Name));
                apps.ItemsSource=personal.Data.Apps.ToArray();toolsRouter.Audit("catálogo","panel","confirmada","revocada");
            });
            page.Children.Add(status);
        }
        static PersonalApp[] DiscoverApps()
        {
            var known=new[]{new[]{"Obsidian","Obsidian.exe"},new[]{"Codex","Codex.exe"},new[]{"Visual Studio Code","Code.exe"},
                new[]{"Microsoft Edge","msedge.exe"},new[]{"Google Chrome","chrome.exe"},new[]{"Brave","brave.exe"},new[]{"Firefox","firefox.exe"}};
            var result=new System.Collections.Generic.List<PersonalApp>();
            foreach(var row in known)
                foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine})
                {
                    try
                    {
                        using(var key=hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+row[1]))
                        {
                            string path=key==null?null:key.GetValue(null) as string;
                            if(!BaliToolRouter.SafeExecutable(path))continue;
                            result.Add(new PersonalApp{Name=row[0],Alias=Path.GetFileNameWithoutExtension(row[1]),Path=path});
                            break;
                        }
                    }
                    catch(Exception){}
                }
            return result.ToArray();
        }
        void DocumentPage(Panel page)
        {
            Heading(page,"Documentos");
            page.Children.Add(Label("Elige o arrastra un archivo. La búsqueda y el resumen extractivo se hacen localmente."));
            var status=Label(reading==null?"Sin documento activo.":reading.Name);
            Action<string> load=async path=>
            {
                if(!toolsRouter.Allowed("documentos") || readingCancellation!=null){status.Text="Lectura desactivada u ocupada.";return;}
                var owner=panel;
                var owned=new CancellationTokenSource(TimeSpan.FromSeconds(16));
                readingCancellation=owned;
                status.Text="Leyendo…";
                try
                {
                    var data=await Task.Run(()=>PersonalDocuments.Load(path,owned.Token));
                    if(panel!=owner || owned.IsCancellationRequested)return;
                    reading=data;
                    status.Text=data.Name+" · "+data.Parts.Count+" fragmentos locales";
                    toolsRouter.Audit("documentos","panel","selección explícita","extraído");
                }
                catch(OperationCanceledException){status.Text="Lectura cancelada.";}
                catch(InvalidOperationException e){status.Text=e.Message;}
                catch(Exception e){status.Text="No se pudo leer el documento.";toolsRouter.Audit("documentos","panel","selección explícita",e.GetType().Name);}
                finally{readingCancellation=null;owned.Dispose();}
            };
            page.AllowDrop=true;
            page.Drop+=(s,e)=>
            {
                var files=e.Data.GetData(DataFormats.FileDrop) as string[];
                if(files!=null && files.Length==1){e.Handled=true;load(files[0]);}
            };
            ActionButton(page,"Seleccionar archivo",()=>
            {
                var dialog=new OpenFileDialog {Filter="Documentos|*.txt;*.md;*.csv;*.docx;*.pdf",CheckFileExists=true};
                if(dialog.ShowDialog(panel)==true)load(dialog.FileName);
            });
            ActionButton(page,"Cancelar lectura",()=>{if(readingCancellation!=null)readingCancellation.Cancel();});
            ActionButton(page,"Descartar documento",()=>{if(readingCancellation!=null)readingCancellation.Cancel();reading=null;status.Text="Documento descartado.";});
            page.Children.Add(status);
            var query=Field(page,"Palabras de búsqueda o pregunta");
            var result=Label("");
            ActionButton(page,"Buscar fragmentos",()=>result.Text=toolsRouter.Execute(new ToolRequest {Id="documentos",Text=query.Text},"panel",DateTime.Now).Message);
            ActionButton(page,"Resumen extractivo",()=>result.Text=toolsRouter.Execute(new ToolRequest {Id="documentos",Text=""},"panel",DateTime.Now).Message);
            ActionButton(page,"Buscar posibles pendientes",()=>
            {
                if(!toolsRouter.Allowed("documentos")||reading==null){result.Text="Selecciona un documento autorizado.";return;}
                var hits=reading.Parts.Where(p=>Regex.IsMatch(p.Value,@"(?i)pendiente|tarea|todo|por hacer|\[ \]")).Take(5).ToArray();
                result.Text=hits.Length==0?"No encontré indicadores de pendientes.":String.Join("\n",hits.Select(x=>x.Key+" · "+x.Value.Substring(0,Math.Min(300,x.Value.Length))));
                toolsRouter.Audit("documentos","panel","autorizada","búsqueda de pendientes");
            });
            page.Children.Add(result);
        }
        void ToolPage(Panel page)
        {
            Heading(page,"Herramientas y permisos");
            foreach(var tool in toolsRouter.Tools)
            {
                string id=tool.Id;
                var box=new CheckBox {Content=tool.Description+" · "+tool.Risk,Foreground=ink,IsChecked=toolsRouter.Allowed(id),Margin=new Thickness(0,4,0,6)};
                box.Click+=(s,e)=>
                {
                    personal.Change(x=>{x.Disabled.Remove(id);if(box.IsChecked!=true)x.Disabled.Add(id);});
                    toolsRouter.Audit(id,"panel","explícita",box.IsChecked==true?"activada":"desactivada");
                };
                page.Children.Add(box);
            }
            Toggle(page,"Sugerir de forma discreta recordatorios próximos",personal.Data.Suggest,n=>personal.Change(x=>x.Suggest=n));
            var prompt=Field(page,"Petición abierta para proponer acciones");prompt.MaxLength=480;
            toolPrompt=prompt;
            if(stagedToolPrompt!=null) { prompt.Text=stagedToolPrompt; stagedToolPrompt=null; }
            panel.Closed+=(s,e)=>{toolPrompt=null;stagedToolPrompt=null;};
            var proposalStatus=Label("");
            var suggestions=new StackPanel();
            ActionButton(page,"Pedir propuesta a DeepSeek · puede generar cargo",()=>
            {
                if(busyConversation || floatingActive || capture!=null || transcription!=null || speaking || speechPending)return;
                if(!settings.Intelligence.Enabled || !settings.Intelligence.Verified || !vault.Exists)
                {proposalStatus.Text="Configura y verifica DeepSeek antes de pedir una propuesta.";return;}
                if(String.IsNullOrWhiteSpace(prompt.Text))return;
                if(MessageBox.Show(panel,"Se enviará la petición escrita a DeepSeek. ¿Autorizas una consulta que puede generar un cargo?","Propuesta",
                    MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                suggestions.Children.Clear();
                RunOnline(prompt.Text,false,proposalStatus,false,json=>
                {
                    var parsed=ToolProposal.Parse(json,DateTime.Now);
                    proposalStatus.Text=parsed.Length==0?"No se recibió una propuesta válida.":"Revisa las acciones. Cada una requiere tu autorización.";
                    foreach(var request in parsed)
                    {
                        var action=request;
                        string details=action.Id+" · "+action.Text+(action.Date.HasValue?" · "+action.Date.Value.ToString("dd/MM/yyyy HH:mm"):"");
                        suggestions.Children.Add(Label(details));
                        Button approve=null;
                        approve=ActionButton(suggestions,"Autorizar esta acción",()=>
                        {
                            if(MessageBox.Show(panel,details,"Autorizar propuesta",MessageBoxButton.YesNo,MessageBoxImage.Question,
                                MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                            approve.IsEnabled=false;
                            var result=toolsRouter.Execute(action,"panel",DateTime.Now);
                            toolsRouter.Audit(action.Id,"propuesta revisada","confirmada",result.Ok?"correcta":"rechazada");
                            proposalStatus.Text=result.Message;
                        });
                    }
                });
            });
            ActionButton(page,"Cancelar consulta",()=>{if(turn!=null)turn.Cancel();});
            page.Children.Add(proposalStatus);page.Children.Add(suggestions);
            var log=Label("");
            Action reload=()=>log.Text=String.Join("\n",personal.Data.Audit.AsEnumerable().Reverse().Take(24));
            ActionButton(page,"Actualizar bitácora",reload);page.Children.Add(log);reload();
        }
    }
}

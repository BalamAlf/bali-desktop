using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using Forms=System.Windows.Forms;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        void DesktopCatalogPage(Panel page)
        {
            Heading(page,"Catálogo de Windows");
            var status=Label(catalogLoaded?"Aplicaciones detectadas: "+desktopApps.Length:"Actualiza para consultar el catálogo.");
            page.Children.Add(status);
            var filter=Field(page,"Buscar aplicación");
            var saved=Label("");page.Children.Add(saved);
            var onlyAuthorized=new CheckBox{Content="Mostrar solo aplicaciones autorizadas"};
            page.Children.Add(onlyAuthorized);
            var list=new ListBox { Height=160 };
            page.Children.Add(list);
            var detail=Label("");page.Children.Add(detail);
            var alias=Field(page,"Alias del elemento seleccionado");
            var project=Field(page,"Proyecto asociado");
            var exactVoice=new CheckBox{Content="Abrir por voz con el nombre exacto, sin volver a confirmar",Margin=new System.Windows.Thickness(0,3,0,8)};
            page.Children.Add(exactVoice);
            var icon=new Image{Width=28,Height=28,HorizontalAlignment=System.Windows.HorizontalAlignment.Left};
            page.Children.Add(icon);
            Action show=()=>
            {
                var selection=(list.SelectedItem as DesktopApp);
                var entries=VisibleDesktopApps();
                var visible=onlyAuthorized.IsChecked==true?entries.Where(CanUseDesktopApp).ToArray():entries;
                var filtered=String.IsNullOrWhiteSpace(filter.Text)?visible:DesktopCatalog.Match(visible,filter.Text);
                list.ItemsSource=filtered;
                if(selection!=null)list.SelectedItem=filtered.FirstOrDefault(x=>String.Equals(x.Id,selection.Id,StringComparison.OrdinalIgnoreCase));
                int remembered=desktopConfig.Permissions.Count(x=>!x.Excluded);
                int available=entries.Count(CanUseDesktopApp);
                saved.Text="Permisos guardados: "+remembered+" · disponibles en el catálogo: "+available+".";
            };
            filter.TextChanged+=(s,e)=>show();
            onlyAuthorized.Checked+=(s,e)=>show();
            onlyAuthorized.Unchecked+=(s,e)=>show();
            Action loaded=()=>{status.Text="Catálogo actualizado.";show();};
            DesktopCatalogChanged+=loaded;
            panel.Closed+=(s,e)=>DesktopCatalogChanged-=loaded;
            list.SelectionChanged+=(s,e)=>
            {
                var app=list.SelectedItem as DesktopApp;
                if(app==null)return;
                var selected=Permission(app);
                alias.Text=selected==null?app.Alias??"":selected.Alias??app.Alias??"";
                project.Text=selected==null?"":selected.Project??"";
                exactVoice.IsChecked=selected!=null && selected.OpenByExactVoice;
                detail.Text=(selected==null?"Requiere autorización":selected.Excluded?"Excluida":"Autorizada")+"\n"+(app.Path??app.Id);
                icon.Source=null;
                if(app.Path!=null && DesktopPaths.Executable(app.Path))
                    try{using(var image=System.Drawing.Icon.ExtractAssociatedIcon(app.Path))if(image!=null)
                        icon.Source=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(image.Handle,System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());}
                    catch(Exception){}
            };
            var actions=new WrapPanel();page.Children.Add(actions);
            ActionButton(actions,"Actualizar",()=>RefreshDesktopCatalog(message=>{status.Text=message;show();}));
            ActionButton(actions,"Cancelar indexación",()=>{if(catalogWork!=null){catalogWork.Cancel();status.Text="Indexación cancelada."; }});
            ActionButton(actions,"Autorizar y guardar alias",async ()=>
            {
                var app=list.SelectedItem as DesktopApp;
                if(app==null)return;
                string approvedAlias=alias.Text.Trim(), approvedProject=project.Text.Trim();
                bool approvedExactVoice=exactVoice.IsChecked==true;
                try
                {
                    var fingerprint=app.Packaged?null:await System.Threading.Tasks.Task.Run(()=>DesktopCatalog.Fingerprint(app.Path));
                    if(!app.Packaged && fingerprint==null){status.Text="La ruta ya no es válida.";return;}
                    if(app.Portable && !desktopConfig.Folders.Contains(app.Root,StringComparer.OrdinalIgnoreCase))
                    {status.Text="La carpeta portátil dejó de estar autorizada.";return;}
                    DesktopPermission item=Permission(app)??new DesktopPermission{Id=app.Id};
                    if(!desktopConfig.Permissions.Contains(item))desktopConfig.Permissions.Add(item);
                    item.Name=app.Name;item.Alias=approvedAlias;item.Project=approvedProject;item.Excluded=false;item.Hash=fingerprint;
                    item.OpenByExactVoice=approvedExactVoice;
                    SaveDesktop();ApplyDesktopPermissions();show();status.Text="Permiso guardado para "+app.Name+".";
                    toolsRouter.Audit("abrir-catalogo","panel","autorizada","permiso guardado");
                }
                catch(Exception){status.Text="No pude guardar la autorización.";}
            });
            ActionButton(actions,"Excluir o revocar",()=>
            {
                var app=list.SelectedItem as DesktopApp;if(app==null)return;
                var item=Permission(app);
                if(item==null){item=new DesktopPermission{Id=app.Id};desktopConfig.Permissions.Add(item);}
                item.Excluded=true;item.Hash=null;item.OpenByExactVoice=false;SaveDesktop();ApplyDesktopPermissions();show();status.Text=app.Name+" excluida.";
            });
            ActionButton(actions,"Abrir",()=>
            {
                var app=list.SelectedItem as DesktopApp;if(app==null)return;
                var permission=Permission(app);
                RunDesktop("abrir-catalogo","panel",t=>DesktopCatalog.Launch(app,permission),key:app.Id,callback:m=>status.Text=m);
            });
            ActionButton(actions,"Abrir carpeta",()=>
            {
                var app=list.SelectedItem as DesktopApp;if(app==null || app.Path==null)return;
                RunDesktop("catalogo","panel",t=>OpenDesktopFolder(Path.GetDirectoryName(app.Path)),callback:m=>status.Text=m);
            });
            Heading(page,"Carpetas portátiles autorizadas");
            var roots=new ListBox {Height=75};page.Children.Add(roots);
            Action showRoots=()=>roots.ItemsSource=desktopConfig.Folders.ToArray();
            showRoots();show();
            ActionButton(page,"Autorizar carpeta",()=>
            {
                using(var picker=new Forms.FolderBrowserDialog{Description="Elige una carpeta local concreta de aplicaciones o de un proyecto."})
                {
                    if(picker.ShowDialog()!=Forms.DialogResult.OK)return;
                    string path=Path.GetFullPath(picker.SelectedPath).TrimEnd('\\');
                    if(!DesktopPaths.Local(path) || path.Length<=3 || desktopConfig.Folders.Count>=16){status.Text="Elige una carpeta local específica.";return;}
                    if(!desktopConfig.Folders.Contains(path,StringComparer.OrdinalIgnoreCase)){desktopConfig.Folders.Add(path);SaveDesktop();}
                    showRoots();RefreshDesktopCatalog(m=>{status.Text=m;show();});
                }
            });
            ActionButton(page,"Revocar carpeta",()=>
            {
                var path=roots.SelectedItem as string;if(path==null)return;
                desktopConfig.Folders.Remove(path);
                desktopConfig.Permissions.RemoveAll(p=>DesktopPaths.Under(path,p.Id));
                desktopApps=desktopApps.Where(x=>x.Root!=path).ToArray();
                SaveDesktop();ApplyDesktopPermissions();showRoots();show();
            });
        }
        void DesktopSystemPage(Panel page)
        {
            Heading(page,"Recursos");
            var status=Label("");page.Children.Add(status);
            var metrics=new WrapPanel();page.Children.Add(metrics);
            foreach(var item in new[]{"RAM|ram","CPU y procesos|cpu","Procesos por RAM|procesos-ram","Unidades|disco","Tiempo activo|actividad","Batería|batería"})
            {
                var parts=item.Split('|');
                ActionButton(metrics,parts[0],()=>RunDesktop("sistema","panel",t=>DesktopMetrics.Read(parts[1],t),callback:m=>status.Text=m));
            }
            Heading(page,"Temporales del usuario");
            page.Children.Add(Label(DesktopMaintenance.UserRoot()));
            var report=Label("");page.Children.Add(report);
            ActionButton(page,"Analizar sin borrar",()=>RunDesktop("analizar-temporales","panel",t=>
            {
                var plan=DesktopMaintenance.Analyze(DesktopMaintenance.UserRoot(),DateTime.UtcNow,t);
                return Dispatcher.Invoke(()=>{
                    t.ThrowIfCancellationRequested();
                    desktopAnalysis=plan;
                    var message=plan.Files.Length+" candidatos · "+DesktopMetrics.Size(plan.Bytes)+".";
                    AddMaintenance(message);return message;
                });
            },callback:m=>
            {
                var plan=desktopAnalysis;
                report.Text=m+(plan==null || plan.Files.Length==0?"":"\n"+String.Join("\n",plan.Files.Take(20).Select(x=>Path.GetFileName(x.Path)+" · "+DesktopMetrics.Size(x.Size))));
                RefreshMaintenanceHistory();
            }));
            ActionButton(page,"Limpiar los candidatos analizados",()=>
            {
                var plan=desktopAnalysis;
                if(plan==null || plan.Expires<=DateTime.UtcNow || plan.Files.Length==0){report.Text="Hace falta un análisis vigente con candidatos.";return;}
                if(System.Windows.MessageBox.Show(panel,"Eliminar "+plan.Files.Length+" archivos del análisis vigente?",
                    "Confirmar limpieza",System.Windows.MessageBoxButton.YesNo)!=System.Windows.MessageBoxResult.Yes)return;
                desktopAnalysis=null;
                RunDesktop("limpiar-temporales","panel",t=>CleanDesktopPlan(plan,t),true,"limpiar-temporales",m=>{report.Text=m;RefreshMaintenanceHistory();});
            });
            ActionButton(page,"Cancelar operación en curso",()=>{if(desktopWork!=null)desktopWork.Cancel();ClearDesktopPending();});
            Toggle(page,"Sugerir mantenimiento por poco espacio",desktopConfig.Suggestions,n=>
            {desktopConfig.Suggestions=n;if(n)desktopConfig.Proactive.Enable=true;SaveDesktop();});
            Heading(page,"Historial de mantenimiento");
            maintenanceHistory=Label("");page.Children.Add(maintenanceHistory);RefreshMaintenanceHistory();
            DesktopRoutinePage(page);
            DesktopDictionaryPage(page);
            panel.Closed+=(s,e)=>maintenanceHistory=null;
        }
        TextBlock maintenanceHistory;
        void RefreshMaintenanceHistory()
        {if(maintenanceHistory!=null)maintenanceHistory.Text=String.Join("\n",desktopConfig.Maintenance.Take(10));}
        void DesktopDictionaryPage(Panel page)
        {
            Heading(page,"Diccionario técnico");
            var words=new ListBox {Height=95};page.Children.Add(words);
            Action show=()=>words.ItemsSource=desktopConfig.Words.ToArray();
            var spoken=Field(page,"Cómo suena");
            var written=Field(page,"Cómo se escribe");
            var context=Choice(page,"Aplicar solo en",new[]{"Aplicaciones","Preguntas técnicas","Agenda","Contexto técnico general"});
            var status=Label("");page.Children.Add(status);
            words.SelectionChanged+=(s,e)=>{var selected=words.SelectedItem as TechnicalWord;if(selected!=null)
            {spoken.Text=selected.Spoken;written.Text=selected.Written;context.SelectedItem=selected.Context??"Contexto técnico general";}};
            ActionButton(page,"Copiar última transcripción para corregir",()=>
            {
                if(lastSpeech==null || !lastSpeech.HasText){status.Text="Haz primero una prueba en Voz.";return;}
                spoken.Text=lastSpeech.Text.Trim(' ','¿','?','¡','!','.');
                status.Text="Conserva solo el término erróneo y escribe la corrección. Se aprende al guardar.";
            });
            ActionButton(page,"Guardar equivalencia",()=>
            {
                var value=new TechnicalWord{Spoken=spoken.Text.Trim(),Written=written.Text.Trim(),Context=context.SelectedIndex==3?null:context.SelectedItem as string};
                if(!TechnicalSpeech.Valid(value) || MemoryBook.LooksSensitive(value.Spoken) || MemoryBook.LooksSensitive(value.Written))
                {status.Text="Usa términos cortos y sin datos sensibles.";return;}
                var editing=words.SelectedItem as TechnicalWord;
                if(editing!=null)desktopConfig.Words.Remove(editing);
                desktopConfig.Words.RemoveAll(x=>String.Equals(x.Spoken,value.Spoken,StringComparison.OrdinalIgnoreCase) && x.Context==value.Context);
                if(desktopConfig.Words.Count>=100){status.Text="El diccionario alcanzó su límite.";return;}
                desktopConfig.Words.Add(value);SaveDesktop();show();status.Text="Equivalencia guardada.";
            });
            ActionButton(page,"Eliminar equivalencia seleccionada",()=>
            {var word=words.SelectedItem as TechnicalWord;if(word!=null){desktopConfig.Words.Remove(word);SaveDesktop();show();}});
            show();
        }
    }
}

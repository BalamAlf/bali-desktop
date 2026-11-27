using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Controls;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        DesktopRoutine FindRoutine(string name)
        {return desktopConfig.Routines.FirstOrDefault(r=>DesktopCatalog.Normalize(r.Name)==DesktopCatalog.Normalize(name));}
        string SaveNamedRoutine(string name)
        {
            name=name.Trim();
            if(name.Length<2 || name.Length>60 || desktopConfig.Routines.Count>=30 || FindRoutine(name)!=null)
                return "Usa un nombre único de 2 a 60 caracteres.";
            desktopConfig.Routines.Add(new DesktopRoutine{Name=name});SaveDesktop();return "Rutina creada. Puedes añadir pasos en Sistema.";
        }
        bool TryDesktopRoutine(string text,string origin,bool floating)
        {
            var action=Regex.Match(text,@"^(crea|crea una|elimina|ejecuta|inicia)\s+(?:la\s+)?rutina\s+(.+)$",RegexOptions.IgnoreCase);
            var edit=Regex.Match(text,@"^(?:agrega|añade)\s+a\s+(?:la\s+)?rutina\s+(.+?)\s+(abrir|carpeta|consultar pendientes|modo concentrado)(?:\s+(.+))?$",RegexOptions.IgnoreCase);
            if(!action.Success && !edit.Success)return false;
            if(!toolsRouter.Allowed("rutina")){DesktopReply("La herramienta está desactivada.",floating);return true;}
            if(edit.Success)
            {
                var routine=FindRoutine(edit.Groups[1].Value);
                if(routine==null){DesktopReply("No encontré esa rutina.",floating);return true;}
                string name=edit.Groups[3].Value;
                DesktopStep step=null;
                switch(edit.Groups[2].Value.ToLowerInvariant())
                {
                    case "consultar pendientes":step=new DesktopStep{Kind=DesktopStepKind.Agenda};break;
                    case "modo concentrado":step=new DesktopStep{Kind=DesktopStepKind.Focus};break;
                    case "abrir":
                        var apps=DesktopCatalog.Match(desktopApps.Where(CanUseDesktopApp),name);
                        if(apps.Length==1)step=new DesktopStep{Kind=DesktopStepKind.Open,Target=apps[0].Id};
                        break;
                    case "carpeta":
                        var paths=desktopConfig.Folders.Where(p=>DesktopCatalog.Normalize(Path.GetFileName(p))==DesktopCatalog.Normalize(name)).ToArray();
                        if(paths.Length==1)step=new DesktopStep{Kind=DesktopStepKind.Folder,Target=paths[0]};
                        break;
                }
                if(step==null || routine.Steps.Count>=12)
                {DesktopReply("El paso necesita un destino autorizado y único. El máximo es doce pasos.",floating);return true;}
                Func<CancellationToken,string> save=t=>Dispatcher.Invoke(()=>{t.ThrowIfCancellationRequested();routine.Steps.Add(step);SaveDesktop();return "Paso guardado en "+routine.Name+".";});
                DesktopReply(AskDesktop("rutina","¿Agrego el paso "+step+" a "+routine.Name+"?",origin,save),floating);
                return true;
            }
            var verb=action.Groups[1].Value.ToLowerInvariant();
            var target=action.Groups[2].Value;
            if(verb.StartsWith("crea",StringComparison.Ordinal))
            {
                DesktopReply(AskDesktop("rutina","¿Creo la rutina "+target+"?",origin,t=>Dispatcher.Invoke(()=>SaveNamedRoutine(target))),floating);
                return true;
            }
            var chosen=FindRoutine(target);
            if(chosen==null){DesktopReply("No encontré esa rutina.",floating);return true;}
            if(verb=="elimina")
            {
                DesktopReply(AskDesktop("rutina","¿Elimino la rutina "+chosen.Name+"?",origin,
                    t=>Dispatcher.Invoke(()=>{desktopConfig.Routines.Remove(chosen);SaveDesktop();return "Rutina eliminada.";})),floating);
                return true;
            }
            StartRoutine(chosen,origin,floating);
            return true;
        }
        bool CanUseDesktopApp(DesktopApp app)
        {var permit=Permission(app);return permit!=null && !permit.Excluded;}
        void StartRoutine(DesktopRoutine routine,string origin,bool floating,Action<string> report=null)
        {
            if(routine.Steps.Count==0){if(report!=null)report("La rutina no tiene pasos.");else DesktopReply("La rutina no tiene pasos.",floating);return;}
            var steps=routine.Steps.ToArray();
            Func<CancellationToken,string> execute=token=>
            {
                var lines=new List<string>();var started=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var step in steps)
                {
                    if(token.IsCancellationRequested){lines.Add("Pasos restantes cancelados.");break;}
                    try
                    {
                        switch(step.Kind)
                        {
                            case DesktopStepKind.Open:
                                if(!started.Add(step.Target)){lines.Add("Apertura repetida omitida.");break;}
                                var app=Dispatcher.Invoke(()=>toolsRouter.Allowed("abrir-catalogo")?
                                    desktopApps.FirstOrDefault(x=>x.Id==step.Target && CanUseDesktopApp(x)):null);
                                lines.Add(app==null?"Aplicación omitida por permiso o ubicación.":DesktopCatalog.Launch(app,Dispatcher.Invoke(()=>Permission(app))));
                                break;
                            case DesktopStepKind.Folder:
                                lines.Add(Dispatcher.Invoke(()=>toolsRouter.Allowed("abrir-catalogo") &&
                                    desktopConfig.Folders.Contains(step.Target,StringComparer.OrdinalIgnoreCase))?
                                    OpenDesktopFolder(step.Target):"Carpeta omitida por permiso.");
                                break;
                            case DesktopStepKind.Agenda:
                                lines.Add(Dispatcher.Invoke(()=>toolsRouter.Interpret("qué tengo hoy",origin,DateTime.Now)));
                                break;
                            case DesktopStepKind.Focus:
                                Dispatcher.Invoke(()=>ChangeMode(Mode.Concentrado));lines.Add("Modo Concentrado activado.");break;
                            default:lines.Add("Paso desconocido omitido.");break;
                        }
                    }
                    catch(Exception){lines.Add("Windows no completó el paso.");}
                }
                if(!token.IsCancellationRequested && lines.Count>0)
                    Dispatcher.Invoke(()=>{
                        if(!settings.Learning || FindRoutine(routine.Name)!=routine)return;
                        var usage=desktopConfig.Proactive.Uses;
                        int count;usage.TryGetValue(routine.Name,out count);
                        usage[routine.Name]=Math.Min(100,count+1);
                        SaveDesktop();
                    });
                return String.Join("\n",lines);
            };
            if(floating && sttNeedsReview)
                DesktopReply(AskDesktop("rutina","¿Ejecuto la rutina "+routine.Name+"?",origin,execute),true);
            else RunDesktop("rutina",origin,execute,key:"rutina|"+routine.Name,callback:report);
        }
        void DesktopRoutinePage(Panel page)
        {
            Heading(page,"Rutinas");
            var routines=new ListBox{Height=95};page.Children.Add(routines);
            var steps=new ListBox{Height=95};page.Children.Add(steps);
            Action showSteps=()=>steps.ItemsSource=(routines.SelectedItem as DesktopRoutine)==null?new DesktopStep[0]:((DesktopRoutine)routines.SelectedItem).Steps.ToArray();
            Action show=()=>{routines.ItemsSource=desktopConfig.Routines.ToArray();showSteps();};
            routines.SelectionChanged+=(s,e)=>showSteps();
            var name=Field(page,"Nombre de la rutina");
            var result=Label("");page.Children.Add(result);
            ActionButton(page,"Crear rutina",()=>
            {
                if(!toolsRouter.Allowed("rutina")){result.Text="La herramienta está desactivada.";return;}
                result.Text=SaveNamedRoutine(name.Text);show();
            });
            ActionButton(page,"Renombrar rutina seleccionada",()=>
            {
                var routine=routines.SelectedItem as DesktopRoutine;
                if(!toolsRouter.Allowed("rutina") || routine==null || name.Text.Trim().Length<2 || name.Text.Trim().Length>60 || FindRoutine(name.Text)!=null)return;
                routine.Name=name.Text.Trim();SaveDesktop();show();
            });
            ActionButton(page,"Eliminar rutina seleccionada",()=>
            {
                var routine=routines.SelectedItem as DesktopRoutine;
                if(!toolsRouter.Allowed("rutina") || routine==null)return;
                if(System.Windows.MessageBox.Show(panel,"¿Eliminar la rutina "+routine.Name+"?","Confirmar",System.Windows.MessageBoxButton.YesNo)
                    !=System.Windows.MessageBoxResult.Yes)return;
                desktopConfig.Routines.Remove(routine);SaveDesktop();show();
            });
            var kind=Choice(page,"Tipo de paso",new[]{"Abrir aplicación","Abrir carpeta autorizada","Consultar agenda","Modo Concentrado"});
            var destination=new ComboBox();page.Children.Add(destination);
            Action populate=()=>destination.ItemsSource=kind.SelectedIndex==0?
                desktopApps.Where(CanUseDesktopApp).Cast<object>().ToArray():kind.SelectedIndex==1?desktopConfig.Folders.Cast<object>().ToArray():new object[0];
            kind.SelectionChanged+=(s,e)=>populate();destination.DropDownOpened+=(s,e)=>populate();populate();
            ActionButton(page,"Añadir paso",()=>
            {
                var routine=routines.SelectedItem as DesktopRoutine;
                if(!toolsRouter.Allowed("rutina") || routine==null || kind.SelectedIndex<0 || routine.Steps.Count>=12)return;
                var app=destination.SelectedItem as DesktopApp;
                string target=kind.SelectedIndex==0?(app==null?null:app.Id):destination.SelectedItem as string;
                if(kind.SelectedIndex<=1 && target==null){result.Text="Selecciona un destino autorizado.";return;}
                routine.Steps.Add(new DesktopStep{Kind=(DesktopStepKind)kind.SelectedIndex,Target=target});SaveDesktop();showSteps();
            });
            ActionButton(page,"Quitar paso seleccionado",()=>
            {
                var routine=routines.SelectedItem as DesktopRoutine;var step=steps.SelectedItem as DesktopStep;
                if(toolsRouter.Allowed("rutina") && routine!=null && step!=null){routine.Steps.Remove(step);SaveDesktop();showSteps();}
            });
            ActionButton(page,"Ejecutar rutina seleccionada",()=>
            {var routine=routines.SelectedItem as DesktopRoutine;if(routine!=null)StartRoutine(routine,"panel",false,m=>result.Text=m);});
            show();
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void CheckComputerControl(TextWriter log)
        {
            var before=new ComputerSettings();
            Check(!before.Windows && !before.Controls && !before.Dictation,"Control de escritorio requiere permisos separados",log);
            Check(ComputerRequest.Parse("Bali, minimiza esta aplicación").Verb==ComputerVerb.Minimize &&
                ComputerRequest.Parse("Cambia a la ventana anterior").Verb==ComputerVerb.Previous &&
                ComputerRequest.Parse("Cambia la ventana anterior").Verb==ComputerVerb.Previous &&
                ComputerRequest.Parse("Desplázate hacia abajo").Verb==ComputerVerb.ScrollDown &&
                ComputerRequest.Parse("Abre la segunda opción").Verb==ComputerVerb.Select &&
                ComputerRequest.Parse("Escribe lo siguiente").Verb==ComputerVerb.Dictate,"Reconoce comandos tipados de control y dictado",log);
            Check(ComputerRequest.Parse("Envía un correo").Verb==ComputerVerb.None &&
                ComputerRequest.Parse("pulsa el botón que dice elimina la cuenta").Verb==ComputerVerb.None &&
                ComputerRequest.Parse("escribe mi contraseña").Verb==ComputerVerb.None,
                "Las acciones restringidas quedan fuera del motor",log);
            Check(ComputerRequest.ClearLowRisk(ComputerRequest.Parse("Minimiza esta ventana"),"Minimiza esta ventana") &&
                !ComputerRequest.ClearLowRisk(ComputerRequest.Parse("Minimiza Word"),"Minimiza Word") &&
                !ComputerRequest.ClearLowRisk(ComputerRequest.Parse("Minimiza esta ventana..."),"Minimiza esta ventana..."),
                "Solo los comandos simples y completos de bajo riesgo pueden seguir sin aclaración",log);
            var items=new[]{
                new VisualElement{Name="Primera",Kind="TabItem",Bounds=new Rect(10,10,30,15),Enabled=true},
                new VisualElement{Name="Segunda",Kind="TabItem",Bounds=new Rect(50,10,30,15),Enabled=true},
                new VisualElement{Name="Aceptar",Kind="Button",Bounds=new Rect(10,90,30,15),Enabled=true}
            };
            Check(VisualResolver.Resolve(items,"la segunda opción","TabItem").Single().Name=="Segunda" &&
                VisualResolver.Resolve(items,"el botón de abajo","Button").Single().Name=="Aceptar" &&
                VisualResolver.Resolve(items,"la pestaña fantasma","TabItem").Length==0,
                "Resuelve referencias espaciales, ordinales y nombres sin inventar destinos",log);
            Check(VisualResolver.Resolve(items,"la pestaña de arriba","TabItem").Length==2 &&
                VisualResolver.Resolve(items,"la pestaña derecha","TabItem").Single().Name=="Segunda",
                "Detecta ambigüedad espacial y conserva coincidencias únicas",log);
            var draft=new DictationDraft();
            draft.Add("Texto errado");draft.SelectLast(false);draft.Add("corregido");draft.Punctuation("punto y aparte");
            draft.Add("Segundo párrafo");draft.SelectLast(true);
            Check(draft.Text=="Texto corregido.\n\nSegundo párrafo" && draft.Selected=="Segundo párrafo","Corrige y selecciona texto solo en el borrador",log);
            var literal=new DictationDraft();
            Check(literal.Add("envía un correo") && literal.Text=="envía un correo" && WhisperRecognizer.Clean("Microsoft",true)=="Microsoft" &&
                WhisperRecognizer.Clean("Microsoft")==null,"El dictado conserva texto literal y permite una sola palabra",log);
            Check(ComputerActions.Restricted(new VisualWindow{App="consent",Title="Prueba"}) &&
                DictationWriter.Insert(null,"texto",new string[0],CancellationToken.None).Status==ComputerStatus.Blocked,
                "Bloquea procesos sensibles y el dictado sin destino",log);
        }
        public static int ComputerFixture(string mode)
        {
            var app=new Application();
            var tabs=new TabControl();
            var list=new StackPanel();
            for(int i=0;i<95;i++)list.Children.Add(new TextBlock{Text="Elemento sintético "+(i+1),FontSize=18,Margin=new Thickness(5)});
            var scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Visible};
            AutomationProperties.SetName(scroll,"Lista de prueba");
            var tab1=new TabItem{Header="Primera",Content=scroll};
            var edit=new TextBox{Text="Texto inicial.",Margin=new Thickness(25),FontSize=22,AcceptsReturn=true,VerticalContentAlignment=VerticalAlignment.Top};
            AutomationProperties.SetName(edit,"Editor sintético");
            var pass=new PasswordBox{Margin=new Thickness(25),Password="NONREAL-PASSWORD-FIXTURE"};
            AutomationProperties.SetName(pass,"Campo protegido");
            var tab2=new TabItem{Header="Segunda",Content=mode=="protected"?(object)pass:edit};
            tabs.Items.Add(tab1);tabs.Items.Add(tab2);
            if(mode!="main")tabs.SelectedIndex=1;
            var window=new Window{Title="Bali control prueba "+mode,Content=tabs,Width=580,Height=370,
                WindowStartupLocation=WindowStartupLocation.CenterScreen,Topmost=true};
            window.Loaded+=(s,e)=>window.Dispatcher.BeginInvoke(new Action(()=>{
                window.Activate();
                if(mode=="protected")pass.Focus();else if(mode!="main")edit.Focus();
            }),DispatcherPriority.ApplicationIdle);
            var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};
            timer.Tick+=(s,e)=>{timer.Stop();window.Close();};timer.Start();
            return app.Run(window);
        }
        public static int ComputerEvidence(string folder)
        {
            Directory.CreateDirectory(folder);
            using(var log=new StreamWriter(Path.Combine(folder,"computer.txt")){AutoFlush=true})
            using(var child=StartComputerFixture("main"))
            using(var own=Process.GetCurrentProcess())
            {
                var elapsed=Stopwatch.StartNew();double cpu=own.TotalProcessorTime.TotalSeconds;
                bool pass=true;var excluded=new string[0];
                try
                {
                    var window=WaitComputerFixture(child);
                    if(window==null){log.WriteLine("FAIL No apareció la ventana sintética.");return 1;}
                    Action<string,ComputerResult,ComputerStatus> record=(name,result,expected)=>
                    {bool ok=result.Status==expected;pass&=ok;log.WriteLine((ok?"PASS ":"FAIL ")+name+" · "+result.Status+" · "+result.Message);};
                    var watch=Stopwatch.StartNew();
                    record("Maximización real",ComputerActions.Window(window,ComputerVerb.Maximize,excluded,CancellationToken.None),ComputerStatus.Verified);
                    record("Minimización real",ComputerActions.Window(window,ComputerVerb.Minimize,excluded,CancellationToken.None),ComputerStatus.Verified);
                    record("Restauración real",ComputerActions.Window(window,ComputerVerb.Restore,excluded,CancellationToken.None),ComputerStatus.Verified);
                    log.WriteLine("Tres operaciones y su verificación: "+watch.ElapsedMilliseconds+" ms");
                    record("Ventana excluida",ComputerActions.Window(window,ComputerVerb.Maximize,new[]{window.Title},CancellationToken.None),ComputerStatus.Blocked);
                    record("Cambio de ventana real",ComputerActions.Window(window,ComputerVerb.Activate,excluded,CancellationToken.None),ComputerStatus.Verified);
                    window=VisualWindows.Inspect(window.Handle);
                    if(window==null)return 1;
                    var accessible=VisualAccessibility.Read(window,CancellationToken.None);
                    var second=VisualResolver.Resolve(accessible.Elements,"la segunda opción","TabItem").SingleOrDefault();
                    bool located=second!=null && second.Bounds.Width>0 && second.Source=="UI Automation";
                    log.WriteLine((located?"PASS ":"FAIL ")+"UI Automation localiza y ordena la segunda pestaña.");pass&=located;
                    record("Desplazamiento real",ComputerActions.Control(window,null,ComputerVerb.ScrollDown,excluded,CancellationToken.None),ComputerStatus.Verified);
                    record("Selección real de pestaña",ComputerActions.Control(window,second,ComputerVerb.Select,excluded,CancellationToken.None),ComputerStatus.Verified);
                    record("Invocación genérica bloqueada",ComputerActions.Control(window,second,ComputerVerb.Invoke,excluded,CancellationToken.None),ComputerStatus.Blocked);
                    record("Control inexistente bloqueado",ComputerActions.Control(window,new VisualElement{Source="UI Automation",Name="inexistente"},
                        ComputerVerb.Select,excluded,CancellationToken.None),ComputerStatus.Blocked);
                    using(var editor=StartComputerFixture("editor"))
                    {
                        try
                        {
                            var editWindow=WaitComputerFixture(editor);
                            if(editWindow==null)return 1;
                            record("Activación de editor sintético",ComputerActions.Window(editWindow,ComputerVerb.Activate,excluded,CancellationToken.None),ComputerStatus.Verified);
                            var target=DictationWriter.Prepare(editWindow,excluded,true);
                            record("Dictado real con inserción verificada",DictationWriter.Insert(target,"Párrafo de prueba.",excluded,CancellationToken.None,true),ComputerStatus.Verified);
                            record("Cambio de contenido bloquea repetir",DictationWriter.Insert(target,"No repetir.",excluded,CancellationToken.None,true),ComputerStatus.Blocked);
                            using(var protectedEditor=StartComputerFixture("protected"))
                            {
                                try
                                {
                                    var protectedWindow=WaitComputerFixture(protectedEditor);
                                    if(protectedWindow==null)return 1;
                                    record("Dictado no escribe tras cambiar de foco",DictationWriter.Insert(target,"No insertar.",excluded,CancellationToken.None,true),ComputerStatus.Blocked);
                                    record("Campo protegido bloqueado",DictationWriter.Insert(DictationWriter.Prepare(protectedWindow,excluded,true),
                                        "No insertar.",excluded,CancellationToken.None,true),ComputerStatus.Blocked);
                                }
                                finally{CloseFixture(protectedEditor);}
                            }
                        }
                        finally{CloseFixture(editor);}
                    }
                    own.Refresh();
                    log.WriteLine(String.Format("{0:0.00} s · CPU {1:0.00} s · RAM final {2:0.0} MiB · pico {3:0.0} MiB",
                        elapsed.Elapsed.TotalSeconds,own.TotalProcessorTime.TotalSeconds-cpu,own.WorkingSet64/1048576d,own.PeakWorkingSet64/1048576d));
                }
                catch(Exception ex){log.WriteLine("FAIL "+ex.GetType().Name+" · "+ex.Message);return 1;}
                finally{CloseFixture(child);}
                return pass?0:1;
            }
        }
        static Process StartComputerFixture(string mode)
        {return Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--computer-fixture "+mode){UseShellExecute=false});}
        static VisualWindow WaitComputerFixture(Process child)
        {
            var timer=Stopwatch.StartNew();
            while(timer.Elapsed.TotalSeconds<6)
            {
                var value=VisualWindows.List(true).FirstOrDefault(x=>x.ProcessId==child.Id);
                if(value!=null){Thread.Sleep(160);return value;}
                Thread.Sleep(90);
            }
            return null;
        }
        static void CloseFixture(Process child)
        {if(!child.HasExited){child.CloseMainWindow();child.WaitForExit(2000);}}
    }
}

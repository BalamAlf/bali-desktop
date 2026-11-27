using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Speech.Synthesis;
using System.Speech.Recognition;
using Forms = System.Windows.Forms;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void Check(bool ok,string name,TextWriter output)
        {
            if (!ok) throw new InvalidOperationException(name);
            output.WriteLine("PASS "+name);
        }
        public static int Run(string folder)
        {
            using (var log=new StreamWriter(Path.Combine(folder,"unit.txt")))
            try
            {
                var a=new Settings();
                var b=new Settings();
                var first=new BaliLifeEngine(a,71);
                var second=new BaliLifeEngine(b,71);
                double original=a.Memory.Energy;
                bool equal=true;
                for(int i=0;i<330;i++)
                {
                    first.Tick(1,0,false,true);
                    second.Tick(1,0,false,true);
                    equal &= first.Current==second.Current && first.MoveRequested==second.MoveRequested;
                }
                Check(equal && a.Memory.Behaviors>10,"Semilla determinista y variedad",log);
                Check(a.Memory.Energy<original && a.Memory.ActiveSeconds==330,"Evolución y tiempo activo",log);
                a.Mode=Mode.Concentrado;
                first.ModeChanged(); first.Tick(1,0,true,true);
                Check(first.Current==Expression.Concentración,"Concentrado",log);
                a.Mode=Mode.Interactivo;
                first.ModeChanged(); first.Tick(1,0,true,true);
                Check(first.Current==Expression.Escucha,"Interactivo",log);
                first.Cue(Expression.Sorpresa,4,8); first.Cue(Expression.Picardía,4,1);
                first.Tick(1,0,true,true);
                Check(first.Current==Expression.Sorpresa,"Prioridad de expresiones",log);
                a.Mode=Mode.Descanso;
                first.ModeChanged(); first.Tick(1,0,false,true);
                Check(first.Current==Expression.Sueño || first.Current==Expression.Descanso,"Descanso",log);
                a.Mode=Mode.Libre;
                first.Tick(1,400,false,true); first.Tick(1,0,false,true);
                Check(first.Current==Expression.Saludo,"Saludo al regresar",log);
                var instant=DateTime.UtcNow;
                first.Interact(instant,false);
                Check(a.Memory.Interactions==1 && a.Memory.LastInteraction==instant && a.Memory.Familiarity>0,"Memoria de interacción",log);
                var expression=new BaliExpressionEngine(8);
                expression.Tick(.02,Expression.Sorpresa,1,0,true,false);
                Check(expression.Pose[0]>1 && expression.Pose[0]<1.36 && expression.X>0 && expression.X<1,"Interpolación y seguimiento",log);
                var signatures=Enum.GetValues(typeof(Expression)).Cast<Expression>().Select(e=>
                {
                    var renderer=new BaliExpressionEngine(8);
                    renderer.Tick(2,e,0,0,false,false);
                    return String.Join("/",renderer.Pose.Select(x=>x.ToString("F3")));
                }).Distinct().Count();
                Check(signatures==15,"Quince poses diferenciadas",log);
                expression.Tick(3.08,Expression.Neutral,0,0,false,false);
                Check(expression.Blink<.4,"Parpadeo",log);
                var store=new Store(Path.Combine(folder,"unit-state"));
                a.Size=275; a.X=432; a.Y=197;
                store.Save(a); store.Save(a);
                var loaded=store.Load();
                Check(loaded.Memory.Interactions==1 && loaded.X==432 && loaded.Y==197 && loaded.Size==275 && loaded.Mode==Mode.Libre,"Persistencia y reemplazo",log);
                Check(loaded.Appearance==FaceStyle.Minimalista,"Estilo inicial minimalista",log);
                string stateFile=Path.Combine(store.Folder,"estado.json");
                File.WriteAllText(stateFile,File.ReadAllText(stateFile).Replace("\"Appearance\":0,",""));
                var legacy=store.Load();
                Check(legacy.Appearance==FaceStyle.Minimalista && legacy.Memory.Interactions==1 && legacy.Size==275,"Migración de perfil 1.0",log);
                var shared=new BaliExpressionEngine(17);
                var face=new Face(shared,legacy.Appearance);
                foreach (var style in new[] { FaceStyle.Clásico, FaceStyle.Minimalista })
                {
                    var before=Render(face);
                    face.StyleVariant=legacy.Appearance=style;
                    var after=Render(face);
                    Check(!before.SequenceEqual(after),"Cambio visual inmediato: "+style,log);
                    store.Save(legacy);
                    var persisted=store.Load();
                    Check(persisted.Appearance==style && persisted.Memory.Interactions==1 && persisted.X==432 && persisted.Mode==Mode.Libre,
                        "Persistencia sin perder el perfil: "+style,log);
                }
                store.Reset();
                Check(store.Load().Memory.Interactions==0,"Eliminación de memoria",log);
                var provider=new LocalConversationProvider();
                Check(LocalConversationProvider.Commands.Select(c=>provider.Respond(c,a.Memory).Intent).Distinct().Count()==8 &&
                    provider.Respond("Bali, ¿cómo estás?",a.Memory).Intent==Intent.Status,"Ocho comandos de texto",log);
                CheckAdaptive(folder,log);
                CheckCognitive(folder,log);
                CheckVoiceIntelligence(folder,log);
                CheckLipSync(log);
                CheckFloatingVoice(log);
                CheckSpokenQuestions(log);
                CheckDesktopControl(folder,log);
                CheckVoiceAndProactive(log);
                CheckVision(log);
                CheckComputerControl(log);
                CheckPersonal(folder,log);
                CheckNaturalSpeech(folder,log);
                return 0;
            }
            catch (Exception e) { log.WriteLine(e); return 1; }
        }
        static byte[] Render(Face face)
        {
            face.Measure(new Size(220,220));
            face.Arrange(new Rect(0,0,220,220));
            var bitmap=new RenderTargetBitmap(220,220,96,96,PixelFormats.Pbgra32);
            bitmap.Render(face);
            var pixels=new byte[220*220*4];
            bitmap.CopyPixels(pixels,220*4,0);
            return pixels;
        }
        public static int Probe(string folder)
        {
            using (var log=new StreamWriter(Path.Combine(folder,"host.txt")))
            {
                foreach (var screen in Forms.Screen.AllScreens) log.WriteLine(screen.Bounds+" área="+screen.WorkingArea);
                var info=new Microsoft.VisualBasic.Devices.ComputerInfo();
                log.WriteLine("RAM física: "+(info.TotalPhysicalMemory/1048576)+" MiB");
                var recognizers=SpeechRecognitionEngine.InstalledRecognizers();
                if (recognizers.Count==0) log.WriteLine("Reconocimiento: ningún motor visible en esta sesión");
                foreach (var recognizer in recognizers) log.WriteLine("Reconocimiento: "+recognizer.Culture.Name);
                using (var synth=new SpeechSynthesizer())
                {
                    try
                    {
                        foreach(var voice in synth.GetInstalledVoices()) log.WriteLine("Voz: "+voice.VoiceInfo.Name+" "+voice.VoiceInfo.Culture.Name);
                        synth.SelectVoice(synth.GetInstalledVoices().First(v=>v.VoiceInfo.Culture.TwoLetterISOLanguageName=="es").VoiceInfo.Name);
                        var wave=Path.Combine(folder,"voz-local.wav");
                        synth.SetOutputToWaveFile(wave);
                        synth.Speak("Hola, soy Bali.");
                        synth.SetOutputToNull();
                        log.WriteLine("Síntesis WAV: "+new FileInfo(wave).Length+" bytes");
                        var spanish=SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r=>r.Culture.TwoLetterISOLanguageName=="es");
                        if (spanish!=null)
                        {
                            using (var recognition=new SpeechRecognitionEngine(spanish))
                            {
                                recognition.LoadGrammar(new Grammar(new GrammarBuilder("hola soy Bali") { Culture=spanish.Culture }));
                                recognition.SetInputToWaveFile(wave);
                                var result=recognition.Recognize();
                                log.WriteLine("Reconocimiento WAV: "+(result==null?"sin resultado":result.Text+" / "+result.Confidence));
                            }
                        }
                    }
                    catch (Exception e) { log.WriteLine(e); }
                }
            }
            return 0;
        }
    }
    sealed partial class BaliApp
    {
        void ValidateTick(double now)
        {
            if(personalValidation) { PersonalValidationTick(now); return; }
            if(floatingValidation) { FloatingValidationTick(now); return; }
            if(lipValidation) { LipValidationTick(now); return; }
            if(voiceValidation) { VoiceValidationTick(now); return; }
            if (agentValidation)
            {
                agentRate=60;
                if (now<3) return;
                var report=agentWorld.Report(agentScenario);
                var positions=agentCards.Select(c=>c.Face.TranslatePoint(new Point(0,0),panel)).ToArray();
                bool sameRow=positions.Length==3 && positions.Max(p=>p.Y)-positions.Min(p=>p.Y)<10 &&
                    positions.Max(p=>p.X)+132<panel.ActualWidth && positions.Max(p=>p.Y)+84<panel.ActualHeight;
                bool valid=agentCards.Count==3 && report.Events.Count>0 && settings.Memory.Interactions==0 &&
                    settings.Memory.Cognitive.Recent.Count==0 && sameRow && agentCards.All(c=>c.Face.IsVisible);
                Capture(panel,"agentes.png");
                WriteResult((valid?"PASS":"FAIL")+" Agentes WPF: "+agentCards.Count+" · encuentros="+report.Events.Count+
                    " · misma fila="+sameRow+" · interacciones del perfil principal="+settings.Memory.Interactions);
                foreach(var social in report.Events.Take(6)) WriteResult(social.From+" → "+social.To+" · "+social.Action+" · "+social.Response);
                Save();
                agentValidation=false;
                Shutdown(valid?0:1);
                return;
            }
            int phase=(int)(now/validationSeconds);
            if (phase==testPhase && testFrames++>0) return;
            if (phase==0)
            {
                if (restartTest)
                {
                    var p=Desktop.Position(handle);
                    bool restored=settings.Mode==Mode.Interactivo && settings.Memory.Interactions==1 && p.X==settings.X && p.Y==settings.Y;
                    WriteResult((restored?"PASS":"FAIL")+" Reinicio: "+settings.Mode+", interacciones="+settings.Memory.Interactions+", posición="+p);
                    Shutdown(restored?0:1);
                    return;
                }
                using(var p=System.Diagnostics.Process.GetCurrentProcess())
                    WriteResult(String.Format("Inicio: HWND={0}, visible={1}, bandeja={2}, área={3}, posición={4}, PID={5}",
                        handle,pet.IsVisible,tray.Visible,Forms.Screen.FromHandle(handle).WorkingArea,Desktop.Position(handle),p.Id));
                OpenPanel(LifeTab);
                Capture(panel,"laboratorio.png");
                panel.Close();
                phaseClock=now;
                phaseCpu=CurrentCpuTime();
                return;
            }
            double total=CurrentCpuTime();
            double average=100*(total-phaseCpu)/(now-phaseClock)/Environment.ProcessorCount;
            phaseCpu=total; phaseClock=now;
            WriteResult(String.Format("{0}: CPU media {1:0.00} % por fase de {2:0} s, RAM {3} MiB, visible={4}, comportamiento={5}, decisión={6}",
                testPhase==4?"Oculto":settings.Mode.ToString(),average,validationSeconds,currentRam,pet.IsVisible,life.Current,life.Profile.LastDecision));
            testPhase=phase;
            switch(phase)
            {
                case 1:
                    OpenPanel(5);
                    Capture(panel,"configuracion.png"); Capture(face,"rostro.png");
                    panel.Close(); ChangeMode(Mode.Concentrado); break;
                case 2: ChangeMode(Mode.Descanso); break;
                case 3:
                    ChangeMode(Mode.Interactivo);
                    OpenPanel();
                    Respond("Bali, ¿cómo estás?");
                    WriteResult("Respuesta: "+response.Text);
                    Capture(panel,"conversacion.png");
                    panel.Close(); break;
                case 4: HidePet(); break;
                case 5: Save(); WriteResult("Guardado y cierre solicitado."); Shutdown(); break;
            }
        }
        static double CurrentCpuTime()
        {
            using (var process=System.Diagnostics.Process.GetCurrentProcess()) return process.TotalProcessorTime.TotalSeconds;
        }
        void WriteResult(string value) { File.AppendAllText(Path.Combine(testFolder,"runtime.txt"),value+Environment.NewLine); }
        void Capture(FrameworkElement element,string name)
        {
            try
            {
                element.UpdateLayout();
                var target=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
                target.Render(element);
                var encoder=new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(target));
                using(var stream=File.Create(Path.Combine(testFolder,name))) encoder.Save(stream);
            }
            catch(Exception e) { WriteResult("Captura: "+e.Message); }
        }
    }
}

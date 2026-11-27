using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void CheckDesktopControl(string folder,TextWriter log)
        {
            var now=DateTime.UtcNow;
            var work=Path.Combine(folder,"desktop-"+Guid.NewGuid().ToString("N"));
            var root=Path.Combine(work,"synthetic-temp");Directory.CreateDirectory(root);
            var other=Directory.CreateDirectory(Path.Combine(work,"protected")).FullName;
            var empty=new TechnicalWord[0];
            Check(TechnicalSpeech.Normalize("Qué es una uve pe ene",empty)=="Qué es una VPN" &&
                TechnicalSpeech.Normalize("Cómo uso hache te te pe ese y yeison",empty)=="Cómo uso HTTPS y JSON","Normaliza siglas técnicas dentro de una pregunta",log);
            Check(TechnicalSpeech.Normalize("que es una uve bene.",empty)=="que es una VPN." &&
                TechnicalSpeech.Normalize("cómo escribir uve bene en mi texto",empty)=="cómo escribir uve bene en mi texto",
                "Recupera la variante observada de VPN únicamente en una pregunta compatible",log);
            Check(TechnicalSpeech.IsAmbiguousVpn("¿Qué es una WPN?") &&
                TechnicalSpeech.Normalize("¿Qué es una WPN?",empty)=="¿Qué es una VPN?" &&
                TechnicalSpeech.Normalize("¿Qué es una red privada virtual?",empty)=="¿Qué es una VPN?" &&
                !TechnicalSpeech.IsAmbiguousVpn("abre WPN") && TechnicalSpeech.Normalize("abre WPN",empty)=="abre WPN",
                "Recupera WPN y red privada virtual con contexto de pregunta",log);
            Check(TechnicalSpeech.Normalize("Bali, abre el editor de Microsoft.",empty)=="Bali, abre Word." &&
                TechnicalSpeech.Normalize("Abre guord",empty)=="Abre Word" &&
                TechnicalSpeech.Normalize("Abre Excel",empty)=="Abre Excel" &&
                TechnicalSpeech.Normalize("Qué es Microsoft Word",empty)=="Qué es Microsoft Word",
                "Las variantes orales de Microsoft Word solo se aplican al comando de apertura",log);
            Check(TechnicalSpeech.Normalize("que usa visual estudio code",empty)=="que usa Visual Studio Code" &&
                TechnicalSpeech.Normalize("una caja de ramitas",empty)=="una caja de ramitas","Normalización respeta límites de palabras",log);
            var custom=new[]{new TechnicalWord{Spoken="mi editor",Written="Obsidian"}};
            Check(TechnicalSpeech.Normalize("abre mi editor",custom)=="abre Obsidian","Diccionario técnico configurable",log);
            var editor=new DesktopApp{Id="a",Name="Visual Studio Professional"};
            var code=new DesktopApp{Id="b",Name="Visual Studio Code"};
            var second=new DesktopApp{Id="c",Name="Visual Studio Professional"};
            Check(DesktopCatalog.Match(new[]{editor,code,second},"Visual Studio").Length==2 &&
                DesktopCatalog.Match(new[]{code},"Visual Studio").Length==0,"Distingue Visual Studio de Code y conserva instalaciones ambiguas",log);
            Check(DesktopCatalog.Match(new[]{new DesktopApp{Name="Obsidian"}},"Obsidiam").Length==1 &&
                !DesktopCatalog.Exact(editor,"Visual Studio"),"Identifica aproximaciones que necesitan confirmación",log);
            string eligible=TempFixture(root,"~DF012345.tmp",now);
            string locked=TempFixture(root,"tmpABCD.tmp",now);
            string outside=TempFixture(other,"tmpABCD.tmp",now);
            string ignored=TempFixture(root,"personal.tmp",now);
            using(var lease=File.Open(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                var plan=DesktopMaintenance.Analyze(root,now,CancellationToken.None);
                Check(plan.Files.Length==1 && plan.Files[0].Path==eligible && File.Exists(eligible),"Análisis excluye archivos ajenos o bloqueados y no modifica candidatos",log);
                Check(DesktopMaintenance.Clean(plan,now.AddMinutes(2),CancellationToken.None).Changed && File.Exists(eligible),"Confirmación vencida no elimina",log);
                File.AppendAllText(eligible,"texto nuevo");
                Check(DesktopMaintenance.Clean(plan,now,CancellationToken.None).Changed && File.Exists(eligible),"Un cambio obliga a analizar de nuevo",log);
                var changed=TempFixture(root,"~DF012345.tmp",now);
                plan=DesktopMaintenance.Analyze(root,now,CancellationToken.None);
                using(var stopped=new CancellationTokenSource())
                {
                    stopped.Cancel();
                    bool cancelled=false;
                    try{DesktopMaintenance.Clean(plan,now,stopped.Token);}catch(OperationCanceledException){cancelled=true;}
                    Check(cancelled && File.Exists(changed),"Cancelación conserva los candidatos",log);
                }
                var clean=DesktopMaintenance.Clean(plan,now,CancellationToken.None);
                Check(clean.Deleted==1 && !File.Exists(eligible) && File.Exists(outside) && File.Exists(locked) && File.Exists(ignored),
                    "Eliminación sintética conserva vecinos, exclusiones y archivos bloqueados",log);
            }
            Check(!DesktopPaths.Local(@"\\network\test.exe") && !DesktopPaths.Executable(Path.Combine(root,"missing.exe")) &&
                !DesktopCatalog.ValidId("package!Start & cmd"),"Rutas inválidas, de red o identificadores no permitidos",log);
            var settingsPath=Path.Combine(work,"desktop.json");
            var config=new DesktopConfig();
            config.Words.Add(custom[0]);config.Permissions.Add(new DesktopPermission{Id="test",Excluded=true});
            config.Routines.Add(new DesktopRoutine{Name="Desarrollo",Steps={new DesktopStep{Kind=DesktopStepKind.Agenda}}});
            LocalJson.Write(settingsPath,config);
            var copy=LocalJson.Read<DesktopConfig>(settingsPath);
            Check(copy.Words[0].Written=="Obsidian" && copy.Permissions[0].Excluded && copy.Routines[0].Steps[0].Kind==DesktopStepKind.Agenda,
                "Persiste diccionario, exclusiones y pasos tipados",log);
            Check(DesktopCatalog.Launch(new DesktopApp{Path=outside},null).Contains("autorización") &&
                DesktopCatalog.Launch(new DesktopApp{Path=outside},new DesktopPermission{Excluded=true}).Contains("autorización"),
                "Un ejecutable descubierto o excluido requiere autorización",log);
            Check(DesktopCatalog.CanAuthorizeByVoice(new DesktopApp{Id="instalada"},null) &&
                !DesktopCatalog.CanAuthorizeByVoice(new DesktopApp{Id="portable",Root=root},null) &&
                !DesktopCatalog.CanAuthorizeByVoice(new DesktopApp{Id="excluida"},new DesktopPermission{Excluded=true}),
                "Solo una aplicación instalada y sin permiso previo puede pedir autorización por voz",log);
            Check(SpokenQuestion.DesktopAnswer("¡Sí, autorizo!") == SpokenReply.Yes &&
                SpokenQuestion.DesktopAnswer("autorizo la aplicación")==SpokenReply.Yes &&
                SpokenQuestion.DesktopAnswer("sí pero no abras")==SpokenReply.Other &&
                SpokenQuestion.DesktopAnswer("no lo autorices")==SpokenReply.No,
                "La autorización de escritorio admite frases claras y rechaza combinaciones ambiguas",log);
            var weakAnswer=new SpeechResult{Text="sí autorizo",Confidence=.64};
            Check(SpokenQuestion.OpeningFallback(weakAnswer,new SpeechResult{Text="Sí, autorizo"},"Brave",true)==SpokenReply.Yes &&
                SpokenQuestion.OpeningFallback(new SpeechResult(),new SpeechResult{Text="Autoriza Brave"},"Brave",true)==SpokenReply.Yes &&
                SpokenQuestion.OpeningFallback(new SpeechResult{Text="no"},new SpeechResult{Text="sí"},"Brave",true)==SpokenReply.No &&
                SpokenQuestion.OpeningFallback(new SpeechResult(),new SpeechResult{Text="Autoriza Word"},"Brave",true)==SpokenReply.Other &&
                SpokenQuestion.OpeningFallback(new SpeechResult(),new SpeechResult{Text="Sí"},"Brave",true)==SpokenReply.Yes &&
                SpokenQuestion.OpeningFallback(new SpeechResult(),new SpeechResult{Text="Sí"},"Brave",false)==SpokenReply.Other &&
                SpokenQuestion.OpeningFallback(new SpeechResult(),new SpeechResult{Text="Sí"},null,true)==SpokenReply.Other,
                "Una apertura pendiente admite sí o no sin ampliar el alcance a otras operaciones",log);
            var optIn=new DesktopPermission{OpenByExactVoice=true};
            Check(DesktopCatalog.CanOpenByExactVoice(optIn,new SpeechResult{Text="abre Brave"},true) &&
                !DesktopCatalog.CanOpenByExactVoice(optIn,new SpeechResult{Text="abre Brabe"},false) &&
                !DesktopCatalog.CanOpenByExactVoice(optIn,new SpeechResult{Text="abre Brave",Rejected=1},true) &&
                !DesktopCatalog.CanOpenByExactVoice(new DesktopPermission(),new SpeechResult{Text="abre Brave"},true),
                "La apertura sin otra confirmación exige permiso explícito y nombre exacto",log);
            var notepad=new DesktopApp{Id="Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"};
            Check(DesktopCatalog.IsPackagedAlias(notepad,new DesktopApp{Path=@"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2607.14.0_x64__8wekyb3d8bbwe\Notepad\Notepad.exe"}) &&
                !DesktopCatalog.IsPackagedAlias(notepad,new DesktopApp{Path=@"C:\Apps\Notepad.exe"}) &&
                !DesktopCatalog.IsPackagedAlias(notepad,new DesktopApp{Path=@"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2607.14.0_x64__OTHER\Notepad\Notepad.exe"}),
                "Vincula la ruta duplicada con el identificador de la misma familia empaquetada",log);
        }
        static string TempFixture(string root,string name,DateTime now)
        {
            var path=Path.Combine(root,name);File.WriteAllText(path,"sintético");
            File.SetCreationTimeUtc(path,now.AddDays(-30));
            File.SetLastWriteTimeUtc(path,now.AddDays(-30));
            File.SetLastAccessTimeUtc(path,now.AddDays(-30));
            return path;
        }
        public static int DesktopEvidence(string folder)
        {
            Directory.CreateDirectory(folder);
            using(var log=new StreamWriter(Path.Combine(folder,"desktop.txt")))
            {
                var catalog=MeasureDesktop(log,"Indexación de Windows",()=>DesktopCatalog.Scan(new string[0],CancellationToken.None));
                log.WriteLine("Aplicaciones detectadas: "+catalog.Length);
                foreach(var app in catalog)log.WriteLine(app.Name+" · "+app.Origin);
                var plan=MeasureDesktop(log,"Análisis real solo lectura",()=>DesktopMaintenance.Analyze(DesktopMaintenance.UserRoot(),DateTime.UtcNow,CancellationToken.None));
                log.WriteLine("Candidatos: "+plan.Files.Length+" · "+DesktopMetrics.Size(plan.Bytes)+" · eliminados: 0");
                foreach(var target in new[]{"ram","disco","cpu","procesos-ram","actividad","batería","lento"})
                    log.WriteLine(MeasureDesktop(log,"Métrica "+target,()=>DesktopMetrics.Read(target,CancellationToken.None)));
                bool ok=catalog.Length>0;
                var link=Path.Combine(folder,"junction");
                if(Directory.Exists(link))
                {
                    bool rejected=!DesktopPaths.Local(link) && !DesktopPaths.Files(link,"*.tmp",0,CancellationToken.None).Any() &&
                        DesktopMaintenance.Analyze(link,DateTime.UtcNow,CancellationToken.None).Files.Length==0;
                    log.WriteLine((rejected?"PASS":"FAIL")+" Punto de reanálisis: no se recorrió la junction.");ok&=rejected;
                }
                var fixture=Path.Combine(folder,"sample-window.exe");
                if(File.Exists(fixture))
                {
                    var portable=DesktopCatalog.Scan(new[]{folder},CancellationToken.None).FirstOrDefault(x=>x.Path==fixture && x.Portable);
                    bool opened=portable!=null && DesktopCatalog.Launch(portable,null).Contains("autorización");
                    if(opened)
                    {
                        var permission=new DesktopPermission{Id=portable.Id,Hash=DesktopCatalog.Fingerprint(fixture)};
                        opened=DesktopCatalog.Launch(portable,permission).Contains("inició");
                        var until=DateTime.UtcNow.AddSeconds(8);
                        while(DesktopCatalog.Running(portable).Length==0 && DateTime.UtcNow<until)Thread.Sleep(100);
                        var targets=DesktopCatalog.Running(portable).Select(BaliApp.GetWindowIdentity).Where(x=>x!=null).ToArray();
                        opened&=targets.Length>0;
                        var close=BaliApp.CloseDesktopWindows(portable,targets,CancellationToken.None);
                        log.WriteLine(close);
                        until=DateTime.UtcNow.AddSeconds(4);
                        while(DesktopCatalog.Running(portable).Length>0 && DateTime.UtcNow<until)Thread.Sleep(100);
                        opened&=DesktopCatalog.Running(portable).Length==0 && (close.Contains("confirmados: 1") || close.Contains("guardado: 1"));
                    }
                    log.WriteLine((opened?"PASS":"FAIL")+" Portátil de prueba: catálogo, aprobación, apertura y cierre normal.");ok&=opened;
                }
                return ok?0:1;
            }
        }
        static T MeasureDesktop<T>(TextWriter log,string operation,Func<T> action)
        {
            using(var process=Process.GetCurrentProcess())
            {
                var start=process.TotalProcessorTime;
                var elapsed=Stopwatch.StartNew();var value=action();
                process.Refresh();
                log.WriteLine(String.Format("{0}: {1:0.000} s · CPU {2:0.000} s · RAM {3:0.0} MiB",operation,
                    elapsed.Elapsed.TotalSeconds,(process.TotalProcessorTime-start).TotalSeconds,process.WorkingSet64/1048576.0));
                return value;
            }
        }
    }
}

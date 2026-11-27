using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void CheckAdaptive(string folder,TextWriter log)
        {
            var stamp=new DateTime(2025,1,1,12,0,0,DateTimeKind.Utc);
            var settings=new Settings();
            var engine=new BaliLifeEngine(settings,23);
            engine.Begin(stamp,true);
            engine.Tick(1,0,false,true,stamp);
            var profile=engine.Profile;
            Check(profile.OriginalUnknown && profile.AgeDays(stamp.AddDays(7))==7 && profile.Days==1,"Fecha inicial fiable y edad digital",log);
            double social=profile.Traits.Social;
            double satisfaction=profile.Satisfaction;
            engine.Interact(stamp,false);
            Check(profile.Satisfaction>satisfaction && profile.Traits.Social==social && profile.InteractionNeed<.25,"Estados temporales y rasgos separados",log);
            for(int i=1;i<=120;i++) engine.Tick(1,0,true,true,stamp.AddSeconds(i));
            Check(profile.Decisions>0 && engine.Candidates.Length>3 && profile.LastReason.Length>0,"Selección de puntuaciones con motivo",log);
            Check(profile.Hours.Sum(h=>h.Samples)==2,"Muestreo estadístico por minuto",log);
            bool cooldown=false;
            for(int i=121;i<240;i++)
            {
                engine.Tick(1,0,i%2==0,true,stamp.AddSeconds(i));
                cooldown |= engine.Candidates.Any(c=>c.Score<0 && c.Reason.Contains("cooldown"));
            }
            Check(cooldown,"Cooldown y penalización de repetición",log);
            int autonomous=profile.Decisions;
            settings.Mode=Mode.Concentrado;
            engine.ModeChanged();
            for(int i=240;i<280;i++) engine.Tick(1,0,true,true,stamp.AddSeconds(i));
            Check(profile.Decisions==autonomous && engine.Current==Expression.Concentración,"Concentrado inhibe iniciativa",log);
            settings.Mode=Mode.Libre;
            settings.Autonomy=0;
            engine.ModeChanged();
            for(int i=280;i<320;i++) engine.Tick(1,0,true,true,stamp.AddSeconds(i));
            Check(profile.Decisions==autonomous && !engine.MoveRequested,"Autonomía desactivada",log);
            var runtime=profile.RuntimeSeconds;
            settings.Memory.Energy=.05;
            engine.Tick(0,0,false,true,stamp.AddDays(6));
            Check(profile.Days==2 && profile.RuntimeSeconds==runtime && settings.Memory.Energy>.75,"Reanudación sin imputar suspensión ni días ausentes",log);
            int learned=profile.Hours.Sum(h=>h.Samples);
            settings.Learning=false;
            engine.Tick(60,0,false,true,stamp.AddDays(6).AddMinutes(1));
            Check(profile.Hours.Sum(h=>h.Samples)==learned && engine.CircadianBias==0,"Aprendizaje desactivado",log);
            int episodeCount=profile.Episodes.Count;
            engine.VoiceRecognized(stamp.AddDays(6));
            engine.VoiceRecognized(stamp.AddDays(6));
            Check(profile.Episodes.Count==episodeCount+1 && profile.VoiceMilestone,"Hito de voz único ante evento explícito",log);
            for(int i=0;i<150;i++) profile.Record("Prueba","Evento "+i,1,stamp.AddDays(6));
            Check(profile.Episodes.Count==80 && profile.Episodes.Any(e=>e.Kind=="Migración"),"Retención limitada con prioridad",log);
            profile.Record("Prueba","Repetido",1,stamp.AddDays(6));
            profile.Record("Prueba","Repetido",1,stamp.AddDays(6).AddMinutes(2));
            Check(profile.Episodes.Last().Count==2,"Consolidación episódica",log);
            var provider=new LocalConversationProvider();
            Check(LocalConversationProvider.ContextCommands.All(c=>provider.Respond(c,settings.Memory).Intent==Intent.Status) &&
                provider.Respond("Bali, ¿cuántos días tienes?",settings.Memory).Text.Contains("desconocida"),"Respuestas contextuales sobre datos registrados",log);

            var store=new Store(Path.Combine(folder,"adaptive-state"));
            Directory.CreateDirectory(store.Folder);
            store.Reset();
            string main=Path.Combine(store.Folder,"estado.json");
            const string legacy="{\"Mode\":1,\"Memory\":{\"Interactions\":22,\"Familiarity\":0.38}}";
            File.WriteAllText(main,legacy);
            var migrated=store.Load();
            Check(migrated.Memory.Interactions==22 && migrated.Memory.Familiarity==.38 && migrated.Mode==Mode.Concentrado &&
                migrated.Memory.Adaptive.OriginalUnknown,"Lectura del perfil 1.0",log);
            new BaliLifeEngine(migrated,1).Begin(stamp,true);
            store.Save(migrated);
            Check(File.ReadAllText(Path.Combine(store.Folder,"estado.1.0.json"))==legacy && store.Load().Memory.Adaptive.KnownSince==stamp,
                "Copia exacta previa y guardado del esquema nuevo",log);
            store.Save(migrated);
            File.WriteAllText(main,"{cortado");
            var restored=store.Load();
            store.Save(restored);
            Check(restored.Memory.Interactions==22 && restored.Memory.Adaptive.KnownSince==stamp && store.Load().Schema==3,
                "Recuperación del estado anterior ante corrupción",log);
            store.Reset();

            string report=Path.Combine(folder,"simulaciones-1.1.txt");
            using(var output=new StreamWriter(report))
            {
                foreach(int duration in new[]{7,30})
                {
                    var socialProfile=LifeSimulator.Run(duration,71,32,9,false,false,false,.7);
                    var quietProfile=LifeSimulator.Run(duration,71,1,19,true,true,true,.2);
                    var repeat=LifeSimulator.Run(duration,71,32,9,false,false,false,.7);
                    var a=socialProfile.Settings.Memory.Adaptive;
                    var b=quietProfile.Settings.Memory.Adaptive;
                    output.WriteLine(duration+" días · semilla 71 · perfil con más contacto\n"+socialProfile);
                    output.WriteLine(duration+" días · semilla 71 · perfil tranquilo, concentración y ausencia\n"+quietProfile);
                    Check(socialProfile.ToString()==repeat.ToString(),"Repetibilidad "+duration+" días",log);
                    Check(a.Traits.Social>b.Traits.Social+.04 && a.Traits.Active>b.Traits.Active+.035 &&
                        a.Traits.Values().Concat(b.Traits.Values()).All(v=>v>=.25 && v<=.80),"Diferenciación estable "+duration+" días",log);
                    Check(a.Hours.Max(h=>h.ActiveBias)>.4 && a.Hours.Min(h=>h.ActiveBias)<-.4 &&
                        b.RuntimeSeconds<a.RuntimeSeconds && b.Days<duration,"Ritmo observado y ausencia "+duration+" días",log);
                }
            }
            Check(settings.Memory.Interactions==1 && settings.Memory.Adaptive==profile,"Simulador aislado del perfil activo",log);
            engine.ResetLearning();
            Check(profile.Hours.Sum(h=>h.Samples)==0 && profile.Traits.Social==.5 && settings.Memory.Interactions==1 &&
                profile.VoiceMilestone,"Restablecer aprendizaje conserva interacciones e hitos",log);
        }
    }
}

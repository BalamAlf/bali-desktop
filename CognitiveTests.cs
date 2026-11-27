using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void CheckCognitive(string folder,TextWriter output)
        {
            var stamp=new DateTime(2025,1,1,12,0,0,DateTimeKind.Utc);
            const string context="Cerca · energía suficiente";
            var stats=new CognitiveMemory();
            var learner=new BaliLearningEngine(stats);
            var unknown=learner.Predict("Explorar",context,stamp,true);
            Check(unknown.Confidence==0 && unknown.ContactProbability==null && unknown.Exploration>0,"Predicción incierta y exploración acotada sin muestras",output);
            learner.Begin("Explorar",context,stamp);
            Check(!learner.Contact(stamp.AddSeconds(.3),true) && !learner.Contact(stamp.AddSeconds(2),false),"Atribución temporal y contacto directo",output);
            learner.Expire(stamp.AddSeconds(19));
            Check(stats.Recent.Last().Result==Outcome.SinRespuesta && stats.TotalReward==0 &&
                learner.Predict("Explorar",context,stamp,false).Confidence==0,"Ausencia de respuesta neutral",output);
            learner.Begin("Explorar","Activo a distancia · energía suficiente",stamp.AddSeconds(20));
            Check(!learner.Contact(stamp.AddSeconds(22),true),"Contacto distante no atribuido a exploración",output);
            learner.Interrupt(stamp.AddSeconds(22));
            learner.Begin("Invitar",context,stamp.AddSeconds(23));
            learner.Begin("Microexpresión",context,stamp.AddSeconds(25));
            Check(stats.Results[(int)Outcome.Interrumpida]==1 && stats.Results[(int)Outcome.Desconocida]==1 &&
                stats.TotalReward==0,"Interrupción y sustitución diferenciadas",output);
            Check(learner.Contact(stamp.AddSeconds(26),true) && Math.Abs(stats.TotalReward-.28)<.001,"Contacto posterior con recompensa débil",output);
            for(int i=0;i<18;i++)
            {
                var t=stamp.AddMinutes(i+2);
                learner.Begin("Explorar",context,t);
                learner.Feedback(Outcome.Negativa,t.AddSeconds(1));
            }
            var negative=learner.Predict("Explorar",context,stamp.AddMinutes(25),true);
            Check(negative.Reward<-.65 && negative.Confidence>.6 && negative.Exploration<unknown.Exploration,"Retroalimentación negativa con evidencia gradual",output);
            for(int i=0;i<40;i++)
            {
                var t=stamp.AddMinutes(i+30);
                learner.Begin("Explorar",context,t);
                learner.Feedback(Outcome.Positiva,t.AddSeconds(1));
            }
            var positive=learner.Predict("Explorar",context,stamp.AddMinutes(75),true);
            var decayed=learner.Predict("Explorar",context,stamp.AddDays(84),true);
            Check(positive.Reward>.68 && positive.Reward<=.9 && decayed.Confidence<positive.Confidence,"Aprendizaje contradictorio, límites y decaimiento",output);
            Check(stats.Recent.Count==36 && stats.Associations.Count<=96,"Memoria asociativa acotada",output);
            for(int i=0;i<9;i++)
            {
                var t=stamp.AddHours(2).AddMinutes(i);
                learner.Begin("Invitar",context,t);
                if(i%2==0) learner.Contact(t.AddSeconds(2),true);
                else learner.Expire(t.AddSeconds(19));
            }
            var contact=learner.Predict("Invitar",context,stamp.AddHours(3),false);
            Check(contact.ContactProbability.HasValue && contact.ContactProbability.Value>.3 &&
                contact.ContactProbability.Value<.7,"Probabilidad basada en contactos observables",output);
            learner.Begin("Invitar",context,stamp.AddHours(3));
            learner.Feedback(Outcome.MenosFrecuente,stamp.AddHours(3).AddSeconds(1));
            learner.Begin("Invitar",context,stamp.AddHours(3).AddMinutes(1));
            learner.Feedback(Outcome.MásFrecuente,stamp.AddHours(3).AddMinutes(1).AddSeconds(1));
            Check(stats.Recent.Last().Reward==.55 && stats.Results[(int)Outcome.MenosFrecuente]==1,"Preferencias explícitas de frecuencia",output);

            var s=new Settings();
            s.Memory.Cognitive=stats;
            var life=new BaliLifeEngine(s,33);
            for(int i=0;i<12;i++) life.Tick(1,0,true,true,stamp.AddDays(1).AddSeconds(i));
            Check(life.Goal!=null && life.Goal.Deadline>12 && life.Candidates.Any(c=>c.Motivation!=0) &&
                life.Candidates.Where(c=>c.Score>=0).All(c=>Math.Abs(c.Learned)<=.27),"Objetivos y contribuciones acotadas en Utility AI",output);
            s.Mode=Mode.Concentrado;
            life.ModeChanged();
            for(int i=12;i<80;i++) life.Tick(1,0,true,true,stamp.AddDays(1).AddSeconds(i));
            Check(life.Current==Expression.Concentración && life.PendingInitiative==null && !life.MoveRequested &&
                life.Profile.LastDecision=="Calma concentrada","Concentrado domina las preferencias aprendidas",output);
            life.ConsequenceEnabled(false);
            Check(!life.Feedback(Outcome.Positiva,stamp.AddDays(1)) && stats.TotalReward>0,"Desactivar consecuencias conserva asociaciones",output);
            s.Memory.Interactions=23;
            s.Memory.Adaptive.KnownSince=stamp;
            var store=new Store(Path.Combine(folder,"cognitive-state"));
            Directory.CreateDirectory(store.Folder);
            store.Reset();
            var path=Path.Combine(store.Folder,"estado.json");
            // Emit a full legacy shape using current serialization, then remove only the cognitive extension.
            store.Save(s);
            string current=File.ReadAllText(path);
            string previous=current.Replace("\"Schema\":3","\"Schema\":1");
            File.WriteAllText(path,previous);
            var migrated=store.Load();
            Check(migrated.Memory.Interactions==23 && migrated.Memory.Adaptive.KnownSince==stamp && migrated.Memory.Cognitive.Recent.Count==0,
                "Migración de esquema 1.1 conserva perfil adaptativo",output);
            store.Save(migrated);
            Check(File.ReadAllText(Path.Combine(store.Folder,"estado.1.1.json"))==previous,"Copia byte a byte previa a Cognitive Life",output);
            store.Save(s);
            Check(store.Load().Memory.Cognitive.Recent.Count==36 && !store.Load().ConsequenceLearning,"Persistencia de asociaciones y preferencia de aprendizaje",output);
            life.ResetConsequences();
            Check(stats.Associations.Count==0 && s.Memory.Interactions==23 && s.Memory.Adaptive.KnownSince==stamp,"Restablecer asociaciones conserva identidad",output);
            store.Save(s,true);
            Check(!File.Exists(path+".bak") && store.Load().Memory.Cognitive.Recent.Count==0 &&
                File.Exists(Path.Combine(store.Folder,"estado.1.1.json")),"Restablecimiento limpia el respaldo con asociaciones",output);
            store.Reset();

            var definitions=AgentDefinition.Defaults();
            var world=new AgentWorld(definitions,true);
            var quiet=world.Agents[2];
            quiet.Paused=true;
            for(int i=0;i<220;i++) world.Step(10,EnvironmentPattern.Frecuente,false);
            var live=world.Report(EnvironmentPattern.Frecuente);
            Check(live.Events.Count>0 && world.Agents[0].Social.ContainsKey(world.Agents[1].Id),"Encuentros sociales producidos por agentes",output);
            Check(quiet.Settings.Memory.Adaptive.RuntimeSeconds==0 && quiet.Social.Count==0 &&
                !Object.ReferenceEquals(world.Agents[0].Settings.Memory.Cognitive,world.Agents[1].Settings.Memory.Cognitive),
                "Agente pausado y memorias independientes",output);
            world.Remove(world.Agents[1]);
            Check(world.Agents.Count==2 && world.Agents[0].Social.Count==0 && world.Report(EnvironmentPattern.Frecuente).Events.Count==0,
                "Eliminación retira el perfil y sus relaciones",output);
            var restarted=new AgentWorld(world.Agents.Select(x=>x.Definition),true);
            Check(restarted.Agents.All(x=>x.Social.Count==0 && x.Settings.Memory.Adaptive.RuntimeSeconds==0),
                "Reiniciar conserva definiciones y vacía la historia experimental",output);

            var one=new[]{definitions[0]};
            var baseline=AgentWorld.Experiment(one,30,EnvironmentPattern.Negativa,false);
            var frozen=AgentWorld.Experiment(one,30,EnvironmentPattern.Negativa,true,false);
            var adapted=AgentWorld.Experiment(one,30,EnvironmentPattern.Negativa,true);
            var repeat=AgentWorld.Experiment(one,30,EnvironmentPattern.Negativa,true);
            var cluster=AgentWorld.Experiment(definitions,90,EnvironmentPattern.Cambiante,true);
            var a=adapted.Agents[0]; var b=baseline.Agents[0]; var f=frozen.Agents[0];
            string report=Path.Combine(folder,"experimentos-1.2.txt");
            File.WriteAllText(report,"Semilla 71 · 30 días · escenario Negativa\n\n1.1\n"+baseline.Summary()+
                "\n\n1.2 sin consecuencias (mismas motivaciones)\n"+frozen.Summary()+"\n\n1.2 con consecuencias\n"+adapted.Summary()+
                "\n\n90 días · escenario Cambiante · semillas 71/72/73\n"+cluster.Summary());
            using(var stream=File.Create(Path.Combine(folder,"experimentos-1.2.json")))
                new DataContractJsonSerializer(typeof(AgentReport[])).WriteObject(stream,new[]{baseline,frozen,adapted,cluster});
            Check(adapted.Summary()==repeat.Summary(),"Experimento controlado reproducible",output);
            Check(a.NegativeFeedback<b.NegativeFeedback && a.NegativeFeedback<f.NegativeFeedback &&
                (double)a.NegativeFeedback/a.FeedbackOpportunities<(double)f.NegativeFeedback/f.FeedbackOpportunities,
                "Disminución de iniciativas con negativa explícita en escenario controlado",output);
            Check(a.Decisions.Count>=5 && a.Memory.Cognitive.Associations.All(x=>Math.Abs(x.Value)<=.90),
                "Exploración y estabilidad tras 30 días",output);
            var audited=new[]{b,f,a};
            File.WriteAllLines(Path.Combine(folder,"auditoria-iniciativas-1.3.txt"),audited.Select((x,i)=>
                String.Format("{0}: iniciativas={1}; positivas={2}; negativas={3}; sin señal={4}; sin respuesta registrada={5}; objetivos satisfechos={6}; explorar={7}; observar={8}",
                    new[]{"1.1","1.2 sin consecuencias","1.2 con consecuencias"}[i],x.Memory.Adaptive.Decisions,x.PositiveFeedback,
                    x.NegativeFeedback,x.NoFeedback,x.Memory.Cognitive.Results[(int)Outcome.SinRespuesta],
                    x.Memory.Cognitive.GoalsSatisfied,x.Decisions["Explorar"],x.Decisions["Observar"])));
            Check(a.Memory.Adaptive.Decisions>=f.Memory.Adaptive.Decisions*.95 && a.PositiveFeedback>f.PositiveFeedback &&
                a.NoFeedback>0 && a.Decisions["Explorar"]>0 && a.Decisions["Observar"]>f.Decisions["Observar"],
                "Auditoría de negativas: se conserva el volumen de iniciativas",output);
            Check(cluster.Agents.Count==3 && cluster.Events.Count>0 && cluster.Agents.All(x=>x.Memory.Adaptive.Days>=89 &&
                x.Memory.Adaptive.RuntimeSeconds==90*86400 && x.Memory.Cognitive.Recent.Count<=36 &&
                x.Memory.Cognitive.Associations.All(y=>Math.Abs(y.Value)<=.90) &&
                x.Relationships.Values.All(y=>Math.Abs(y.Affinity)<=1)), "Noventa días multiagente en condiciones cambiantes",output);
            Check(s.Memory.Interactions==23 && s.Memory.Cognitive.Associations.Count==0,"Simulación cognitiva aislada del perfil principal",output);
        }
    }
}

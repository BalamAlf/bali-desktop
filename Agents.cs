using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace BaliDesktop
{
    public enum EnvironmentPattern { Frecuente, Limitada, Positiva, Negativa, Cambiante, Ausencia }

    [DataContract]
    public sealed class AgentDefinition
    {
        [DataMember] public string Name;
        [DataMember] public int Seed;
        [DataMember] public FaceStyle Style;
        [DataMember] public double Social=.5, Curious=.5, Active=.5, Explore=.5, Calm=.5, Autonomy=.55;
        public AgentDefinition Copy() { return (AgentDefinition)MemberwiseClone(); }
        public static AgentDefinition[] Defaults()
        {
            return new[] {
                new AgentDefinition { Name="Bali A · Explorador",Seed=71,Curious=.8,Explore=.8,Active=.63 },
                new AgentDefinition { Name="Bali B · Sociable",Seed=72,Style=FaceStyle.Clásico,Social=.82,Autonomy=.68 },
                new AgentDefinition { Name="Bali C · Reservado",Seed=73,Calm=.83,Active=.35,Explore=.34,Autonomy=.33 }
            };
        }
    }

    [DataContract]
    public sealed class SocialMemory
    {
        [DataMember] public int Encounters, Accepted, Observed, Shared, Withdrawn;
        [DataMember] public string Last;
        public double Confidence { get { return Encounters/(Encounters+8.0); } }
        public double Familiarity { get { return 1-Math.Exp(-Encounters/32.0); } }
        public double Affinity { get { return (Accepted*.7+Shared*.3-Withdrawn)/(Encounters+8.0); } }
    }

    [DataContract]
    public sealed class SocialEvent
    {
        [DataMember] public DateTime Date;
        [DataMember] public string From, To, Action, Response;
    }

    public sealed class Agent
    {
        public readonly AgentDefinition Definition;
        public readonly Settings Settings;
        public readonly BaliLifeEngine Engine;
        public readonly int Id;
        public readonly Dictionary<int,SocialMemory> Social=new Dictionary<int,SocialMemory>();
        public readonly Dictionary<string,int> Decisions=new Dictionary<string,int>();
        internal readonly Random Random, FeedbackRandom;
        internal string LastAction;
        public bool Paused;
        public string Name { get { return Definition.Name; } }
        public string LastSocial="Sin encuentro registrado";
        public int Opportunities, NegativeFeedback, PositiveFeedback, NoFeedback, ContactEvents;
        internal int LastDecisionVersion;

        public Agent(AgentDefinition definition,int id,bool cognitive)
        {
            Id=id;
            Definition=definition.Copy();
            Settings=new Settings { Interest=definition.Curious,Activity=definition.Active,Autonomy=definition.Autonomy,Appearance=definition.Style };
            var t=Settings.Memory.Adaptive.Traits;
            t.Social=definition.Social; t.Curious=definition.Curious; t.Active=definition.Active; t.Explore=definition.Explore; t.Calm=definition.Calm;
            Settings.Memory.Adaptive.TraitMilestone=t.Values();
            Engine=new BaliLifeEngine(Settings,definition.Seed,cognitive);
            Random=new Random(definition.Seed^75301);
            FeedbackRandom=new Random(definition.Seed^45919);
        }
        internal SocialMemory Relation(Agent other)
        {
            SocialMemory relation;
            if(!Social.TryGetValue(other.Id,out relation)) Social.Add(other.Id,relation=new SocialMemory());
            return relation;
        }
        internal void CountDecision(string action)
        {
            int count;
            Decisions.TryGetValue(action,out count);
            Decisions[action]=count+1;
        }
        public string Summary()
        {
            var m=Settings.Memory;
            var t=m.Adaptive.Traits;
            var last=m.Cognitive.Recent.LastOrDefault();
            return String.Format("{0} · {1}\nEnergía {2:P0} · sociabilidad {3:P0}\nCuriosidad {4:P0} · tranquilidad {5:P0}\n{6}",
                Paused?"En pausa":Engine.Current.ToString(),Engine.GoalName,m.Energy,t.Social,t.Curious,t.Calm,
                last==null?"Sin resultado asociativo":last.Action+" → "+last.Result);
        }
    }

    [DataContract]
    public sealed class AgentResult
    {
        [DataMember] public AgentDefinition Definition;
        [DataMember] public Memory Memory;
        [DataMember] public Dictionary<string,int> Decisions;
        [DataMember] public Dictionary<string,SocialMemory> Relationships;
        [DataMember] public int FeedbackOpportunities, NegativeFeedback, PositiveFeedback, NoFeedback, Contacts;
        [DataMember] public string LastSocial;
    }

    [DataContract]
    public sealed class AgentReport
    {
        [DataMember] public DateTime Start, End;
        [DataMember] public string Scenario;
        [DataMember] public bool Cognitive;
        [DataMember] public bool Learning;
        [DataMember] public List<AgentResult> Agents;
        [DataMember] public List<SocialEvent> Events;

        public string Summary()
        {
            return String.Join("\n\n",Agents.Select(a=>
            {
                var cognitive=a.Memory.Cognitive;
                var traits=a.Memory.Adaptive.Traits;
                string counts=String.Join(", ",a.Decisions.OrderBy(x=>x.Key).Select(x=>x.Key+" "+x.Value));
                return String.Format(CultureInfo.InvariantCulture,
                    "{0} · {1} contactos · {2}/{3} negativas · recompensa {4:+0.0;-0.0;0.0}\n{5}\nRasgos {6} · {7} cambios de objetivo\n{8}\n{9}",
                    a.Definition.Name,a.Contacts,a.NegativeFeedback,a.FeedbackOpportunities,cognitive.TotalReward,
                    counts,String.Join(" / ",traits.Values().Select(x=>x.ToString("P0",CultureInfo.InvariantCulture))),
                    cognitive.GoalChanges,cognitive.Preference(),String.Join(" · ",a.Relationships.Select(pair=>
                        String.Format("{0}: afinidad {1:+0.00;-0.00;0.00}, familiaridad {2:P0}, confianza {3:P0}",pair.Key,pair.Value.Affinity,pair.Value.Familiarity,pair.Value.Confidence))));
            }));
        }
    }

    public sealed class AgentWorld
    {
        public const int Capacity=6;
        public readonly List<Agent> Agents=new List<Agent>();
        readonly List<SocialEvent> events=new List<SocialEvent>();
        readonly DateTime start;
        double time, nextSocial=12;
        int nextId;
        int socialRound;
        double changeAfter=15*86400;
        readonly bool cognitive;
        readonly bool learning;
        public DateTime Now { get { return start.AddSeconds(time); } }
        public AgentWorld(IEnumerable<AgentDefinition> definitions,bool cognitive,bool learning=true)
        {
            start=new DateTime(2025,1,1,9,0,0,DateTimeKind.Local).ToUniversalTime();
            this.cognitive=cognitive;
            this.learning=learning;
            foreach(var definition in definitions) Add(definition);
        }
        public Agent Add(AgentDefinition definition)
        {
            if (Agents.Count>=Capacity) throw new InvalidOperationException("El laboratorio admite hasta seis agentes.");
            var agent=new Agent(definition,++nextId,cognitive);
            agent.Engine.ConsequenceEnabled(cognitive && learning);
            Agents.Add(agent);
            return agent;
        }
        public void Remove(Agent agent)
        {
            Agents.Remove(agent);
            foreach(var other in Agents) other.Social.Remove(agent.Id);
            events.RemoveAll(e=>e.From==agent.Definition.Name || e.To==agent.Definition.Name);
        }

        public void Step(double seconds,EnvironmentPattern scenario,bool feedback)
        {
            time+=seconds;
            var stamp=Now;
            bool simulateDay=feedback && (stamp.ToLocalTime().Hour<8 || stamp.ToLocalTime().Hour>=23);
            bool present=scenario!=EnvironmentPattern.Ausencia && !simulateDay;
            foreach(var a in Agents)
            {
                if(a.Paused) continue;
                var engine=a.Engine;
                double chance=scenario==EnvironmentPattern.Frecuente?.30:scenario==EnvironmentPattern.Limitada?.025:
                    scenario==EnvironmentPattern.Cambiante && time<changeAfter?.2:.10;
                bool proximity=present && a.Random.NextDouble()<chance;
                double idle=present?0:5400;
                engine.Tick(seconds,idle,proximity,true,stamp);
                var action=engine.Profile.LastDecision;
                if (engine.DecisionVersion!=a.LastDecisionVersion && !String.IsNullOrEmpty(action))
                {
                    a.LastDecisionVersion=engine.DecisionVersion;
                    a.CountDecision(action);
                    a.LastAction=action;
                    if (present && feedback) EnvironmentFeedback(a,action,scenario,stamp);
                }
            }
            if (present && time>=nextSocial)
            {
                nextSocial=time+Math.Max(12,seconds*3);
                SocialStep(stamp);
            }
        }
        void EnvironmentFeedback(Agent a,string action,EnvironmentPattern scenario,DateTime stamp)
        {
            double roll=a.FeedbackRandom.NextDouble();
            double contact=a.FeedbackRandom.NextDouble();
            if (scenario==EnvironmentPattern.Frecuente && contact<.22 || scenario==EnvironmentPattern.Limitada && contact<.035)
            {
                a.ContactEvents++;
                a.Engine.Interact(stamp.AddSeconds(1),false,true);
            }
            if (action=="Descansar" || action=="Despertar" || action=="Calma concentrada" || action=="Calma manual") return;
            if (scenario!=EnvironmentPattern.Negativa && scenario!=EnvironmentPattern.Positiva && scenario!=EnvironmentPattern.Cambiante) return;
            if (roll>.82) { a.NoFeedback++; return; }
            bool prefersExplore=scenario==EnvironmentPattern.Positiva || scenario==EnvironmentPattern.Cambiante && time<changeAfter;
            bool bad=prefersExplore?action=="Invitar":action=="Explorar" || action=="Microexpresión";
            bool good=action=="Observar" || prefersExplore && action=="Explorar";
            a.Opportunities++;
            if (bad) a.NegativeFeedback++;
            if (good) a.PositiveFeedback++;
            if(!bad && !good) a.NoFeedback++;
            if (bad || good) a.Engine.Feedback(bad?Outcome.Negativa:Outcome.Positiva,stamp.AddSeconds(1));
        }
        void SocialStep(DateTime stamp)
        {
            var available=Agents.Where(a=>!a.Paused && a.Engine.Current!=Expression.Descanso &&
                a.Engine.Current!=Expression.Sueño && a.Settings.Mode!=Mode.Concentrado).ToList();
            if(available.Count<2) return;
            var busy=new HashSet<int>();
            for(int slot=0;slot<available.Count;slot++)
            {
                var actor=available[(slot+socialRound)%available.Count];
                if(busy.Contains(actor.Id)) continue;
                Agent other=null;
                double best=double.MinValue;
                foreach(var target in available)
                {
                    if(target==actor || busy.Contains(target.Id)) continue;
                    double score=actor.Relation(target).Affinity*.42+actor.Random.NextDouble()*.38;
                    if(score>best) { best=score; other=target; }
                }
                if(other==null) continue;
                var m=actor.Settings.Memory;
                var t=m.Adaptive.Traits;
                var relation=actor.Relation(other);
                var proposals=new[] {
                    .09+t.Social*.46+m.Adaptive.InteractionNeed*.18+relation.Affinity*.20,
                    .17+m.Curiosity*.48,
                    .16+t.Explore*.43+m.Energy*.13,
                    .02+relation.Familiarity*.47+relation.Affinity*.25,
                    .19+t.Calm*.49+(1-m.Energy)*.18
                };
                int choice=0;
                double largest=-1;
                for(int i=0;i<proposals.Length;i++)
                {
                    double score=proposals[i]+actor.Random.NextDouble()*.19;
                    if(score>largest) { largest=score; choice=i; }
                }
                if(choice==4) { actor.LastSocial="Permanece independiente."; continue; }
                var receiver=other.Settings.Memory;
                var reciprocal=other.Relation(actor);
                double accept=.13+receiver.Adaptive.Traits.Social*.49+reciprocal.Affinity*.30;
                double withdrawal=.12+receiver.Adaptive.Traits.Calm*.18;
                double roll=other.Random.NextDouble();
                bool accepted=(choice==0 || choice==3) && roll<accept;
                bool withdrew=!accepted && roll>1-withdrawal;
                string action=new[] {"Saludar","Observar","Explorar cerca","Compartir actividad"}[choice];
                string response=withdrew?"Se retira":accepted?(choice==0?"Responde al saludo":"Se suma"):"Mantiene su actividad";
                foreach(var row in new[] { relation,reciprocal })
                {
                    row.Encounters++; row.Last=action+" · "+response;
                    if(accepted) row.Accepted++;
                    if(choice==1 || choice==2) row.Observed++;
                    if(accepted && choice==3) row.Shared++;
                    if(withdrew) row.Withdrawn++;
                }
                actor.LastSocial=action+" → "+other.Definition.Name+" · "+response;
                other.LastSocial=actor.Definition.Name+" → "+response;
                actor.Engine.Cue(choice==0?Expression.Saludo:Expression.Curiosidad,2,1);
                if(accepted)
                {
                    other.Engine.Cue(choice==0?Expression.Saludo:Expression.Alegría,2,2);
                    actor.ContactEvents++; other.ContactEvents++;
                    actor.Engine.Interact(stamp.AddSeconds(1),false,false);
                    other.Engine.Interact(stamp.AddSeconds(1),false,false);
                    other.Engine.Cue(choice==0?Expression.Saludo:Expression.Alegría,2,4);
                }
                events.Add(new SocialEvent { Date=stamp,From=actor.Definition.Name,To=other.Definition.Name,Action=action,Response=response });
                if(events.Count>100) events.RemoveAt(0);
                busy.Add(actor.Id); busy.Add(other.Id);
            }
            socialRound=(socialRound+1)%available.Count;
        }
        public AgentReport Report(EnvironmentPattern scenario)
        {
            return new AgentReport { Start=start, End=Now, Scenario=scenario.ToString(), Cognitive=cognitive, Learning=cognitive && learning, Events=new List<SocialEvent>(events),
                Agents=Agents.Select(a=>new AgentResult { Definition=a.Definition.Copy(), Memory=a.Settings.Memory, Decisions=a.Decisions,
                    Relationships=Agents.Where(b=>a.Social.ContainsKey(b.Id)).ToDictionary(b=>b.Definition.Name+" #"+b.Id,b=>a.Social[b.Id]),
                    FeedbackOpportunities=a.Opportunities,NegativeFeedback=a.NegativeFeedback,PositiveFeedback=a.PositiveFeedback,
                    NoFeedback=a.NoFeedback,Contacts=a.ContactEvents,LastSocial=a.LastSocial }).ToList() };
        }
        public static AgentReport Experiment(AgentDefinition[] definitions,int days,EnvironmentPattern scenario,bool cognitive,bool learning=true)
        {
            var world=new AgentWorld(definitions,cognitive,learning);
            world.changeAfter=days*43200.0;
            for(int i=0;i<days*24*60*4;i++) world.Step(15,scenario,true);
            return world.Report(scenario);
        }
    }
}

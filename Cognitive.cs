using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BaliDesktop
{
    public enum Outcome { Positiva, Negativa, Contacto, SinRespuesta, Interrumpida, Desconocida, MenosFrecuente, MásFrecuente }

    [DataContract]
    public sealed class Association
    {
        [DataMember] public string Action, Context;
        [DataMember] public double Evidence, Value;
        [DataMember] public int Encounters, Contact, Silent;
        [DataMember] public DateTime Date;
        public double Confidence { get { return Evidence/(Evidence+6); } }
    }

    [DataContract]
    public sealed class Experience
    {
        [DataMember] public string Action, Context;
        [DataMember] public DateTime Date;
        [DataMember] public Outcome Result;
        [DataMember] public double Reward;
        public int Relevance { get { return Math.Abs(Reward)>=.5?3:Reward!=0?2:1; } }
    }

    [DataContract]
    public sealed class CognitiveMemory
    {
        [DataMember] public List<Association> Associations=new List<Association>();
        [DataMember] public List<Experience> Recent=new List<Experience>();
        [DataMember] public int[] Results=new int[Enum.GetValues(typeof(Outcome)).Length];
        [DataMember] public int GoalChanges, GoalsSatisfied;
        [DataMember] public double TotalReward;

        public string Preference()
        {
            var best=Associations.Where(a=>a.Context=="Todas" && a.Evidence>=3 && a.Value>.06)
                .OrderByDescending(a=>a.Value*a.Confidence).FirstOrDefault();
            return best==null?"Aún no tengo una preferencia positiva suficientemente respaldada.":
                String.Format("Tengo una preferencia aprendida por {0}, con confianza {1:P0}.",best.Action.ToLowerInvariant(),best.Confidence);
        }
    }

    public sealed class Prediction
    {
        public readonly double Reward, Confidence, Exploration;
        public readonly double? ContactProbability;
        public readonly int Samples;
        internal Prediction(double reward,double confidence,int samples,double? contact,double exploration)
        { Reward=reward; Confidence=confidence; Samples=samples; ContactProbability=contact; Exploration=exploration; }
        public string Describe()
        {
            string contact=ContactProbability.HasValue?String.Format("Contacto estimado {0:P0}",ContactProbability.Value):"Contacto incierto";
            return String.Format("{0} · {1} observaciones · confianza de recompensa {2:P0} · expectativa {3:+0.00;-0.00;0.00}",
                contact,Samples,Confidence,Reward);
        }
    }

    public sealed class BaliLearningEngine
    {
        readonly CognitiveMemory memory;
        string pendingAction,pendingContext;
        DateTime started;
        static readonly TimeSpan Window=TimeSpan.FromSeconds(18);
        public bool Pending { get { return pendingAction!=null; } }
        public string PendingAction { get { return pendingAction; } }
        public BaliLearningEngine(CognitiveMemory memory) { this.memory=memory; }

        static double Decay(Association stats,DateTime utc)
        { return Math.Pow(.5,Math.Max(0,(utc-stats.Date).TotalDays)/21); }

        public Prediction Predict(string action,string context,DateTime utc,bool explore)
        {
            var local=memory.Associations.Find(a=>a.Action==action && a.Context==context);
            var general=memory.Associations.Find(a=>a.Action==action && a.Context=="Todas");
            double l=local==null?0:local.Evidence*Decay(local,utc), g=general==null?0:general.Evidence*Decay(general,utc);
            double weight=l/(l+5);
            double confidence=(l+g*.5)/(l+g*.5+6);
            double estimate=(local==null?0:local.Value*weight)+(general==null?0:general.Value*(1-weight));
            var contact=local!=null && local.Contact+local.Silent>=8?local:general;
            int observable=contact==null?0:contact.Contact+contact.Silent;
            int samples=local!=null && local.Encounters>=8?local.Encounters:general==null?0:general.Encounters;
            return new Prediction(estimate,confidence,samples,observable>=8?(double?)(contact.Contact+1.0)/(observable+4):null,
                explore?.034/Math.Sqrt(1+samples):0);
        }

        public void Begin(string action,string context,DateTime utc)
        {
            Expire(utc);
            if (Pending) Finish(Outcome.Desconocida,utc);
            pendingAction=action; pendingContext=context; started=utc;
        }
        public void Expire(DateTime utc)
        {
            if (!Pending) return;
            if (utc<started || utc-started>Window+TimeSpan.FromSeconds(20)) Finish(Outcome.Desconocida,utc);
            else if (utc-started>Window) Finish(Outcome.SinRespuesta,utc);
        }
        public bool Contact(DateTime utc,bool direct)
        {
            Expire(utc);
            if (!direct || !Pending || pendingContext.StartsWith("Sin presencia") ||
                (!pendingContext.StartsWith("Cerca") && pendingAction!="Invitar" && pendingAction!="Saludar") ||
                (utc-started).TotalSeconds<.65) return false;
            Finish(Outcome.Contacto,utc);
            return true;
        }
        public bool Feedback(Outcome result,DateTime utc)
        {
            Expire(utc);
            if (!Pending || (result!=Outcome.Positiva && result!=Outcome.Negativa && result!=Outcome.MenosFrecuente && result!=Outcome.MásFrecuente)) return false;
            Finish(result,utc);
            return true;
        }
        public void Interrupt(DateTime utc) { if(Pending) Finish(Outcome.Interrumpida,utc); }
        public void ForgetPending() { pendingAction=null; }
        void Finish(Outcome result,DateTime utc)
        {
            double reward=result==Outcome.Positiva?1:result==Outcome.Negativa?-1:
                result==Outcome.MásFrecuente?.55:result==Outcome.MenosFrecuente?-.55:result==Outcome.Contacto?.28:0;
            memory.Results[(int)result]++;
            memory.TotalReward+=reward;
            memory.Recent.Add(new Experience { Action=pendingAction,Context=pendingContext,Date=utc,Result=result,Reward=reward });
            if (memory.Recent.Count>36) memory.Recent.RemoveAt(0);
            Update(pendingAction,pendingContext,result,reward,utc);
            Update(pendingAction,"Todas",result,reward,utc);
            pendingAction=null;
        }
        void Update(string action,string context,Outcome result,double reward,DateTime utc)
        {
            if (result==Outcome.Desconocida || result==Outcome.Interrumpida) return;
            var a=memory.Associations.Find(row=>row.Action==action && row.Context==context);
            if (a==null)
            {
                if (memory.Associations.Count>=96) memory.Associations.Remove(memory.Associations.OrderBy(x=>x.Date).First());
                a=new Association { Action=action,Context=context,Date=utc };
                memory.Associations.Add(a);
            }
            if (a.Encounters>=512) { a.Encounters/=2; a.Contact/=2; a.Silent/=2; }
            a.Encounters++;
            if (result==Outcome.Contacto) a.Contact++;
            if (result==Outcome.SinRespuesta) a.Silent++;
            if (reward==0) return;
            double decayed=Decay(a,utc);
            a.Evidence=Math.Min(32,a.Evidence*decayed+Math.Abs(reward));
            a.Value=Math.Max(-.9,Math.Min(.9,a.Value*decayed+.10*(reward-a.Value*decayed)));
            a.Date=utc;
        }
    }

    public sealed class Motivation
    {
        public readonly string Name;
        public readonly double Priority, Intensity, Deadline;
        internal Motivation(string name,double priority,double intensity,double deadline)
        { Name=name; Priority=priority; Intensity=intensity; Deadline=deadline; }
    }

    public sealed partial class BaliLifeEngine
    {
        readonly bool cognitive;
        readonly BaliLearningEngine learner;
        readonly Dictionary<string,double> goalsCooling=new Dictionary<string,double>();
        Motivation goal;
        string cognitionContext;
        double nextGoal;
        int invitationBudget;
        double invitationBudgetUntil;
        static readonly string[] GoalLabels={"Recuperar energía","Reducir estímulos","Satisfacer curiosidad","Buscar interacción","Probar alternativas","Mantener tranquilidad"};
        static readonly double[] GoalOn={.65,.59,.48,.65,.50,0}, GoalOff={.30,.33,.38,.43,.30,0}, GoalRanks={1,.86,.56,.45,.40,.38};
        public Motivation Goal { get { return goal; } }
        public string GoalName { get { return goal==null?"Sin objetivo activo":goal.Name; } }
        public string PendingInitiative { get { return learner.PendingAction; } }
        public DecisionScore LastInitiative { get; private set; }
        public string LastContext { get; private set; }

        public bool Feedback(Outcome result,DateTime utc)
        { return cognitive && settings.ConsequenceLearning && learner.Feedback(result,utc); }
        public void InterruptInitiative(DateTime utc) { learner.Interrupt(utc); }
        public void ConsequenceEnabled(bool enabled)
        { settings.ConsequenceLearning=enabled; if (!enabled) learner.ForgetPending(); }
        public void ResetConsequences()
        {
            var m=settings.Memory.Cognitive;
            m.Associations.Clear(); m.Recent.Clear(); Array.Clear(m.Results,0,m.Results.Length); m.TotalReward=0;
            learner.ForgetPending();
            LastInitiative=null;
        }
        string ExperienceContext(bool near,double idle)
        {
            return (idle>45?"Sin presencia reciente":near?"Cerca":"Activo a distancia") +
                (settings.Memory.Energy<.36?" · energía baja":" · energía suficiente");
        }
        void NoteInitiative(DecisionScore chosen,bool record)
        {
            LastInitiative=chosen;
            LastContext=cognitionContext;
            if (record && settings.ConsequenceLearning) learner.Begin(chosen.Name,cognitionContext,utcNow);
        }
        void StartRestExperience(bool near,double idle)
        {
            cognitionContext=ExperienceContext(near,idle);
            var forecast=settings.ConsequenceLearning?learner.Predict("Descansar",cognitionContext,utcNow,false):null;
            NoteInitiative(new DecisionScore("Descansar","La condición de reposo tiene prioridad.",1,Expression.Descanso,0,0,forecast:forecast),true);
        }
        void Goals(bool sleeping,double idle)
        {
            if (!cognitive) return;
            var m=settings.Memory;
            var p=Profile;
            if (goal!=null && goal.Name=="Buscar interacción" && (sleeping || idle>45 || settings.Autonomy<=0)) goal=null;
            if (elapsed<nextGoal && goal!=null) return;
            nextGoal=elapsed+2;
            double[] levels={sleeping?Math.Max(.68,m.Rest):m.Rest, m.Stimulation, m.Curiosity, p.InteractionNeed, m.Curiosity*(1-m.Stimulation), p.Traits.Calm};
            var labels=GoalLabels; var on=GoalOn; var off=GoalOff; var ranks=GoalRanks;
            int index=goal==null?-1:Array.IndexOf(labels,goal.Name);
            if (index>=0 && (elapsed>=goal.Deadline || levels[index]<off[index]))
            {
                if(levels[index]<off[index]) m.Cognitive.GoalsSatisfied++;
                goalsCooling[goal.Name]=elapsed+12; goal=null;
            }
            int best=-1;
            double score=-1;
            for(int i=0;i<labels.Length;i++)
            {
                double until;
                if (goalsCooling.TryGetValue(labels[i],out until) && elapsed<until) continue;
                if (levels[i]<on[i] || (i==3 && (idle>45 || settings.Autonomy<=0 || settings.Mode==Mode.Concentrado))) continue;
                if ((sleeping || settings.Mode==Mode.Concentrado) && i>=2 && i<5) continue;
                double current=levels[i]*ranks[i]+(sleeping && i==0?.70:0);
                if (current>score) { best=i; score=current; }
            }
            if (best<0) { goal=null; p.CognitiveGoal=null; return; }
            if (goal==null || (goal.Name!=labels[best] && (index<0 || score>goal.Priority*goal.Intensity+.10)))
            {
                goal=new Motivation(labels[best],ranks[best],levels[best],elapsed+(best==3?35:65));
                m.Cognitive.GoalChanges++;
                p.CognitiveGoal=goal.Name;
            }
            else
            {
                index=Array.IndexOf(labels,goal.Name);
                goal=new Motivation(goal.Name,goal.Priority,levels[index],goal.Deadline);
            }
        }
        double GoalInfluence(string action)
        {
            if (goal==null) return 0;
            switch(goal.Name)
            {
                case "Recuperar energía":
                    return (action=="Descansar"?.08:action=="Calma"?.065:action=="Invitar"||action=="Moverse"?-.045:0)*goal.Intensity;
                case "Reducir estímulos":
                case "Mantener tranquilidad": return (action=="Calma"?.065:action=="Invitar"||action=="Moverse"?-.045:0)*goal.Intensity;
                case "Satisfacer curiosidad": return (action=="Explorar"||action=="Observar"?.07:0)*goal.Intensity;
                case "Buscar interacción": return (action=="Invitar"?.075:0)*goal.Intensity;
                case "Probar alternativas": return (action=="Microexpresión"||action=="Explorar"?.065:0)*goal.Intensity;
                default:return 0;
            }
        }
    }
}

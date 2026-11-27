using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace BaliDesktop
{
    [DataContract]
    public sealed class Traits
    {
        [DataMember] public double Social = .5, Curious = .5, Active = .5, Explore = .5, Calm = .5;
        public double[] Values() { return new[] { Social, Curious, Active, Explore, Calm }; }
    }

    [DataContract]
    public sealed class HourHabit
    {
        [DataMember] public int Samples, Active, Interactions, Invited, Answered;
        [DataMember] public int[] Modes = new int[4];
        public double Confidence { get { return Samples/(Samples+12.0); } }
        public double ActiveBias { get { return (2.0*Active-Samples)/(Samples+12.0); } }
        public double FocusWeight { get { return Modes[(int)Mode.Concentrado]/(Samples+12.0); } }
    }

    [DataContract]
    public sealed class Episode
    {
        [DataMember] public string Kind, Description;
        [DataMember] public DateTime Date;
        [DataMember] public int Relevance, Count;
    }

    [DataContract]
    public sealed class DayRecord
    {
        [DataMember] public string Date;
        [DataMember] public double[] Values;
        [DataMember] public double Seconds;
        [DataMember] public int Interactions, Decisions;
    }

    [DataContract]
    public sealed class AdaptiveProfile
    {
        [DataMember] public Traits Traits = new Traits();
        [DataMember] public HourHabit[] Hours = Enumerable.Range(0,24).Select(i=>new HourHabit()).ToArray();
        [DataMember] public List<Episode> Episodes = new List<Episode>();
        [DataMember] public List<DayRecord> History = new List<DayRecord>();
        [DataMember] public DateTime KnownSince, LastSeen;
        [DataMember] public bool OriginalUnknown, VoiceMilestone;
        [DataMember] public string LastDate, LastReason = "", LastDecision = "";
        [DataMember] public string CognitiveGoal;
        [DataMember] public int Days, Decisions;
        [DataMember] public double InteractionNeed = .25, Satisfaction = .55, Activation = .3, RuntimeSeconds;
        [DataMember] public double[] TraitMilestone = new[] {.5,.5,.5,.5,.5};
        internal void Record(string kind,string description,int importance,DateTime utc)
        {
            Episodes.RemoveAll(e=>e.Relevance<3 && e.Date<utc.AddDays(-90));
            var recent=Episodes.LastOrDefault(e=>e.Kind==kind && e.Description==description && e.Date>=utc.AddHours(-1));
            if (recent!=null) { recent.Count++; recent.Date=utc; Episodes.Remove(recent); Episodes.Add(recent); return; }
            Episodes.Add(new Episode { Kind=kind,Description=description,Relevance=importance,Date=utc,Count=1 });
            if (Episodes.Count>80) Episodes.Remove(Episodes.OrderBy(e=>e.Relevance).ThenBy(e=>e.Date).First());
        }
        public HourHabit StrongestHour()
        {
            return Hours.Where(h=>h.Samples>=12 && h.Interactions>=2)
                .OrderByDescending(h=>h.Interactions/(h.Samples+12.0)).FirstOrDefault();
        }
        public string HabitSummary()
        {
            var hour=StrongestHour();
            return hour==null ? "Todavía no hay suficientes observaciones de interacción." :
                String.Format("La franja {0:00}:00 tiene {1} interacciones registradas y confianza {2:P0}.",Array.IndexOf(Hours,hour),hour.Interactions,hour.Confidence);
        }
        public int AgeDays(DateTime utc)
        {
            return KnownSince==default(DateTime) ? 0 : Math.Max(0,(int)(utc-KnownSince).TotalDays);
        }
    }

    public sealed class DecisionScore
    {
        public readonly string Name, Reason;
        public readonly double Score;
        public readonly double BaseUtility, Learned, Intrinsic, Interruption, Motivation;
        public readonly Prediction Forecast;
        internal readonly Expression Expression;
        internal readonly double Duration, Cooldown;
        internal DecisionScore(string name,string reason,double score,Expression expression,double duration,double cooldown,
            double baseUtility=0,double learned=0,double intrinsic=0,double interruption=0,double motivation=0,Prediction forecast=null)
        { Name=name; Reason=reason; Score=score; Expression=expression; Duration=duration; Cooldown=cooldown;
          BaseUtility=baseUtility; Learned=learned; Intrinsic=intrinsic; Interruption=interruption; Motivation=motivation; Forecast=forecast; }
    }

    public sealed partial class BaliLifeEngine
    {
        readonly Dictionary<string,double> lastActions = new Dictionary<string,double>();
        double observedSeconds, nextDecision = 5, nextProximity, invitationUntil;
        HourHabit invitationHour;
        DateTime utcNow;
        bool nearPending;
        public DecisionScore[] Candidates { get; private set; }
        public AdaptiveProfile Profile { get { return settings.Memory.Adaptive; } }
        public double CircadianBias { get; private set; }
        public int DecisionVersion { get; private set; }

        public void Begin(DateTime utc,bool migrated)
        {
            if (Profile.KnownSince!=default(DateTime)) return;
            Profile.KnownSince=utc;
            Profile.OriginalUnknown=migrated;
            Profile.Record(migrated?"Migración":"Inicio",migrated?"Primer registro fiable de Adaptive Life.":"Primera ejecución registrada.",3,utc);
        }
        public void VoiceRecognized(DateTime utc)
        {
            if (Profile.VoiceMilestone) return;
            Profile.VoiceMilestone=true;
            Profile.Record("Voz","Primer comando reconocido por el micrófono.",3,utc);
        }
        public void RecordMode(Mode mode,DateTime utc)
        {
            Profile.Record("Modo","Modo "+mode.ToString().ToLowerInvariant()+" seleccionado.",1,utc);
        }
        public void ResetLearning()
        {
            var p=Profile;
            p.Hours=new AdaptiveProfile().Hours;
            p.Traits=new Traits();
            p.TraitMilestone=p.Traits.Values();
            foreach(var day in p.History) day.Values=null;
            p.Episodes.RemoveAll(e=>e.Kind=="Hábito" || e.Kind=="Personalidad");
            invitationHour=null;
            invitationUntil=0;
            observedSeconds=0;
        }
        void LearnInteraction(DateTime utc)
        {
            var p=Profile;
            p.InteractionNeed=Limit(p.InteractionNeed-.18);
            p.Satisfaction=Limit(p.Satisfaction+.11);
            p.Activation=Limit(p.Activation+.07);
            if (settings.Learning)
            {
                var hour=p.Hours[utc.ToLocalTime().Hour];
                hour.Interactions=Math.Min(10000,hour.Interactions+1);
                if (invitationHour!=null && elapsed<=invitationUntil)
                {
                    invitationHour.Answered=Math.Min(invitationHour.Invited,invitationHour.Answered+1);
                    invitationHour=null;
                }
            }
            if (settings.Memory.Interactions==1 || settings.Memory.Interactions%25==0)
                p.Record("Interacción",String.Format("Interacción acumulada número {0}.",settings.Memory.Interactions),2,utc);
            else p.Record("Interacción","Interacción directa con Bali.",1,utc);
            if (p.History.Count>0 && p.History[p.History.Count-1].Date==utc.ToLocalTime().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))
                p.History[p.History.Count-1].Interactions++;
        }
        void Observe(double seconds,double idle,DateTime utc)
        {
            var p=Profile;
            var local=utc.ToLocalTime();
            if (p.LastSeen!=default(DateTime) && (utc-p.LastSeen).TotalSeconds>120)
            {
                double recovery=1-Math.Exp(-(utc-p.LastSeen).TotalHours/3);
                var m=settings.Memory;
                m.Energy=Approach(m.Energy,.86,recovery);
                m.Stimulation=Approach(m.Stimulation,.12,recovery);
                m.Attention=Approach(m.Attention,.32,recovery);
                m.Rest=Approach(m.Rest,.14,recovery);
                p.Activation=Approach(p.Activation,.28,recovery);
                p.Satisfaction=Approach(p.Satisfaction,.55,recovery);
                p.InteractionNeed=Approach(p.InteractionNeed,.40,recovery);
                wasAway=false;
                observedSeconds=0;
            }
            p.LastSeen=utc;
            p.RuntimeSeconds+=seconds;
            string day=local.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            if (p.LastDate!=day && (String.IsNullOrEmpty(p.LastDate) || String.CompareOrdinal(day,p.LastDate)>0))
            {
                p.LastDate=day;
                p.Days++;
                if (p.Days%7==0) p.Record("Actividad",p.Days+" días con actividad registrada.",2,utc);
            }
            var entry=p.History.LastOrDefault();
            if (entry==null || entry.Date!=day)
            {
                entry=new DayRecord { Date=day };
                p.History.Add(entry);
                if (p.History.Count>60) p.History.RemoveAt(0);
            }
            entry.Seconds+=seconds;

            var hour=p.Hours[local.Hour];
            CircadianBias=settings.Learning ? hour.ActiveBias : 0;
            if (!settings.Learning) return;
            observedSeconds+=seconds;
            if (observedSeconds<60) return;
            observedSeconds-=60;
            if (hour.Samples>=120) 
            {
                hour.Samples/=2; hour.Active/=2; hour.Interactions/=2; hour.Invited/=2; hour.Answered/=2;
                for(int i=0;i<hour.Modes.Length;i++) hour.Modes[i]/=2;
            }
            hour.Samples++;
            bool active=idle<120;
            if (active) hour.Active++;
            hour.Modes[(int)settings.Mode]++;
            if (hour.Samples==12 && (hour.Active>=9 || hour.Active<=3))
                p.Record("Hábito",String.Format("Franja {0:00}:00 con señales de {1}.",local.Hour,hour.Active>=9?"actividad":"inactividad"),2,utc);
            var t=p.Traits;
            int contacts=0, activeMinutes=0, accepted=0, offered=0;
            foreach (var bucket in p.Hours)
            {
                contacts+=bucket.Interactions;
                activeMinutes+=bucket.Active;
                accepted+=bucket.Answered;
                offered+=bucket.Invited;
            }
            double interaction=Math.Min(1,14.0*contacts/(activeMinutes+90));
            double response=accepted/(offered+8.0);
            t.Social=Approach(t.Social,.34+.42*interaction+.12*response,.0015);
            t.Curious=Approach(t.Curious,.34+.24*settings.Interest+.24*interaction,.0012);
            t.Active=Approach(t.Active,.34+.25*settings.Activity+.11*CircadianBias,.001);
            t.Explore=Approach(t.Explore,.32+.24*settings.Interest+.22*response,.001);
            t.Calm=Approach(t.Calm,.40+.36*hour.FocusWeight+.10*(active?0:1),.0015);
            entry.Values=t.Values();
            if (entry.Values.Zip(p.TraitMilestone,(x,y)=>Math.Abs(x-y)).Max()>=.12)
            {
                p.TraitMilestone=entry.Values;
                p.Record("Personalidad","Un rasgo se desplazó al menos 12 puntos desde su último hito.",2,utc);
            }
        }
        DecisionScore Score(string name,string reason,double utility,Expression expression,double duration,double cooldown)
        {
            if (cognitive && name=="Invitar" && invitationBudget>=3)
                return new DecisionScore(name,"Límite de tres invitaciones por tramo de 30 minutos.",-1,expression,duration,cooldown);
            double previous;
            if (lastActions.TryGetValue(name,out previous))
            {
                double remaining=elapsed-previous;
                if (remaining<cooldown) return new DecisionScore(name,"En pausa por cooldown",-1,expression,duration,cooldown);
                utility-=.22*Math.Max(0,1-remaining/(cooldown*3));
            }
            double noise=random.NextDouble()*.07;
            if (!cognitive) return new DecisionScore(name,reason,Limit(utility+noise),expression,duration,cooldown);
            var prediction=settings.ConsequenceLearning?learner.Predict(name,cognitionContext,utcNow,
                settings.Memory.Energy>.35 && (name=="Observar" || name=="Explorar" || name=="Microexpresión" || name=="Calma")):null;
            double learned=prediction==null?0:.30*prediction.Reward*prediction.Confidence;
            double intrinsic=prediction==null?0:prediction.Exploration;
            double cost=name=="Invitar"?.035:name=="Moverse"?.05:0;
            double motive=GoalInfluence(name);
            return new DecisionScore(name,reason,Limit(utility+noise+learned+intrinsic-cost+motive),expression,duration,cooldown,
                utility,learned,intrinsic,cost,motive,prediction);
        }
        void Decide(bool near,bool returned,double idle)
        {
            var p=Profile;
            var m=settings.Memory;
            var t=p.Traits;
            var hour=p.Hours[utcNow.ToLocalTime().Hour];
            if(elapsed>=invitationBudgetUntil) { invitationBudget=0; invitationBudgetUntil=elapsed+1800; }
            cognitionContext=ExperienceContext(near,idle);
            double quiet=settings.Learning ? hour.FocusWeight*.35 : 0;
            double active=settings.Autonomy;
            double social=p.InteractionNeed*.30+t.Social*.15;
            Expression micro=random.NextDouble()<settings.Play ? Expression.Picardía :
                p.Satisfaction>.57 ? Expression.Alegría : Expression.Procesamiento;
            Candidates=new[]
            {
                Score("Calma","La quietud encaja con el contexto.",.18+quiet+t.Calm*.12+(1-active)*.18+m.Rest*.10,Expression.Neutral,1,3),
                Score("Observar",near?"El cursor está cerca del rostro.":"La atención actual favorece observar el entorno.",.13+(near?.40:0)+m.Attention*.16+m.Curiosity*.17-quiet,Expression.Curiosidad,1.8,7),
                Score("Explorar","Curiosidad y estimulación baja favorecen explorar.",.15+t.Explore*.22+m.Curiosity*.24+(1-m.Stimulation)*.10+CircadianBias*.10-quiet,Expression.Curiosidad,2.8,12),
                Score("Saludar",returned?"Se detectó un regreso tras una ausencia.":"Sin regreso reciente.",returned?.97:0,Expression.Saludo,2.6,20),
                Score("Invitar","La necesidad de interacción favorece una señal discreta.",.10+social+active*.14+p.Activation*.08-.19*(idle>45?1:0)-quiet,Expression.Escucha,2.0,Math.Max(12,settings.InitiativeCooldown)),
                Score("Microexpresión","El nivel de activación y el ánimo favorecen un gesto breve.",.18+p.Activation*.21+m.Mood*.18+t.Active*.1-quiet, micro,1.8,8)
            };
            if (cognitive && settings.Move && settings.Mode==Mode.Libre && !near && idle<45)
                Candidates=Candidates.Concat(new[] { Score("Moverse","Explorar con movimiento autorizado.",.14+t.Explore*.30+m.Energy*.15,
                    Expression.Curiosidad,2.8,120) }).ToArray();
            if (cognitive && settings.Mode==Mode.Libre && m.Rest>.56 && idle>12 && !near && !returned)
                Candidates=Candidates.Concat(new[] { Score("Descansar","La necesidad de descanso favorece una pausa discreta.",.15+.42*m.Rest,
                    Expression.Descanso,6,65) }).ToArray();
            if (cognitive && !returned)
                Candidates[3]=new DecisionScore("Saludar","Sin regreso reciente.",-1,Expression.Saludo,2.6,20);
            var choice=Candidates.OrderByDescending(c=>c.Score).First();
            lastActions[choice.Name]=elapsed;
            p.LastDecision=choice.Name;
            p.LastReason=choice.Reason;
            DecisionVersion++;
            if (cognitive) NoteInitiative(choice,choice.Name!="Calma" || !learner.Pending);
            if (choice.Name=="Calma") return;
            if (choice.Name=="Invitar") invitationBudget++;
            if (choice.Name=="Descansar") napUntil=elapsed+choice.Duration;
            Cue(choice.Expression,choice.Duration,choice.Name=="Saludar"?3:1);
            p.Decisions++;
            if (p.History.Count>0) p.History[p.History.Count-1].Decisions++;
            if (choice.Name=="Invitar" && settings.Learning)
            {
                invitationUntil=elapsed+18;
                invitationHour=hour;
                hour.Invited++;
            }
            MoveRequested=cognitive?choice.Name=="Moverse":settings.Move && settings.Mode==Mode.Libre && !near && choice.Name=="Explorar" &&
                random.NextDouble()<.35*settings.Activity;
        }
        void ContextDecision(string name,string reason)
        {
            if (Profile.LastDecision==name) return;
            Candidates=new[] { new DecisionScore(name,reason,1,Current,0,0) };
            Profile.LastDecision=name;
            Profile.LastReason=reason;
            DecisionVersion++;
        }
    }
}

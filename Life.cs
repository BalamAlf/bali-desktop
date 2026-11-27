using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BaliDesktop
{
    public enum Mode { Libre, Concentrado, Descanso, Interactivo }
    public enum FaceStyle { Minimalista, Clásico }
    public enum Expression { Neutral, Alegría, Curiosidad, Satisfacción, Sorpresa, Confusión, Sueño, Concentración, Picardía, Escucha, Procesamiento, Saludo, Descanso, Aburrimiento, Entusiasmo }

    [DataContract]
    public sealed class Memory
    {
        [DataMember] public double Energy = .8, Mood = .65, Curiosity = .55, Stimulation = .15, Attention = .4, Rest = .2, Familiarity, ActiveSeconds;
        [DataMember] public int Interactions, Behaviors;
        [DataMember] public DateTime LastInteraction;
        [DataMember] public string LastBehavior = "Neutral";
        [DataMember] public Dictionary<string,int> Counts = new Dictionary<string,int>();
        [DataMember] public AdaptiveProfile Adaptive = new AdaptiveProfile();
        [DataMember] public CognitiveMemory Cognitive = new CognitiveMemory();
    }

    [DataContract]
    public sealed class Settings
    {
        [DataMember] public double Size = 220, Opacity = 1, Interest = .6, Activity = .5, Frequency = .5, Play = .5, Rest = .5;
        [DataMember] public int X = int.MinValue, Y = int.MinValue, Rate, Volume = 75;
        [DataMember] public bool Top = true, Move, Pass, AutoHide, Light, Voice, Microphone, Startup;
        [DataMember] public string VoiceName = "";
        [DataMember] public Mode Mode;
        [DataMember] public FaceStyle Appearance = FaceStyle.Minimalista;
        [DataMember] public bool Learning = true;
        [DataMember] public double Autonomy = .55, InitiativeCooldown = 75;
        [DataMember] public int Schema;
        [DataMember] public bool ConsequenceLearning=true;
        [DataMember] public IntelligenceSettings Intelligence=new IntelligenceSettings();
        [DataMember] public Memory Memory = new Memory();
        [OnDeserializing] void Defaults(StreamingContext context)
        {
            var defaults = new Settings();
            foreach (var field in typeof(Settings).GetFields()) field.SetValue(this, field.GetValue(defaults));
        }
    }

    public sealed partial class BaliLifeEngine
    {
        readonly Random random;
        readonly Settings settings;
        double elapsed, expires, napUntil;
        int priority;
        bool wasAway, wasNear, wasSleeping;
        public bool MoveRequested { get; private set; }
        public Expression Current { get; private set; }
        public BaliLifeEngine(Settings settings, int seed,bool cognitive=true)
        {
            this.settings=settings;
            random=new Random(seed);
            settings.Memory.Adaptive=settings.Memory.Adaptive ?? new AdaptiveProfile();
            settings.Memory.Cognitive=settings.Memory.Cognitive ?? new CognitiveMemory();
            this.cognitive=cognitive;
            learner=new BaliLearningEngine(settings.Memory.Cognitive);
            Candidates=new DecisionScore[0];
        }
        public static double Limit(double n) { return Math.Max(0, Math.Min(1, n)); }
        static double Approach(double value, double target, double rate) { return value + (target-value)*Limit(rate); }
        public void Interact(DateTime utc, bool surprise,bool direct=false)
        {
            var m = settings.Memory;
            m.Interactions++;
            m.LastInteraction = utc;
            m.Familiarity = Limit(m.Familiarity + .022*(1-m.Familiarity));
            m.Mood = Limit(m.Mood + .08);
            m.Stimulation = Limit(m.Stimulation + .15);
            LearnInteraction(utc);
            if (cognitive && settings.ConsequenceLearning) learner.Contact(utc,direct);
            if (settings.Mode != Mode.Descanso) Cue(surprise ? Expression.Sorpresa : Expression.Satisfacción, 2.2, 4);
        }
        public void ModeChanged()
        {
            learner.Interrupt(utcNow==default(DateTime)?DateTime.UtcNow:utcNow);
            priority=0; expires=0; napUntil=0; nextDecision=elapsed+3; nearPending=false; goal=null;
            Profile.CognitiveGoal=null;
        }
        public void Cue(Expression expression, double duration, int rank)
        {
            if (elapsed < expires && rank < priority) return;
            Set(expression);
            priority = rank;
            expires = elapsed + duration;
        }
        public void ReleaseVisualCue() { if(priority>=8) { priority=0; expires=0; } }
        void Set(Expression expression)
        {
            if (Current == expression) return;
            Current = expression;
            var m = settings.Memory;
            string name = expression.ToString();
            m.LastBehavior = name;
            m.Behaviors++;
            int count;
            m.Counts.TryGetValue(name, out count);
            m.Counts[name] = count + 1;
        }
        public void Tick(double seconds, double idle, bool near, bool present)
        {
            Tick(seconds,idle,near,present,new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(elapsed+seconds));
        }
        public void Tick(double seconds,double idle,bool near,bool present,DateTime utc)
        {
            seconds = Math.Max(0,Math.Min(seconds,60));
            utcNow=utc;
            if (cognitive && settings.ConsequenceLearning) learner.Expire(utc);
            Begin(utc,false);
            Observe(seconds,idle,utc);
            elapsed += seconds;
            MoveRequested = false;
            var m = settings.Memory;
            var mode = settings.Mode;
            bool away = idle >= 100 + 440*(1-settings.Rest);
            if (near || idle<=12) napUntil=0;
            bool shortRest=elapsed<napUntil && mode==Mode.Libre;
            bool sleeping = shortRest || mode == Mode.Descanso || (away && mode != Mode.Interactivo) ||
                (mode==Mode.Libre && m.Energy < .17 && idle > 45);
            if (present) m.ActiveSeconds += seconds;
            m.Energy = Approach(m.Energy,sleeping?.94:.24+.10*CircadianBias,
                seconds*(sleeping?.0018:.00021*(1+settings.Activity)));
            m.Rest = Approach(m.Rest, sleeping ? .09 : 1-m.Energy, seconds*.02);
            m.Stimulation = Approach(m.Stimulation, near && !sleeping ? .54 : .12, seconds*.12);
            m.Attention = Approach(m.Attention, sleeping ? .07 : near || mode == Mode.Interactivo ? .95 : .32, seconds*.35);
            m.Curiosity = Approach(m.Curiosity, Limit(settings.Interest*.8 + .18*m.Familiarity + (near ? .18 : .04)), seconds*.08);
            m.Mood = Approach(m.Mood, .55+.26*m.Familiarity, seconds*.012);
            var p=Profile;
            p.InteractionNeed=Approach(p.InteractionNeed,.60+.22*p.Traits.Social,seconds*.0015);
            p.Satisfaction=Approach(p.Satisfaction,.50+.12*m.Mood,seconds*.007);
            p.Activation=Approach(p.Activation,sleeping?.07:Limit(.22+.36*m.Stimulation+.16*CircadianBias+.09*p.Traits.Active),seconds*.035);
            Goals(sleeping,idle);
            nearPending |= near && !wasNear;
            bool returned=wasAway && !away;

            if (sleeping && !(elapsed < expires && priority >= 8))
            {
                if (cognitive && !shortRest && !wasSleeping && mode==Mode.Libre && present && settings.Autonomy>0)
                    StartRestExperience(near,idle);
                expires = 0;
                Set(m.Rest > .3 || mode == Mode.Descanso && idle < 8 ? Expression.Sueño : Expression.Descanso);
                if(!shortRest) ContextDecision("Descansar",mode==Mode.Descanso?"Modo Descanso seleccionado.":away?"Inactividad prolongada.":"Energía baja durante la inactividad.");
            }
            else if (elapsed >= expires)
            {
                Set(mode == Mode.Concentrado ? Expression.Concentración : mode == Mode.Interactivo ? Expression.Escucha : Expression.Neutral);
                if (mode==Mode.Concentrado) ContextDecision("Calma concentrada","El modo Concentrado bloquea las iniciativas.");
                else if (settings.Autonomy<=0) ContextDecision("Calma manual","La iniciativa autónoma está desactivada.");
                else if (wasSleeping && !returned) ContextDecision("Despertar","Terminó la condición de descanso.");
                else if (present && (returned || mode==Mode.Libre && nearPending && near && elapsed>=nextProximity || elapsed>=nextDecision))
                {
                    nextDecision=elapsed+(7+random.NextDouble()*12)/(.40+settings.Autonomy*settings.Frequency);
                    nextProximity=elapsed+5;
                    Decide(near,returned,idle);
                    nearPending=false;
                }
            }
            if (!near) nearPending=false;
            wasAway = away;
            wasNear = near;
            wasSleeping=sleeping;
        }
    }

    public enum Intent { Unknown, Hello, Focus, Free, Rest, Wake, Status, Here, Hide, Agents, Life, Classic, Minimal }
    public sealed class Reply
    {
        public readonly Intent Intent;
        public readonly string Text;
        public Reply(Intent intent, string text) { Intent = intent; Text = text; }
    }
    public interface IBaliIntelligenceProvider { Reply Respond(string text, Memory state); }

    public sealed class LocalConversationProvider : IBaliIntelligenceProvider
    {
        public static readonly string[] Commands = { "hola Bali", "Bali estoy ocupado", "Bali modo libre", "Bali descansa", "Bali despierta", "Bali cómo estás", "Bali ven aquí", "Bali escóndete" };
        public static readonly string[] ContextCommands={ "Bali qué has hecho hoy","Bali cuántos días tienes","Bali qué has aprendido",
            "Bali por qué estás descansando","Bali qué recuerdas","Bali por qué estás tan activo",
            "Bali qué has aprendido hoy","Bali por qué decidiste hacer eso","Bali qué prefieres hacer","Bali qué objetivo tienes ahora",
            "Bali cómo ha cambiado tu personalidad","Bali qué recuerdas de tus experiencias" };
        static readonly Intent[] Intents = { Intent.Hello, Intent.Focus, Intent.Free, Intent.Rest, Intent.Wake, Intent.Status, Intent.Here, Intent.Hide };
        public static readonly string[] ControlCommands={"Bali muestra el laboratorio","Bali muestra vida artificial","Bali apariencia clásica","Bali apariencia minimalista","Bali consulta energía"};
        readonly Random random = new Random();
        static string Normalize(string value)
        {
            var result = new System.Text.StringBuilder();
            foreach (char c in value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
                if (char.IsLetterOrDigit(c) || c == ' ') result.Append(c);
            return System.Text.RegularExpressions.Regex.Replace(result.ToString(), @"\s+", " ").Trim();
        }
        public Reply Respond(string text, Memory state)
        {
            string command = Normalize(text).Replace("bali", "").Trim();
            switch(command)
            {
                case "cambia a modo concentrado": case "modo concentrado": return new Reply(Intent.Focus,"Me quedo tranquilo.");
                case "solicitar descanso": case "modo descanso": return new Reply(Intent.Rest,"Voy a descansar.");
                case "mostrar el laboratorio": case "muestra el laboratorio": case "abre el laboratorio": return new Reply(Intent.Agents,"Abro el laboratorio de agentes.");
                case "muestra vida artificial": case "muestra el laboratorio de vida artificial": return new Reply(Intent.Life,"Abro Vida artificial.");
                case "apariencia clasica": case "cambia a apariencia clasica": return new Reply(Intent.Classic,"Activé el estilo clásico.");
                case "apariencia minimalista": case "cambia a apariencia minimalista": return new Reply(Intent.Minimal,"Activé el estilo minimalista.");
                case "cuanta energia tienes": case "consultar energia": case "consulta energia": case "energia":
                    return new Reply(Intent.Status,String.Format("Mi energía simulada está al {0:P0}.",state.Energy));
            }
            for (int i = 0; i < Commands.Length; i++)
            {
                if (command != Normalize(Commands[i]).Replace("bali", "").Trim()) continue;
                string[] replies = { random.Next(2) == 0 ? "¡Hola! Aquí estoy." : "Qué gusto verte.", "Me quedo tranquilo.", "Vuelvo al modo libre.", "Voy a descansar.", "Ya estoy despierto.", String.Format("Mi energía está al {0:P0} y mi nivel de activación al {1:P0}.",state.Energy,state.Adaptive==null?0:state.Adaptive.Activation), "Aquí estoy.", "Puedes volver a mostrarme desde la bandeja." };
                return new Reply(Intents[i], replies[i]);
            }
            var p=state.Adaptive;
            if (p!=null)
            {
                string answer=null;
                switch(command)
                {
                    case "que has hecho hoy":
                        var today=DateTime.Now.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
                        var day=p.History.FindLast(d=>d.Date==today);
                        answer=day==null?"Aún no hay actividad registrada para hoy.":String.Format("Hoy llevo {0:0} minutos de ejecución, {1} interacciones y {2} decisiones autónomas.",day.Seconds/60,day.Interactions,day.Decisions);
                        break;
                    case "cuantos dias tienes":
                        answer=p.KnownSince==default(DateTime)?"Aún no hay una fecha inicial registrada.":
                            String.Format("Han pasado {0} días desde mi primer registro fiable. He registrado actividad en {1} días.{2}",p.AgeDays(DateTime.UtcNow),p.Days,p.OriginalUnknown?" Mi fecha de creación original es desconocida.":"");
                        break;
                    case "que has aprendido": answer=p.HabitSummary(); break;
                    case "por que estas descansando":
                        answer=p.LastDecision=="Descansar"?p.LastReason:"Mi última decisión registrada no fue descansar."; break;
                    case "que recuerdas":
                        var last=p.Episodes.FindLast(e=>e.Relevance>=2);
                        answer=last==null?"Aún no tengo acontecimientos destacados.":String.Format("Mi último acontecimiento destacado fue el {0:d}. {1}",last.Date.ToLocalTime(),last.Description);
                        break;
                    case "por que estas tan activo":
                        answer=String.Format("Mi activación está al {0:P0}. {1}",p.Activation,String.IsNullOrEmpty(p.LastDecision)?"Todavía no hay una decisión registrada.":p.LastDecision+". "+p.LastReason);
                        break;
                    case "que has aprendido hoy":
                        var recent=state.Cognitive==null?null:state.Cognitive.Recent.FindLast(x=>x.Date.ToLocalTime().Date==DateTime.Today && x.Reward!=0);
                        answer=recent==null?"Hoy no tengo retroalimentación atribuible en mi registro reciente.":
                            String.Format("Hoy registré {0} al {1}. Lo incorporaré con un peso limitado.",recent.Result.ToString().ToLowerInvariant(),recent.Action.ToLowerInvariant());
                        break;
                    case "por que decidiste hacer eso":
                        answer=String.IsNullOrEmpty(p.LastDecision)?"Aún no he registrado una decisión.":p.LastDecision+". "+p.LastReason;
                        break;
                    case "que prefieres hacer":
                        answer=state.Cognitive==null?"Todavía no tengo preferencias registradas.":state.Cognitive.Preference();
                        break;
                    case "que objetivo tienes ahora":
                        answer=String.IsNullOrEmpty(p.CognitiveGoal)?"Estoy sin un objetivo temporal activo.":"Mi objetivo temporal es "+p.CognitiveGoal.ToLowerInvariant()+".";
                        break;
                    case "como ha cambiado tu personalidad":
                        var history=p.History.Where(d=>d.Values!=null).ToArray();
                        answer=history.Length<2?"Me faltan dos días con muestras para comparar mis rasgos.":
                            String.Format("Desde {0}, mi sociabilidad registrada pasó de {1:P0} a {2:P0} y mi curiosidad habitual de {3:P0} a {4:P0}.",
                                history[0].Date,history[0].Values[0],p.Traits.Social,history[0].Values[1],p.Traits.Curious);
                        break;
                    case "que recuerdas de tus experiencias":
                        var experience=state.Cognitive==null?null:state.Cognitive.Recent.LastOrDefault();
                        answer=experience==null?"Aún no tengo resultados asociativos registrados.":
                            String.Format("Mi experiencia más reciente fue {0}. El resultado registrado fue {1}.",
                                experience.Action.ToLowerInvariant(),experience.Result.ToString().ToLowerInvariant()); break;
                }
                if (answer!=null) return new Reply(Intent.Status,answer);
            }
            return new Reply(Intent.Unknown, "Prueba uno de los comandos de la lista.");
        }
    }
}

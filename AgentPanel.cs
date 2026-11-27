using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        const int AgentsTab=7;
        AgentWorld agentWorld;
        EnvironmentPattern agentScenario;
        bool runAgents=true;
        double agentRate=1, agentAccumulator, nextAgentRead;
        AgentReport[] experimentReports;
        readonly List<AgentCard> agentCards=new List<AgentCard>();
        Canvas agentHistory;
        TextBlock agentClock;
        ComboBox agentSelection;

        sealed class AgentCard
        {
            public Agent Agent;
            public Face Face;
            public BaliExpressionEngine Expressions;
            public TextBlock Details;
        }

        void AdvanceAgents(double now,double dt)
        {
            if (panel==null || tabs.SelectedIndex!=AgentsTab || agentWorld==null) return;
            if (runAgents)
            {
                agentAccumulator+=Math.Min(dt,.25)*agentRate;
                while(agentAccumulator>=1)
                {
                    double step=Math.Min(60,Math.Floor(agentAccumulator));
                    agentWorld.Step(step,agentScenario,true);
                    agentAccumulator-=step;
                }
            }
            if (now>=nextAgentRead)
            {
                nextAgentRead=now+1;
                agentClock.Text="Reloj virtual · "+agentWorld.Now.ToLocalTime().ToString("g")+" · "+
                    (runAgents?"En marcha":"En pausa")+" · "+agentWorld.Agents.Count+" agentes";
                foreach(var card in agentCards) card.Details.Text=card.Agent.Summary();
                var agent=agentSelection.SelectedItem as Agent;
                if (agent!=null) DrawTraitHistory(agentHistory,agent.Settings.Memory.Adaptive);
            }
            foreach(var card in agentCards)
            {
                if (!runAgents || card.Agent.Paused || !card.Face.IsVisible) continue;
                card.Expressions.Tick(dt,card.Agent.Engine.Current,0,0,false,
                    card.Agent.Engine.Current==Expression.Descanso || card.Agent.Engine.Current==Expression.Sueño);
                card.Face.InvalidateVisual();
            }
        }

        void AgentPage(Panel page)
        {
            if (agentWorld==null) agentWorld=new AgentWorld(AgentDefinition.Defaults(),true);
            Heading(page,"Laboratorio de agentes");
            page.Children.Add(Label("Entorno experimental. Los agentes, experiencias y resultados están separados del Bali del escritorio."));
            agentClock=Label("");
            page.Children.Add(agentClock);
            var controls=new WrapPanel();
            page.Children.Add(controls);
            ActionButton(controls,"Pausar / reanudar grupo",()=>runAgents=!runAgents);
            var layout=new WrapPanel();
            page.Children.Add(layout);
            Heading(page,"Perfiles del experimento");
            agentSelection=new ComboBox { DisplayMemberPath="Name", Width=230,HorizontalAlignment=HorizontalAlignment.Left };
            page.Children.Add(agentSelection);
            var actions=new WrapPanel();
            page.Children.Add(actions);
            Action rebuild=()=>
            {
                layout.Children.Clear();
                agentCards.Clear();
                agentSelection.ItemsSource=agentWorld.Agents.ToArray();
                agentSelection.SelectedIndex=agentWorld.Agents.Count>0?0:-1;
                foreach(var a in agentWorld.Agents)
                {
                    var card=new StackPanel { Width=178,Background=canvas,Margin=new Thickness(0,6,8,5) };
                    layout.Children.Add(card);
                    var title=Label(a.Definition.Name,17);
                    title.Margin=new Thickness(10,9,9,0);
                    card.Children.Add(title);
                    var expressions=new BaliExpressionEngine(a.Definition.Seed);
                    var face=new Face(expressions,a.Definition.Style) { Width=132,Height=84,IsHitTestVisible=false };
                    card.Children.Add(face);
                    var cardDetails=Label(a.Summary());
                    cardDetails.MinHeight=102;
                    cardDetails.Margin=new Thickness(10,0,8,4);
                    card.Children.Add(cardDetails);
                    var capture=a;
                    ActionButton(card,"Pausar / reanudar",()=>
                    {
                        capture.Paused=!capture.Paused;
                        if (capture.Paused) capture.Engine.InterruptInitiative(agentWorld.Now);
                    }).Margin=new Thickness(10,0,7,10);
                    agentCards.Add(new AgentCard { Agent=a,Face=face,Expressions=expressions,Details=cardDetails });
                }
                nextAgentRead=0;
            };
            ActionButton(actions,"Nuevo agente",()=>
            {
                if(agentWorld.Agents.Count>=AgentWorld.Capacity) { MessageBox.Show(panel,"El máximo es de seis agentes."); return; }
                var draft=EditAgent(new AgentDefinition { Name="Bali "+(agentWorld.Agents.Count+1),Seed=74+agentWorld.Agents.Count });
                if(draft!=null) { agentWorld.Add(draft); rebuild(); agentSelection.SelectedIndex=agentWorld.Agents.Count-1; }
            });
            ActionButton(actions,"Configurar seleccionado",()=>
            {
                var selected=agentSelection.SelectedItem as Agent;
                if (selected==null) return;
                var draft=EditAgent(selected.Definition);
                if (draft==null) return;
                agentWorld.Remove(selected);
                agentWorld.Add(draft);
                rebuild();
                agentSelection.SelectedIndex=agentWorld.Agents.Count-1;
            });
            ActionButton(actions,"Eliminar seleccionado",()=>
            {
                var selected=agentSelection.SelectedItem as Agent;
                if(selected==null || MessageBox.Show(panel,"¿Eliminar el agente experimental "+selected.Definition.Name+"?","Perfil experimental",
                    MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                agentWorld.Remove(selected);
                rebuild();
            });
            ActionButton(actions,"Reiniciar experimento",()=>
            {
                if(MessageBox.Show(panel,"¿Reiniciar las memorias experimentales de los agentes actuales?","Experimento",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                agentWorld=new AgentWorld(agentWorld.Agents.Select(x=>x.Definition).ToArray(),true);
                experimentReports=null;
                rebuild();
            });
            ActionButton(actions,"Restaurar tres perfiles iniciales",()=>
            {
                if(MessageBox.Show(panel,"¿Reemplazar los perfiles de esta sesión por los tres iniciales?","Experimento",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                agentWorld=new AgentWorld(AgentDefinition.Defaults(),true);
                experimentReports=null;
                rebuild();
            });
            var details=Label("");
            page.Children.Add(details);
            ActionButton(page,"Ver memoria, decisiones y afinidades",()=>
            {
                var a=agentSelection.SelectedItem as Agent;
                if(a==null) return;
                var report=agentWorld.Report(agentScenario);
                var one=new AgentReport { Agents=report.Agents.Where(r=>r.Definition.Name==a.Definition.Name).ToList() };
                details.Text=one.Summary()+"\nÚltima decisión: "+a.Engine.Profile.LastDecision+" · "+a.Engine.Profile.LastReason+
                    "\n"+String.Join("\n",a.Settings.Memory.Cognitive.Recent.AsEnumerable().Reverse().Take(6).Select(x=>x.Action+" · "+x.Context+" → "+x.Result));
            });
            agentHistory=new Canvas { Width=365,Height=82,HorizontalAlignment=HorizontalAlignment.Left,Background=canvas,ClipToBounds=true };
            page.Children.Add(Label("Evolución del agente seleccionado · requiere dos días observados"));
            page.Children.Add(agentHistory);
            Heading(page,"Simulación acelerada");
            var config=new WrapPanel();
            page.Children.Add(config);
            var speed=new ComboBox { ItemsSource=new[] {"1×","60×","600×"},Width=74,SelectedIndex=agentRate==600?2:agentRate==60?1:0 };
            speed.SelectionChanged+=(s,e)=>{ if(speed.SelectedIndex>=0) agentRate=new[]{1,60,600}[speed.SelectedIndex]; };
            config.Children.Add(Label("Reloj en directo"));
            config.Children.Add(speed);
            var scenario=new ComboBox { ItemsSource=Enum.GetValues(typeof(EnvironmentPattern)),Width=130,SelectedItem=agentScenario,Margin=new Thickness(15,0,0,0) };
            scenario.SelectionChanged+=(s,e)=>{ if(scenario.SelectedItem!=null) agentScenario=(EnvironmentPattern)scenario.SelectedItem; };
            config.Children.Add(scenario);
            var duration=new ComboBox { ItemsSource=new[] {"24 horas","7 días","30 días","90 días"},SelectedIndex=1,Width=105,Margin=new Thickness(15,0,0,0) };
            config.Children.Add(duration);
            var matched=new CheckBox { Content="Igualar rasgos iniciales",Foreground=ink,Margin=new Thickness(0,8,0,5) };
            page.Children.Add(matched);
            var compare=new CheckBox { Content="Comparar con política 1.1 (sin consecuencias)",Foreground=ink,IsChecked=true,Margin=new Thickness(0,5,0,6) };
            page.Children.Add(compare);
            var results=Label("");
            var bars=new StackPanel { Width=365,HorizontalAlignment=HorizontalAlignment.Left };
            Button run=null;
            run=ActionButton(page,"Ejecutar experimento",async ()=>
            {
                if(agentWorld.Agents.Count==0) { results.Text="Crea al menos un agente."; return; }
                var definitions=agentWorld.Agents.Select(x=>x.Definition.Copy()).ToArray();
                if(matched.IsChecked==true)
                {
                    foreach(var d in definitions) {d.Curious=d.Explore=d.Social=d.Calm=d.Active=.5; d.Autonomy=.55;}
                }
                int days=new[]{1,7,30,90}[duration.SelectedIndex];
                var context=agentScenario;
                bool baseline=compare.IsChecked==true;
                run.IsEnabled=false;
                results.Text="Ejecutando sobre copias separadas…";
                try
                {
                    var report=await Task.Run(()=>
                    {
                        var enabled=AgentWorld.Experiment(definitions,days,context,true);
                        return baseline?new[]{enabled,AgentWorld.Experiment(definitions,days,context,false),
                            AgentWorld.Experiment(definitions,days,context,true,false)}:new[]{enabled};
                    });
                    experimentReports=report;
                    bars.Children.Clear();
                    var selected=agentSelection.SelectedItem as Agent;
                    string displayed=selected==null?definitions[0].Name:selected.Name;
                    foreach(var policy in report)
                    {
                        var profile=policy.Agents.FirstOrDefault(a=>a.Definition.Name==displayed)??policy.Agents[0];
                        if(profile.FeedbackOpportunities==0) continue;
                        double ratio=profile.NegativeFeedback/(double)profile.FeedbackOpportunities;
                        string model=policy.Learning?"1.2 con aprendizaje":policy.Cognitive?"1.2 sin consecuencias":"1.1";
                        if(bars.Children.Count==0) bars.Children.Add(Label("Proporción de negativas · "+profile.Definition.Name));
                        bars.Children.Add(Label(model+" · "+Percent(ratio)));
                        bars.Children.Add(new ProgressBar { Height=6,Maximum=1,Value=ratio,Foreground=ColorOf("#49BDD9"),
                            Background=button,Margin=new Thickness(0,0,0,8) });
                    }
                    results.Text=String.Join("\n\n",report.Select(x=>(x.Learning?"Política 1.2 con aprendizaje":
                        x.Cognitive?"Política 1.2 sin consecuencias":"Política 1.1")+" · "+days+" días · "+x.Scenario+"\n"+x.Summary()));
                }
                catch(Exception ex) { results.Text="No se pudo ejecutar el experimento."; store.Log(ex.Message); }
                finally { run.IsEnabled=true; }
            });
            ActionButton(page,"Guardar resultados",()=>
            {
                try
                {
                    string folder=Path.Combine(store.Folder,"experimentos");
                    Directory.CreateDirectory(folder);
                    string file=Path.Combine(folder,"bali-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");
                    using(var stream=new FileStream(file,FileMode.CreateNew))
                        new DataContractJsonSerializer(typeof(AgentReport[])).WriteObject(stream,experimentReports??new[]{agentWorld.Report(agentScenario)});
                    MessageBox.Show(panel,file,"Resultados guardados");
                }
                catch(Exception ex) { MessageBox.Show(panel,ex.Message,"Resultados"); }
            });
            page.Children.Add(bars);
            page.Children.Add(results);
            rebuild();
        }

        AgentDefinition EditAgent(AgentDefinition original)
        {
            var value=original.Copy();
            var dialog=new Window { Title="Perfil experimental",Owner=panel,Width=480,Height=540,Background=surface,Foreground=ink,
                WindowStartupLocation=WindowStartupLocation.CenterOwner };
            var body=new StackPanel { Margin=new Thickness(18) };
            dialog.Content=new ScrollViewer { Content=body };
            body.Children.Add(Label("Nombre"));
            var name=new TextBox { Text=value.Name,MaxLength=42 };
            body.Children.Add(name);
            body.Children.Add(Label("Semilla aleatoria"));
            var seed=new TextBox { Text=value.Seed.ToString(),MaxLength=12 };
            body.Children.Add(seed);
            var style=new ComboBox { ItemsSource=Enum.GetValues(typeof(FaceStyle)),SelectedItem=value.Style,Width=160,
                HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,8) };
            body.Children.Add(style);
            Range(body,"Sociabilidad inicial",0,1,value.Social,x=>value.Social=x,Percent);
            Range(body,"Curiosidad inicial",0,1,value.Curious,x=>value.Curious=x,Percent);
            Range(body,"Actividad inicial",0,1,value.Active,x=>value.Active=x,Percent);
            Range(body,"Exploración inicial",0,1,value.Explore,x=>value.Explore=x,Percent);
            Range(body,"Tranquilidad inicial",0,1,value.Calm,x=>value.Calm=x,Percent);
            Range(body,"Autonomía",0,1,value.Autonomy,x=>value.Autonomy=x,Percent);
            var error=Label("");
            body.Children.Add(error);
            ActionButton(body,"Aplicar perfil e iniciar de nuevo",()=>
            {
                int number;
                if(!int.TryParse(seed.Text,out number) || String.IsNullOrWhiteSpace(name.Text) ||
                    agentWorld.Agents.Any(a=>a.Definition!=original && a.Definition.Name.Equals(name.Text.Trim(),StringComparison.OrdinalIgnoreCase)))
                { error.Text="Usa un nombre único y una semilla entera válida."; return; }
                value.Name=name.Text.Trim(); value.Seed=number; value.Style=(FaceStyle)style.SelectedItem;
                dialog.DialogResult=true;
            });
            return dialog.ShowDialog()==true?value:null;
        }
    }
}

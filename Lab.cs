using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        const int LifeTab=4;

        void AdaptiveLab(Panel page)
        {
            Heading(page,"Vida artificial");
            var summary=Label("",16);
            page.Children.Add(summary);
            var refreshCognition=AddCognition(page);
            Range(page,"Autonomía",0,1,settings.Autonomy,n=>{settings.Autonomy=n; if(n==0) destination=null;},Percent);
            Range(page,"Pausa entre invitaciones",12,300,settings.InitiativeCooldown,n=>settings.InitiativeCooldown=n,n=>n.ToString("0")+" s");
            Toggle(page,"Aprender de mis hábitos",settings.Learning,n=>settings.Learning=n);
            ActionButton(page,"Restablecer aprendizaje",()=>
            {
                if (MessageBox.Show(panel,"¿Borrar los hábitos y reiniciar los rasgos aprendidos?","Aprendizaje",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                life.ResetLearning();
                Save();
            });
            Heading(page,"Necesidades y estados simulados");
            var names=new[] {"Energía","Estimulación","Atención","Necesidad de descanso","Necesidad de interacción","Ánimo","Curiosidad","Satisfacción","Activación"};
            var bars=new ProgressBar[names.Length];
            var labels=new TextBlock[names.Length];
            for(int i=0;i<names.Length;i++)
            {
                labels[i]=Label(names[i]);
                page.Children.Add(labels[i]);
                bars[i]=new ProgressBar { Maximum=1,Height=5,Foreground=ColorOf("#41BDE8"),Background=button,Margin=new Thickness(0,0,0,8) };
                page.Children.Add(bars[i]);
            }
            Heading(page,"Última decisión");
            var decision=Label("");
            var candidates=Label("");
            page.Children.Add(decision);
            page.Children.Add(candidates);
            Heading(page,"Hábitos por hora local");
            var habits=Label("");
            page.Children.Add(habits);
            Heading(page,"Rasgos e historia");
            var traits=Label("");
            var history=new Canvas { Width=365, Height=85, HorizontalAlignment=HorizontalAlignment.Left, Background=canvas, ClipToBounds=true };
            page.Children.Add(traits);
            page.Children.Add(history);
            var chartNote=Label("");
            page.Children.Add(chartNote);
            Heading(page,"Memoria episódica");
            var events=Label("");
            page.Children.Add(events);
            AddSimulation(page);
            AddProactiveLab(page);

            var updates=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
            EventHandler refresh=(s,e)=>
            {
                if (tabs.SelectedIndex!=LifeTab) return;
                var m=settings.Memory;
                refreshCognition();
                var p=life.Profile;
                var t=p.Traits;
                summary.Text=String.Format("{0} · {1}\n{2} días desde {3:d}{4}\n{5} días observados · {6:0.0} h de ejecución · {7:0.0} h visibles\n{8} interacciones · {9} decisiones autónomas",
                    settings.Mode,life.Current,p.AgeDays(DateTime.UtcNow),p.KnownSince.ToLocalTime(),
                    p.OriginalUnknown?" (fecha original desconocida)":"",p.Days,p.RuntimeSeconds/3600,m.ActiveSeconds/3600,m.Interactions,p.Decisions);
                double[] values={m.Energy,m.Stimulation,m.Attention,m.Rest,p.InteractionNeed,m.Mood,m.Curiosity,p.Satisfaction,p.Activation};
                for(int i=0;i<bars.Length;i++) { bars[i].Value=values[i]; labels[i].Text=names[i]+" · "+Percent(values[i]); }
                decision.Text=String.IsNullOrEmpty(p.LastDecision)?"Esperando la primera evaluación.":p.LastDecision+" · "+p.LastReason;
                candidates.Text=String.Join("\n",life.Candidates.Select(c=>c.Name+" · "+(c.Score<0?c.Reason:
                    c.Score.ToString("0.00")+" · "+c.Reason+(c.Forecast==null?"":" · "+c.Forecast.Describe()))));
                var observed=p.Hours.Select((h,i)=>new { h,i }).Where(x=>x.h.Samples>0).ToArray();
                habits.Text=String.Format("{0}\nInfluencia circadiana actual: {1:+0.00;-0.00;0.00}\n{2}",p.HabitSummary(),life.CircadianBias,
                    String.Join("\n",observed.Select(x=>String.Format("{0:00}:00 · {1} muestras · {2:P0} confianza · {3:P0} actividad · modo {4} · {5} contactos · {6}/{7} respuestas",
                    x.i,x.h.Samples,x.h.Confidence,(double)x.h.Active/x.h.Samples,
                    (Mode)Array.IndexOf(x.h.Modes,x.h.Modes.Max()),x.h.Interactions,x.h.Answered,x.h.Invited))));
                traits.Text=String.Format("Sociabilidad {0:P0} · Curiosidad habitual {1:P0} · Actividad {2:P0}\nExploración {3:P0} · Tranquilidad {4:P0}",
                    t.Social,t.Curious,t.Active,t.Explore,t.Calm);
                DrawTraitHistory(history,p);
                chartNote.Text=p.History.Count(d=>d.Values!=null)<2?"La gráfica necesita al menos dos días con aprendizaje.":
                    "Cian: sociabilidad · Azul: curiosidad · Verde: actividad · Lila: exploración · Gris: tranquilidad\n"+
                    p.History[0].Date+" — "+p.History[p.History.Count-1].Date+" · escala vertical 0–100 %";
                events.Text=String.Join("\n",p.Episodes.AsEnumerable().Reverse().Take(12).Select(record=>
                    String.Format("{0:g} · {1} · relevancia {2}/3 · {3}{4}",record.Date.ToLocalTime(),record.Kind,record.Relevance,record.Description,
                    record.Count>1?" ×"+record.Count:"")));
            };
            updates.Tick+=refresh;
            tabs.SelectionChanged+=(s,e)=>{ if(e.Source==tabs) refresh(s,e); };
            panel.Closed+=(s,e)=>updates.Stop();
            updates.Start();
        }

        void DrawTraitHistory(Canvas chart,AdaptiveProfile profile)
        {
            chart.Children.Clear();
            var observed=profile.History.Where(d=>d.Values!=null).ToArray();
            if (observed.Length<2) return;
            string[] colors={"#39BFEF","#6796FF","#58CEB2","#C2A4FF","#94A7B8"};
            for(int trait=0;trait<colors.Length;trait++)
            {
                var line=new Polyline { Stroke=ColorOf(colors[trait]),StrokeThickness=1.7 };
                for(int i=0;i<observed.Length;i++)
                    line.Points.Add(new Point(5+i*355.0/(observed.Length-1),5+75*(1-observed[i].Values[trait])));
                chart.Children.Add(line);
            }
        }

        void AddSimulation(Panel page)
        {
            Heading(page,"Simulador experimental");
            page.Children.Add(Label("Perfil nuevo e independiente. El resultado no se aplica a la memoria de Bali."));
            var options=new WrapPanel();
            page.Children.Add(options);
            var duration=new ComboBox { ItemsSource=new[] {"24 horas","7 días","30 días","90 días"},SelectedIndex=1,Width=110,Margin=new Thickness(0,4,9,6) };
            options.Children.Add(duration);
            var seed=new TextBox { Text="71",Width=78,Margin=new Thickness(0,4,9,6) };
            options.Children.Add(Label("Semilla"));
            options.Children.Add(seed);
            int frequency=8, start=9;
            double activity=.5;
            bool varied=false, absence=false, focus=true;
            Range(page,"Interacciones esperadas por día",0,40,frequency,n=>frequency=(int)Math.Round(n),n=>n.ToString("0"));
            Range(page,"Hora inicial de actividad",0,23,start,n=>start=(int)Math.Round(n),n=>n.ToString("0")+":00");
            Range(page,"Actividad del perfil simulado",0,1,activity,n=>activity=n,Percent);
            foreach (var item in new[] {
                new { Text="Variar horario",Initial=false, Set=(Action<bool>)(n=>varied=n) },
                new { Text="Ausencia prolongada",Initial=false, Set=(Action<bool>)(n=>absence=n) },
                new { Text="Periodos de concentración",Initial=true, Set=(Action<bool>)(n=>focus=n) } })
            {
                var box=new CheckBox { Content=item.Text,IsChecked=item.Initial,Foreground=ink,Margin=new Thickness(0,4,0,4) };
                box.Click+=(s,e)=>item.Set(box.IsChecked==true);
                page.Children.Add(box);
            }
            var result=Label("");
            var chart=new Canvas { Width=365, Height=85, HorizontalAlignment=HorizontalAlignment.Left, Background=canvas, ClipToBounds=true };
            Button run=null;
            run=ActionButton(page,"Simular",async ()=>
            {
                int number;
                if (!int.TryParse(seed.Text,out number)) { result.Text="Introduce una semilla entera válida."; return; }
                int days=new[]{1,7,30,90}[duration.SelectedIndex];
                int contacts=frequency, hour=start;
                bool vary=varied, absent=absence, concentrate=focus;
                double pace=activity;
                run.IsEnabled=false;
                result.Text="Ejecutando simulación…";
                try
                {
                    var simulation=await Task.Run(()=>LifeSimulator.Run(days,number,contacts,hour,vary,absent,concentrate,pace));
                    result.Text=simulation.ToString();
                    DrawTraitHistory(chart,simulation.Settings.Memory.Adaptive);
                }
                catch (Exception ex) { result.Text="La simulación no pudo completarse."; store.Log(ex.Message); }
                finally { run.IsEnabled=true; }
            });
            page.Children.Add(result);
            page.Children.Add(chart);
        }
    }
}

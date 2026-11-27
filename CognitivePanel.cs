using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        static readonly KeyValuePair<string,Outcome>[] FeedbackActions={
            new KeyValuePair<string,Outcome>("Me gustó esta iniciativa",Outcome.Positiva),
            new KeyValuePair<string,Outcome>("Prefiero que no hagas esto",Outcome.Negativa),
            new KeyValuePair<string,Outcome>("Menos frecuente",Outcome.MenosFrecuente),
            new KeyValuePair<string,Outcome>("Más frecuente",Outcome.MásFrecuente)
        };
        TextBlock feedbackStatus;
        void SubmitFeedback(Outcome outcome)
        {
            bool ok=life.Feedback(outcome,DateTime.UtcNow);
            if(ok) Save();
            if(feedbackStatus!=null) feedbackStatus.Text=ok?"Preferencia registrada.":"No hay una iniciativa reciente que se pueda valorar.";
        }
        Action AddCognition(Panel page)
        {
            Heading(page,"Aprendizaje por experiencias");
            var goal=Label("");
            var forecast=Label("");
            var experience=Label("");
            var preferences=Label("");
            var waiting=Label("");
            feedbackStatus=Label("");
            page.Children.Add(goal);
            page.Children.Add(waiting);
            var actions=new WrapPanel();
            page.Children.Add(actions);
            foreach(var option in FeedbackActions)
            {
                var captured=option;
                ActionButton(actions,captured.Key,()=>SubmitFeedback(captured.Value));
            }
            page.Children.Add(feedbackStatus);
            Toggle(page,"Aprender por consecuencias",settings.ConsequenceLearning,enabled=>life.ConsequenceEnabled(enabled));
            ActionButton(page,"Restablecer asociaciones",()=>
            {
                if (MessageBox.Show(panel,"¿Borrar preferencias y experiencias asociativas?","Aprendizaje",
                    MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                life.ResetConsequences();
                Save(true);
            });
            page.Children.Add(forecast);
            page.Children.Add(experience);
            page.Children.Add(preferences);
            panel.Closed+=(s,e)=>feedbackStatus=null;
            return ()=>
            {
                var current=life.Goal;
                goal.Text=current==null?"Sin motivación dominante.":String.Format("Objetivo · {0} · intensidad {1:P0} · prioridad {2:P0}",current.Name,current.Intensity,current.Priority);
                waiting.Text=life.PendingInitiative==null?"Sin iniciativa reciente para valorar.":"Iniciativa disponible · "+life.PendingInitiative;
                foreach(Button control in actions.Children) control.IsEnabled=life.PendingInitiative!=null;
                var score=life.LastInitiative;
                forecast.Text=score==null?"Sin predicción registrada en esta sesión.":
                    score.Name+" · "+life.LastContext+"\n"+
                    (score.Forecast==null?"Predicción desactivada.":score.Forecast.Describe())+
                    String.Format("\nUtilidad {0:0.00} · motivación {1:+0.00;-0.00;0.00} · preferencia {2:+0.00;-0.00;0.00} · exploración {3:0.00} · coste {4:0.00}",
                    score.BaseUtility,score.Motivation,score.Learned,score.Intrinsic,score.Interruption);
                var memory=settings.Memory.Cognitive;
                experience.Text=memory.Recent.Count==0?"Sin resultados observados.":"Últimos resultados\n"+
                    String.Join("\n",memory.Recent.AsEnumerable().Reverse().Take(6).Select(e=>
                        String.Format("{0:t} · {1} · {2} → {3} · recompensa {4:+0.00;-0.00;0.00} · relevancia {5}/3",
                            e.Date.ToLocalTime(),e.Action,e.Context,e.Result,e.Reward,e.Relevance)));
                preferences.Text=memory.Preference()+"\n"+String.Join("\n",memory.Associations.Where(a=>a.Context=="Todas").OrderByDescending(a=>a.Evidence).Take(8).Select(a=>
                    String.Format("{0} · preferencia {1:+0.00;-0.00;0.00} · confianza registrada {2:P0} · {3} observaciones",
                        a.Action,a.Value,a.Confidence,a.Encounters)));
            };
        }
    }
}

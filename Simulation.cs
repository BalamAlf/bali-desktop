using System;
using System.Globalization;

namespace BaliDesktop
{
    public sealed class SimulationResult
    {
        public readonly Settings Settings;
        public SimulationResult(Settings settings) { Settings=settings; }
        public override string ToString()
        {
            var m=Settings.Memory;
            var p=m.Adaptive;
            var t=p.Traits;
            return String.Format(CultureInfo.InvariantCulture,
                "{0} días observados · {1:0.0} h simuladas · {2} interacciones · {3} decisiones\n"+
                "Energía {4:P0} · ánimo {5:P0} · satisfacción {6:P0}\n"+
                "Sociabilidad {7:P1} · curiosidad {8:P1} · actividad {9:P1} · exploración {10:P1} · tranquilidad {11:P1}\n{12}",
                p.Days,p.RuntimeSeconds/3600,m.Interactions,p.Decisions,m.Energy,m.Mood,p.Satisfaction,
                t.Social,t.Curious,t.Active,t.Explore,t.Calm,p.HabitSummary());
        }
    }

    public static class LifeSimulator
    {
        public static SimulationResult Run(int days,int seed,int interactions,int startHour,bool variable,bool absence,bool focus,double activity)
        {
            var s=new Settings { Activity=activity };
            var engine=new BaliLifeEngine(s,seed,false);
            var rng=new Random(seed);
            // Preserve the host's civil-time conversion while avoiding a DST transition in the experiment.
            var start=new DateTime(2025,1,1,0,0,0,DateTimeKind.Local).ToUniversalTime();
            for(int day=0;day<days;day++)
            {
                if (absence && days>1 && day>=days/3 && day<Math.Max(days/3+1,2*days/3)) continue;
                int shift=(startHour+(variable?rng.Next(-3,4):0)+24)%24;
                for(int second=0;second<24*3600;second+=10)
                {
                    DateTime stamp=start.AddDays(day).AddSeconds(second);
                    int relative=(second/3600-shift+24)%24;
                    bool active=relative<(absence && days==1?3:9);
                    Mode mode=active && focus && relative<4 ? Mode.Concentrado : Mode.Libre;
                    if (mode!=s.Mode) { s.Mode=mode; engine.ModeChanged(); engine.RecordMode(mode,stamp); }
                    engine.Tick(10,active?0:3600,active && rng.NextDouble()<.1,true,stamp);
                    if (active && rng.NextDouble()<interactions/(absence && days==1?1080.0:3240.0))
                        engine.Interact(stamp,false);
                }
            }
            return new SimulationResult(s);
        }
    }
}

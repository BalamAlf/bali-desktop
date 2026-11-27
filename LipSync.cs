using System;
using System.Diagnostics;

namespace BaliDesktop
{
    public enum MouthShape { Cerrada, Pequeña, Media, Amplia, Redondeada, Horizontal, Sonrisa }

    public struct MouthPose
    {
        public readonly double Width, Opening, Smile;
        public MouthPose(double width,double opening,double smile) { Width=width; Opening=opening; Smile=smile; }
        public static MouthPose Shape(MouthShape shape)
        {
            switch(shape)
            {
                case MouthShape.Pequeña: return new MouthPose(.87,.23,.30);
                case MouthShape.Media: return new MouthPose(.92,.48,.25);
                case MouthShape.Amplia: return new MouthPose(.93,.82,.17);
                case MouthShape.Redondeada: return new MouthPose(.62,.62,.04);
                case MouthShape.Horizontal: return new MouthPose(1.12,.20,.22);
                case MouthShape.Sonrisa: return new MouthPose(1.10,.29,.77);
                default: return new MouthPose(.87,0,.32);
            }
        }
        internal static MouthPose Blend(MouthPose from,MouthPose to,double mix)
        {
            mix=Math.Max(0,Math.Min(1,mix));
            mix=mix*mix*(3-2*mix);
            return new MouthPose(from.Width+(to.Width-from.Width)*mix,
                from.Opening+(to.Opening-from.Opening)*mix,from.Smile+(to.Smile-from.Smile)*mix);
        }
    }

    public sealed class LipSync
    {
        static readonly MouthShape[] Visemes={
            MouthShape.Cerrada,MouthShape.Media,MouthShape.Amplia,MouthShape.Redondeada,MouthShape.Media,MouthShape.Media,
            MouthShape.Sonrisa,MouthShape.Redondeada,MouthShape.Redondeada,MouthShape.Amplia,MouthShape.Redondeada,MouthShape.Sonrisa,
            MouthShape.Pequeña,MouthShape.Pequeña,MouthShape.Pequeña,MouthShape.Horizontal,MouthShape.Horizontal,MouthShape.Pequeña,
            MouthShape.Horizontal,MouthShape.Pequeña,MouthShape.Pequeña,MouthShape.Cerrada
        };
        readonly MouthPose closed=MouthPose.Shape(MouthShape.Cerrada);
        double anchor=Double.NaN, started, ends, pauseAt, lastAudio;
        MouthPose from, target, next;
        bool hasCue;
        public bool Playing { get; private set; }
        public bool Paused { get; private set; }
        public bool HasVisemes { get; private set; }
        public int LastViseme { get; private set; }
        public MouthShape CurrentShape { get; private set; }
        public double Smoothness=.55, Intensity=.75;
        public static double Now { get { return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency; } }
        public static MouthShape VisemeShape(int id) { return id>=0 && id<Visemes.Length?Visemes[id]:MouthShape.Cerrada; }

        public void Start()
        {
            End();
            Playing=true;
            HasVisemes=false;
            LastViseme=-1;
            lastAudio=-1;
        }
        public void End()
        {
            Playing=Paused=hasCue=false;
            anchor=Double.NaN;
            CurrentShape=MouthShape.Cerrada;
        }
        public void Pause(double now)
        {
            if(!Playing || Paused) return;
            Paused=true;
            pauseAt=now;
            CurrentShape=MouthShape.Cerrada;
        }
        public void Resume(double now)
        {
            if(!Paused) return;
            Paused=false;
            double delta=Math.Max(0,now-pauseAt);
            if(!Double.IsNaN(anchor)) anchor+=delta;
            started+=delta;
            ends+=delta;
            from=closed;
        }
        public void Viseme(int id,int following,double position,double duration,double observed)
        {
            if(!Playing || Paused || id<0 || id>=Visemes.Length) return;
            if(!HasVisemes) lastAudio=-1;
            HasVisemes=true;
            LastViseme=id;
            Cue(VisemeShape(id),VisemeShape(following),position,duration,observed);
        }
        public void Phoneme(string phoneme,double position,double duration,double observed)
        {
            if(!Playing || Paused || HasVisemes || String.IsNullOrEmpty(phoneme)) return;
            var token=phoneme.Trim().ToLowerInvariant();
            if(token.Length==0) return;
            MouthShape shape;
            if(token=="_" || token=="sil" || token=="sp") shape=MouthShape.Cerrada;
            else if(token.IndexOfAny(new[]{'o','u','ɔ'})>=0) shape=MouthShape.Redondeada;
            else if(token.IndexOfAny(new[]{'a','ɑ','æ'})>=0) shape=MouthShape.Amplia;
            else if(token.IndexOfAny(new[]{'e','ɛ','ə'})>=0) shape=MouthShape.Media;
            else if(token.IndexOfAny(new[]{'i','j','ɪ'})>=0) shape=MouthShape.Sonrisa;
            else if(token=="p" || token=="b" || token=="m") shape=MouthShape.Cerrada;
            else if(token.Length==1 && Char.IsLetter(token[0])) shape=MouthShape.Pequeña;
            else return;
            Cue(shape,MouthShape.Cerrada,position,duration,observed);
        }
        void Cue(MouthShape shape,MouthShape following,double position,double duration,double observed)
        {
            if(position<0 || position<lastAudio) return;
            bool first=Double.IsNaN(anchor);
            double measured=observed-position;
            anchor=first?measured:anchor+Math.Max(-.025,Math.Min(.025,measured-anchor))*.24;
            var previous=SampleRaw(observed);
            CurrentShape=shape;
            started=Math.Min(observed+.018,anchor+position);
            ends=started+Math.Max(.018,Math.Min(4,duration));
            from=previous;
            target=MouthPose.Shape(shape);
            next=MouthPose.Shape(following);
            hasCue=true;
            lastAudio=position;
        }
        public MouthPose Sample(double now)
        {
            var pose=SampleRaw(now);
            double intensity=Math.Max(0,Math.Min(1,Intensity));
            return new MouthPose(.90+(pose.Width-.90)*intensity,pose.Opening*intensity,pose.Smile);
        }
        MouthPose SampleRaw(double now)
        {
            if(!Playing || Paused || !hasCue) return closed;
            double smooth=Math.Max(0,Math.Min(1,Smoothness));
            double attack=Math.Min((ends-started)*.42,.011+smooth*.048);
            double release=.010+smooth*.034;
            MouthPose pose;
            if(now<started+attack) pose=MouthPose.Blend(from,target,(now-started)/attack);
            else if(now>=ends) pose=MouthPose.Blend(target,closed,(now-ends)/release);
            else
            {
                double anticipation=Math.Min(.028,(ends-started)*.20);
                pose=target.Opening>0 && next.Opening>0?
                    MouthPose.Blend(target,next,.32*Math.Max(0,(now-(ends-anticipation))/anticipation)):target;
            }
            return pose;
        }
    }

    sealed class SpeechSignals
    {
        public Action Started;
        public Action<int,int,double,double,double> Viseme;
        public Action<string,double,double,double> Phoneme;
        public Action<double> Progress;
        public Action<bool,bool> Completed;
    }
}

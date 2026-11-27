using System;
using System.Windows;
using System.Windows.Media;

namespace BaliDesktop
{
    public sealed class BaliExpressionEngine
    {
        // Apertura izquierda/derecha, ancho, inclinación, boca, apertura de boca y luz.
        static readonly double[][] Poses = {
            new[]{1.0,1,1,0,1,0,.8}, new[]{.78,.78,1.03,-2,1.6,.35,1},
            new[]{1.17,.88,1,-7,.52,0,.93}, new[]{.59,.59,1.08,0,1.3,.15,.85},
            new[]{1.36,1.36,.94,0,0,.87,1}, new[]{.71,1.1,.92,8,-.4,0,.77},
            new[]{.32,.27,1,0,.3,.44,.49}, new[]{.63,.63,1,-9,.2,0,.61},
            new[]{.98,.47,1,5,1.32,.14,.92}, new[]{1.07,1.07,1,0,.46,0,.88},
            new[]{.81,.9,.91,4,0,.12,.7}, new[]{1.17,1.17,1.04,0,1.58,.36,1},
            new[]{.09,.09,.96,0,.42,0,.39}, new[]{.52,.54,1,3,-.1,0,.55},
            new[]{1.24,1.24,1.04,-3,1.5,.75,1}
        };
        readonly Random random;
        readonly double[] pose = (double[])Poses[0].Clone();
        double time, blinkAt = 3, lookX, lookY;
        public BaliExpressionEngine(int seed) { random = new Random(seed); }
        public double[] Pose { get { return pose; } }
        public double X { get { return lookX; } }
        public double Y { get { return lookY; } }
        public double Breath { get { return Math.Sin(time*1.75); } }
        public double Blink
        {
            get
            {
                double phase = (time - blinkAt) / .23;
                return phase < 0 || phase > 1 ? 1 : Math.Max(.09, 1-Math.Sin(phase*Math.PI));
            }
        }
        public void Tick(double dt, Expression expression, double x, double y, bool tracking, bool quiet)
        {
            time += dt;
            if (time > blinkAt + .25) blinkAt = time + 2.7 + random.NextDouble()*(quiet ? 11 : 5);
            var target = Poses[(int)expression];
            double ease = 1-Math.Exp(-dt*(expression == Expression.Sorpresa ? 18 : quiet ? 5 : 9));
            for (int i = 0; i < pose.Length; i++) pose[i] += (target[i]-pose[i])*ease;
            double wander = quiet ? .1 : expression == Expression.Curiosidad ? .62 : .33;
            lookX += ((tracking ? x : Math.Sin(time*.62)*wander)-lookX)*ease;
            lookY += ((tracking ? y : Math.Sin(time*.43)*wander*.55)-lookY)*ease;
        }
    }

    public sealed class Face : FrameworkElement
    {
        readonly BaliExpressionEngine engine;
        static readonly Brush Shell = Gradient("#264766","#091829",65);
        static readonly Brush Glass = Gradient("#102D46","#070E1C",82);
        static readonly Brush Rim = Gradient("#527B96","#182A42",70);
        static readonly Brush Eyes = Gradient("#E2FBFF","#46BBF0",78);
        static readonly Brush Aura = BrushOf("#164467");
        static readonly Brush Iris = Gradient("#258ABA","#12628F",80);
        static readonly Brush White = BrushOf("#F0FCFF");
        static readonly Brush Accent = BrushOf("#65BCE4");
        static readonly Brush Reflection = BrushOf("#2094CDED");
        static readonly Brush Shade = BrushOf("#50020914");
        static readonly Brush MinimalShell = Gradient("#223C53","#0D1826",76);
        static readonly Brush MinimalGlass = Gradient("#132737","#091521",76);
        static readonly Brush MinimalRim = Gradient("#435A70","#1A2839",70);
        static readonly Brush MinimalEyes = Gradient("#B5EAF4","#5DAFCB",87);
        static readonly Brush MinimalMouth = BrushOf("#88BDCD");
        static readonly Pen MinimalLip = CreateLip();
        static readonly Pen ClassicLip = CreateLip(Accent,3.5);
        FaceStyle style;
        internal MouthPose? SpeechMouth { get; set; }
        public FaceStyle StyleVariant
        {
            get { return style; }
            set { if (style != value) { style = value; InvalidateVisual(); } }
        }
        static Pen CreateLip() { return CreateLip(MinimalMouth,1.9); }
        static Pen CreateLip(Brush brush,double width)
        {
            var pen = new Pen(brush,width) { StartLineCap=PenLineCap.Round, EndLineCap=PenLineCap.Round };
            pen.Freeze();
            return pen;
        }
        static Brush BrushOf(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        static Brush Gradient(string top, string bottom, double angle)
        {
            var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top),(Color)ColorConverter.ConvertFromString(bottom),angle);
            brush.Freeze();
            return brush;
        }
        public Face(BaliExpressionEngine engine,FaceStyle style) { this.engine = engine; this.style = style; }
        protected override void OnRender(DrawingContext dc)
        {
            double scale = Math.Min(ActualWidth,ActualHeight)/220;
            if (scale <= 0) return;
            dc.PushTransform(new TranslateTransform((ActualWidth-220*scale)/2,(ActualHeight-220*scale)/2));
            dc.PushTransform(new ScaleTransform(scale,scale));
            if (style == FaceStyle.Minimalista)
            {
                DrawMinimal(dc);
                dc.Pop(); dc.Pop();
                return;
            }
            dc.DrawEllipse(Shade,null,new Point(110,181),82,13);
            dc.PushTransform(new TranslateTransform(0,engine.Breath*1.5));
            dc.PushTransform(new RotateTransform(engine.X*2.7,110,108));
            dc.DrawRoundedRectangle(Rim,null,new Rect(9,32,202,151),51,51);
            dc.DrawRoundedRectangle(Shell,null,new Rect(10,33,200,147),50,50);
            dc.DrawRoundedRectangle(Glass,null,new Rect(16,40,188,128),43,43);
            dc.DrawRoundedRectangle(Reflection,null,new Rect(60,45,100,1.1),1,1);
            var pose = engine.Pose;
            dc.PushOpacity(pose[6]);
            DrawEye(dc,73,99,pose[0],-pose[3],1+engine.X*.08);
            DrawEye(dc,147,99,pose[1],pose[3],1-engine.X*.08);
            double mouthX = 110 + engine.X*3;
            if(SpeechMouth.HasValue) DrawSpeechMouth(dc,mouthX,136,false);
            else if (pose[5] > .20)
                dc.DrawEllipse(Accent,null,new Point(mouthX,137),6+5*Math.Abs(pose[4]),1.5+pose[5]*8);
            else
            {
                var path = new StreamGeometry();
                using (var ctx = path.Open())
                {
                    ctx.BeginFigure(new Point(mouthX-13,135),false,false);
                    ctx.QuadraticBezierTo(new Point(mouthX,135+pose[4]*15),new Point(mouthX+13,135),true,false);
                }
                dc.DrawGeometry(null,ClassicLip,path);
            }
            dc.Pop();
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        }
        void DrawMinimal(DrawingContext dc)
        {
            dc.DrawEllipse(Shade,null,new Point(110,168),81,6);
            dc.PushTransform(new TranslateTransform(0,engine.Breath));
            dc.PushTransform(new RotateTransform(engine.X*1.35,110,110));
            dc.DrawRoundedRectangle(MinimalRim,null,new Rect(11,50,198,119),33,33);
            dc.DrawRoundedRectangle(MinimalShell,null,new Rect(12,51,196,116),32,32);
            dc.DrawRoundedRectangle(MinimalGlass,null,new Rect(17,56,186,104),27,27);
            var pose = engine.Pose;
            dc.PushOpacity(.34 + pose[6]*.60);
            DrawMinimalEye(dc,73,pose[0],-pose[3],1+engine.X*.045);
            DrawMinimalEye(dc,147,pose[1],pose[3],1-engine.X*.045);
            double x=110+engine.X*2.5, y=128+engine.Y, width=7+pose[5]*1.5;
            if(SpeechMouth.HasValue)
            {
                DrawSpeechMouth(dc,x,y,true);
                dc.Pop(); dc.Pop(); dc.Pop();
                return;
            }
            double curve=pose[4]*5.4;
            var path=new StreamGeometry();
            using (var ctx=path.Open())
            {
                ctx.BeginFigure(new Point(x-width,y),false,false);
                ctx.QuadraticBezierTo(new Point(x,y+curve-pose[5]*3),new Point(x+width,y),true,false);
                ctx.QuadraticBezierTo(new Point(x,y+curve+pose[5]*8),new Point(x-width,y),true,false);
            }
            dc.DrawGeometry(null,MinimalLip,path);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        void DrawSpeechMouth(DrawingContext dc,double x,double y,bool minimal)
        {
            var mouth=SpeechMouth.Value;
            double width=mouth.Width*(minimal?9:13);
            double aperture=mouth.Opening*(minimal?12:19);
            double curve=mouth.Smile*(minimal?5:9)+Math.Max(0,engine.Pose[4])*.5;
            var geometry=new StreamGeometry();
            using(var path=geometry.Open())
            {
                path.BeginFigure(new Point(x-width,y),!minimal && aperture>.45,false);
                path.QuadraticBezierTo(new Point(x,y+curve-aperture*.67),new Point(x+width,y),true,false);
                if(aperture>.45)
                    path.QuadraticBezierTo(new Point(x,y+curve+aperture),new Point(x-width,y),true,false);
            }
            dc.DrawGeometry(!minimal && aperture>.45?Accent:null,minimal?MinimalLip:ClassicLip,geometry);
        }
        void DrawMinimalEye(DrawingContext dc,double x,double opening,double angle,double perspective)
        {
            x+=engine.X*8;
            double y=101+engine.Y*4;
            double width=38*engine.Pose[2]*perspective;
            double height=(2+12*Math.Pow(opening,.75))*engine.Blink;
            double radius=Math.Min(4.5,height*.42);
            dc.PushTransform(new RotateTransform(angle*.56,x,y));
            dc.DrawRoundedRectangle(MinimalEyes,null,new Rect(x-width/2,y-height/2,width,height),radius,radius);
            dc.Pop();
        }
        void DrawEye(DrawingContext dc,double x,double y,double opening,double angle,double perspective)
        {
            double width = 39*engine.Pose[2]*perspective;
            double height = Math.Max(2.4,30*opening*engine.Blink);
            x += engine.X*4;
            y += engine.Y*3;
            dc.PushTransform(new RotateTransform(angle,x,y));
            dc.DrawRoundedRectangle(Aura,null,new Rect(x-width/2-3,y-height/2-3,width+6,height+6),12,12);
            var rect = new Rect(x-width/2,y-height/2,width,height);
            dc.DrawRoundedRectangle(Eyes,null,rect,10,10);
            if (height > 9)
            {
                dc.PushClip(new RectangleGeometry(rect,10,10));
                var pupil = new Point(x+engine.X*9,y+engine.Y*5 + (x < 110 ? -.45 : .45));
                dc.DrawEllipse(Iris,null,pupil,7.6,Math.Min(9,height*.37));
                dc.DrawEllipse(White,null,new Point(pupil.X-2,pupil.Y-3),2.2,2.2);
                dc.Pop();
            }
            dc.Pop();
        }
    }
}

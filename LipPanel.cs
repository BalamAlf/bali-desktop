using System;
using System.Windows;
using System.Windows.Controls;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        const int LipTab=9;
        internal const string LipShort="Hola, soy Bali.";
        internal const string LipLong="Hola. Primero abro la boca al hablar. Después hago una pausa, y continúo con calma. La voz y el rostro se acompañan hasta el final.";
        readonly LipSync lips=new LipSync();
        bool speechPending;
        double closeMouthUntil;
        int visemeCount, phonemeCount, wordCount, nextViseme;
        double lastVisemePosition,lastVisemeDuration,wordPosition,nextLipRead;
        string lipStatus="En reposo";
        Face classicLipPreview,minimalLipPreview;
        TextBlock lipRead;
        MouthShape previewShape;
        bool manualLipPreview;

        void ClearSpeechTrace()
        {
            visemeCount=phonemeCount=wordCount=0;
            lastVisemePosition=lastVisemeDuration=wordPosition=0;
            nextViseme=-1;
            nextLipRead=0;
            manualLipPreview=false;
        }
        void FinishSpeech(string status)
        {
            speaking=speechPending=false;
            lips.End();
            lipStatus=status;
            closeMouthUntil=LipSync.Now+.32;
            nextLipRead=0;
            UpdateSpeechMouth();
            face.InvalidateVisual();
            if(conversationFace!=null) conversationFace.InvalidateVisual();
            if(LipLabVisible) { classicLipPreview.InvalidateVisual(); minimalLipPreview.InvalidateVisual(); }
            SetDialogueStatus(null,status);
        }
        void ToggleSpeechPause()
        {
            if(!speaking || !speechOutput.PauseSpeech(!lips.Paused)) return;
            if(lips.Paused)
            {
                lips.Resume(LipSync.Now);
                lipStatus="Reproduciendo";
            }
            else
            {
                lips.Pause(LipSync.Now);
                lipStatus="En pausa";
            }
            UpdateSpeechMouth();
            nextLipRead=0;
        }
        Expression DialogueExpression()
        {
            if(speechPending) return Expression.Procesamiento;
            if(lips.Paused) return Expression.Escucha;
            if(!speaking) return life.Current;
            switch(life.Current)
            {
                case Expression.Alegría: case Expression.Curiosidad: case Expression.Satisfacción: case Expression.Picardía: case Expression.Entusiasmo:
                    return life.Current;
                default: return Expression.Satisfacción;
            }
        }
        bool LipLabVisible { get { return lipRead!=null && panel!=null && tabs.SelectedIndex==LipTab; } }
        void UpdateSpeechMouth()
        {
            double timing=LipSync.Now;
            MouthPose? mouth=null;
            if(speaking) mouth=lips.Sample(timing);
            else if(speechPending || busyConversation || capture!=null || transcription!=null || timing<closeMouthUntil)
                mouth=MouthPose.Shape(MouthShape.Cerrada);
            face.SpeechMouth=mouth;
            if(conversationFace!=null) conversationFace.SpeechMouth=mouth;
            if(!LipLabVisible) return;
            var comparison=manualLipPreview?MouthPose.Shape(previewShape):mouth??MouthPose.Shape(MouthShape.Cerrada);
            classicLipPreview.SpeechMouth=minimalLipPreview.SpeechMouth=comparison;
            if(timing>=nextLipRead)
            {
                nextLipRead=timing+.17;
                string signal=visemeCount!=0?"Visema "+lips.LastViseme+" → "+nextViseme+" · "+
                    lastVisemePosition.ToString("0.000")+" s · duración "+(lastVisemeDuration*1000).ToString("0")+" ms":
                    phonemeCount!=0?"Fonemas disponibles · aún sin visema":"Sin eventos de audio en esta reproducción";
                lipRead.Text=String.Format("{0} · {1:P0} de apertura\n{2}\n{3} visemas · {4} fonemas · {5} palabras · último progreso {6:0.000} s",
                    manualLipPreview?"Previsualización: "+previewShape:lipStatus,comparison.Opening,signal,visemeCount,phonemeCount,wordCount,wordPosition);
            }
        }
        void LipPage(Panel page)
        {
            Heading(page,"Sincronización labial");
            page.Children.Add(Label("Compara los dos estilos mientras Windows reproduce la misma frase local."));
            var comparison=new WrapPanel();
            page.Children.Add(comparison);
            classicLipPreview=AddLipFace(comparison,"Clásico",FaceStyle.Clásico);
            minimalLipPreview=AddLipFace(comparison,"Minimalista",FaceStyle.Minimalista);
            var controls=new WrapPanel();
            page.Children.Add(controls);
            ActionButton(controls,"Frase corta",()=>Play(LipShort));
            ActionButton(controls,"Frase con pausas",()=>Play(LipLong));
            ActionButton(controls,"Pausar / reanudar",ToggleSpeechPause);
            ActionButton(controls,"Detener",StopPlayback);
            lipRead=Label("");
            page.Children.Add(lipRead);
            var previewButtons=new WrapPanel();
            page.Children.Add(Label("Probar forma en la vista previa"));
            page.Children.Add(previewButtons);
            foreach(MouthShape shape in Enum.GetValues(typeof(MouthShape)))
            {
                MouthShape selection=shape;
                ActionButton(previewButtons,shape.ToString(),()=>
                {
                    if(speaking || speechPending) return;
                    previewShape=selection;
                    manualLipPreview=true; nextLipRead=0;
                });
            }
            ActionButton(previewButtons,"Seguir audio",()=>{ manualLipPreview=false; nextLipRead=0; });
            Range(page,"Suavidad",0,1,lips.Smoothness,n=>settings.Intelligence.LipSmoothness=lips.Smoothness=n,Percent);
            Range(page,"Intensidad",0,1,lips.Intensity,n=>settings.Intelligence.LipIntensity=lips.Intensity=n,Percent);
            panel.Closed+=(s,e)=>{ lipRead=null; classicLipPreview=minimalLipPreview=null; manualLipPreview=false; };
        }
        Face AddLipFace(Panel page,string title,FaceStyle style)
        {
            var card=new StackPanel { Width=190,Background=canvas,Margin=new Thickness(0,0,10,10) };
            page.Children.Add(card);
            var name=Label(title,17);
            name.Margin=new Thickness(12,8,0,0);
            card.Children.Add(name);
            var previewFace=new Face(expressions,style) { Width=150,Height=108,IsHitTestVisible=false };
            card.Children.Add(previewFace);
            return previewFace;
        }
    }
}

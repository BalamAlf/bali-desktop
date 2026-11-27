using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        WindowsRecognizer windowsStt;
        readonly WhisperRecognizer whisperStt=new WhisperRecognizer();
        readonly WhisperRecognizer whisperBase=new WhisperRecognizer(true);
        bool sttDemo, compareStt, compareWhisperModels, sttNeedsReview;
        double nextSpeechDisplay;
        string lastSpeechOutcome="Sin prueba", speechDevice="Predeterminado de Windows", speechIntent="", speechParameters="";
        SpeechResult lastSpeech, alternateSpeech;
        TextBlock speechDisplay;
        TextBox speechReference;
        VoiceSilenceDetector lastLevels;
        ISpeechRecognizer CurrentStt
        {
            get
            {
                if(settings.Intelligence.SttProvider!="windows" && PreferredWhisper.Available && !toolsRouter.AwaitsApproval("voz",DateTime.Now) &&
                    !DesktopRequiresReliableReply() && !ComputerAwaiting)
                    return PreferredWhisper;
                return windowsStt;
            }
        }
        WhisperRecognizer PreferredWhisper
        {get{return settings.Intelligence.SttProvider=="whisper-base" && whisperBase.Available?whisperBase:whisperStt;}}
        async Task<SpeechResult> RecognizeLocal(byte[] pcm,bool strict,CancellationToken cancel)
        {
            var selected=CurrentStt;
            try{return await selected.Recognize(pcm,strict,cancel);}
            catch(Exception)
            {
                cancel.ThrowIfCancellationRequested();
                if(selected==windowsStt || !windowsStt.Available)throw;
            }
            var fallback=await windowsStt.Recognize(pcm,strict,cancel);
            fallback.Provider+=" · recuperación tras error de Whisper";
            return fallback;
        }
        static async Task<SpeechResult> RunSpeechDiagnostic(ISpeechRecognizer recognizer,byte[] pcm,CancellationToken cancel)
        {
            try { if(recognizer.Available)return await recognizer.Recognize(pcm,true,cancel); }
            catch(Exception ex)
            {
                cancel.ThrowIfCancellationRequested();
                return new SpeechResult { Provider=recognizer.Name, Rejection="Error local ("+ex.GetType().Name+")." };
            }
            return new SpeechResult { Provider=recognizer.Name, Rejection="Reconocedor no disponible." };
        }
        void SpeechSettings(Panel page)
        {
            page.Children.Add(Label("Reconocimiento local"));
            var options=new System.Collections.Generic.List<string>{"windows"};
            var names=new System.Collections.Generic.List<string>{"Windows Speech"};
            if(whisperStt.Available){options.Add("whisper");names.Add(whisperStt.Name);}
            if(whisperBase.Available){options.Add("whisper-base");names.Add(whisperBase.Name);}
            var engines=new ComboBox { ItemsSource=names,SelectedIndex=Math.Max(0,options.IndexOf(settings.Intelligence.SttProvider)) };
            engines.SelectionChanged+=(s,e)=>{if(engines.SelectedIndex>=0){settings.Intelligence.SttProvider=options[engines.SelectedIndex];Save();}};
            page.Children.Add(engines);
            page.Children.Add(Label(whisperStt.Available?"Whisper no informa confianza calibrada. Las acciones requieren confirmación con Windows Speech.":"Whisper Tiny no está disponible."));
            Range(page,"Sensibilidad de detección",.65,1.45,settings.Intelligence.SttSensitivity,n=>settings.Intelligence.SttSensitivity=n,Percent);
            Range(page,"Duración máxima de captura",8,20,settings.Intelligence.SttMax,n=>settings.Intelligence.SttMax=n,n=>n.ToString("0")+" s");
            Toggle(page,"Filtro de graves y ganancia moderada",settings.Intelligence.SttEnhance,n=>settings.Intelligence.SttEnhance=n);
            Heading(page,"Diagnóstico de reconocimiento");
            page.Children.Add(Label("La prueba no ejecuta herramientas ni consulta DeepSeek. La comparación reutiliza el mismo audio en RAM."));
            speechReference=Field(page,"Frase esperada para medir errores de palabra (opcional)");
            speechReference.MaxLength=500;
            speechDisplay=Label("");page.Children.Add(speechDisplay);
            ActionButton(page,"Capturar una frase de prueba",()=>BeginSpeechDemo(false));
            if(whisperStt.Available)ActionButton(page,"Capturar y comparar Windows / Whisper",()=>BeginSpeechDemo(true));
            if(whisperStt.Available && whisperBase.Available)
                ActionButton(page,"Capturar y comparar Whisper Tiny / Base",()=>BeginSpeechDemo(true,true));
            ActionButton(page,"Cancelar prueba",StopListening);
            ActionButton(page,"Borrar transcripción visible",()=>{lastSpeech=null;alternateSpeech=null;speechIntent="";speechParameters="";});
            panel.Closed+=(s,e)=>{speechDisplay=null;speechReference=null;lastSpeech=null;alternateSpeech=null;speechIntent="";speechParameters="";};
        }
        void BeginSpeechDemo(bool compare,bool models=false)
        {
            if(floatingActive || capture!=null || transcription!=null || busyConversation || speaking || speechPending)return;
            sttDemo=true;compareStt=compare;compareWhisperModels=models;alternateSpeech=null;lastSpeech=null;speechIntent="";speechParameters="";
            lastSpeechOutcome="Escuchando para diagnóstico";
            if(!StartListening(true))sttDemo=false;
        }
        void RefreshSpeechDiagnostic(double now)
        {
            if(speechDisplay==null || panel==null || tabs.SelectedIndex!=3 || now<nextSpeechDisplay)return;
            nextSpeechDisplay=now+.18;
            var levels=floatingDetector??lastLevels;
            string signal=levels==null?"Sin medición":String.Format("{0:0.0} dBFS RMS · pico {1:P0} · saturación máxima por bloque {2:P1} · {3}",
                20*Math.Log10(Math.Max(.00001,levels.Rms)),levels.Peak,levels.Clipped,levels.HeardVoice?"actividad vocal detectada":"esperando voz");
            speechDisplay.Text=speechDevice+" · "+CurrentStt.Name+"\n"+signal+"\n"+lastSpeechOutcome+
                FormatSpeech(lastSpeech)+(alternateSpeech==null?"":"\nComparación"+FormatSpeech(alternateSpeech))+
                (String.IsNullOrEmpty(speechIntent)?"":"\nIntención · "+speechIntent+"\nParámetros · "+speechParameters);
        }
        string FormatSpeech(SpeechResult result)
        {
            if(result==null)return "";
            string corrected=TechnicalSpeech.Normalize(result.Text,desktopConfig.Words);
            return String.Format("\n{0} · {1} · audio {2:0.0} s · decodificación {3:0.00} s\nConfianza: {4} · rechazos {5}\n{6}\n{7}",
                result.Provider,result.Model,result.CaptureSeconds,result.DecodeSeconds,result.Confidence.HasValue?
                result.Confidence.Value.ToString("P0"):"sin dato calibrado",result.Rejected,result.Text??"Sin transcripción",result.Rejection??"")+
                (corrected==result.Text?"":"\nInterpretación local: "+corrected)+
                (speechReference==null || String.IsNullOrWhiteSpace(speechReference.Text)?"":"\nErrores de palabra (WER): "+SpeechBenchmark.ErrorRate(speechReference.Text,result.Text));
        }
    }
}

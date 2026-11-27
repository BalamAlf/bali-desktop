using System;
using System.Text.RegularExpressions;

namespace BaliDesktop
{
    sealed class VoiceSilenceDetector
    {
        double noise=.0018, candidate, afterVoice, elapsed;
        public double Sensitivity=1, Rms, Peak, Clipped, VoiceRms;
        public double? VoiceStart {get;private set;}
        public bool HeardVoice { get; private set; }
        public void Observe(byte[] pcm,int count)
        {
            if(count<2) return;
            double seconds=count/32000.0;
            double power=0, peak=0;
            int clipping=0;
            for(int i=0;i<count-1;i+=2)
            {
                double sample=unchecked((short)(pcm[i]|pcm[i+1]<<8))/32768.0;
                power+=sample*sample;
                peak=Math.Max(peak,Math.Abs(sample));
                if(Math.Abs(sample)>=.995)clipping++;
            }
            double rms=Math.Sqrt(power/(count/2));
            Rms=rms;Peak=Math.Max(Peak,peak);Clipped=Math.Max(Clipped,clipping/(double)(count/2));
            bool voiced=rms>=Math.Max(.0032,noise*2.6)/Sensitivity && peak>=.008/Sensitivity;
            if(voiced)VoiceRms=Math.Max(VoiceRms,rms);
            elapsed+=seconds;
            if(voiced)
            {
                candidate+=seconds;
                if(candidate>=.15 && !HeardVoice) {HeardVoice=true;VoiceStart=elapsed-candidate;}
            }
            else
            {
                candidate=0;
                noise=Math.Max(.0008,Math.Min(.009,noise*.94+rms*.06));
            }
            if(HeardVoice) afterVoice=voiced?0:afterVoice+seconds;
        }
        public bool Ready(double silence) { return HeardVoice && afterVoice>=silence; }
        public bool NoSpeech(double seconds) { return !HeardVoice && elapsed>=seconds; }
        public static bool IsQuestion(string text)
        {
            return !String.IsNullOrWhiteSpace(text) && Regex.Matches(text,@"[\p{L}\p{N}]+").Count>=2;
        }
    }

    sealed partial class BaliApp
    {
        VoiceSilenceDetector floatingDetector;
        bool floatingActive, followUp, manualVoiceEnd;
        double floatingRearm;
        readonly SpokenQuestion spokenQuestion=new SpokenQuestion();
        readonly VoiceSession voiceSession=new VoiceSession();
        string lastOnlineVoiceQuestion;
        double lastOnlineVoiceTime;

        void ToggleFloatingVoice()
        {
            if(manualVoiceEnd && capture!=null) { FinishListening(); return; }
            if(floatingActive || capture!=null || transcription!=null || busyConversation || desktopWork!=null || visualWork!=null || computerWork!=null || speaking || speechPending)
            {
                CancelConversation();
                SetDialogueStatus(null,"Turno cancelado.");
                return;
            }
            floatingActive=true;
            followUp=false;
            store.Log("Voz flotante: turno solicitado.");
            if(!StartListening(true)) EndFloating();
        }
        void ToggleManualVoice()
        {
            if(manualVoiceEnd && capture!=null) { FinishListening(); return; }
            if(floatingActive || capture!=null || transcription!=null || busyConversation || speaking || speechPending) return;
            floatingActive=true;
            manualVoiceEnd=true;
            if(!StartListening(true))EndFloating();
        }
        void EndFloating()
        {
            floatingActive=followUp=false;
            manualVoiceEnd=false;
            floatingRearm=0;
            floatingDetector=null;
            spokenQuestion.Clear();
            ClearDesktopPending("voz");
            if(dictationActive || computerPending!=null)StopComputer(false);
        }
        void FloatingTick(double now)
        {
            if(floatingRearm>0 && now>=floatingRearm)
            {
                floatingRearm=0;
                if(floatingActive && ContinueVoice() && !speaking && !speechPending &&
                    capture==null && transcription==null && !busyConversation)
                {
                    followUp=true;
                    if(!StartListening(true)) EndFloating();
                }
                else EndFloating();
            }
            if(floatingDetector==null || capture==null) return;
            if(DictationFocusLost)
            {
                StopComputer(false);EndFloating();StopListening();
                ReportFloatingFailure("El foco cambió o terminó el turno de dictado. El borrador quedó en Control de escritorio.");
                return;
            }
            if(spokenQuestion.HasSession && !spokenQuestion.Active(now))
            { StopFloatingCapture("Terminó la espera de confirmación."); return; }
            try
            {
                capture.Pump(floatingDetector.Observe);
                double pause=settings.Intelligence.VoiceSilence;
                if(!manualVoiceEnd && floatingDetector.Ready(pause)) FinishListening();
                else if(!manualVoiceEnd && floatingDetector.NoSpeech(followUp?settings.Intelligence.VoiceIdle:9))
                    StopFloatingCapture("No se detectó voz.");
            }
            catch(InvalidOperationException) { StopFloatingCapture("Windows interrumpió la captura."); }
        }
        void StopFloatingCapture(string message)
        {
            lastLevels=floatingDetector;
            if(sttDemo)
            {
                sttDemo=false; lastSpeechOutcome=message;
                EndFloating(); StopListening(); return;
            }
            bool explain=!followUp;
            EndFloating();
            if(!explain) toolsRouter.Cancel("voz");
            StopListening();
            if(explain) ReportFloatingFailure(message);
            else store.Log("Voz flotante: seguimiento finalizado sin voz.");
        }
        void ReportFloatingFailure(string message)
        {
            EndFloating();
            lastSpeechOutcome=message;
            store.Log("Voz flotante: "+message);
            if(capture==null && transcription==null) Play(message);
            SetDialogueStatus(null,message);
        }
        void CompleteFloatingSpeech(bool spoken)
        {
            if(!floatingActive) return;
            if(spoken && ContinueVoice())
                floatingRearm=clock.Elapsed.TotalSeconds+.65;
            else EndFloating();
        }
        bool ContinueVoice()
        {return settings.Intelligence.ContinuousVoice || ComputerContinuation || VisualAwaitsReply() || DesktopAwaitsAnswer() || spokenQuestion.Active(clock.Elapsed.TotalSeconds) ||
            toolsRouter.AwaitsTime("voz",DateTime.Now) || toolsRouter.AwaitsApproval("voz",DateTime.Now);}
        void RouteFloating(string recognized)
        {
            if(!floatingActive) return;
            if(TryComputer(recognized,true))return;
            if(TryVisual(recognized,true))return;
            bool technicalClarification=TechnicalSpeech.IsAmbiguousVpn(recognized);
            recognized=TechnicalSpeech.Normalize(recognized,desktopConfig.Words);
            double now=clock.Elapsed.TotalSeconds;
            if(spokenQuestion.HasSession)
            {
                if(!spokenQuestion.Active(now)) { ReportFloatingFailure("Terminó la espera. Vuelve a preguntar cuando quieras."); return; }
                var decision=SpokenQuestion.Answer(recognized);
                if(decision==SpokenReply.Cancel)
                { ReportFloatingFailure("Consulta cancelada."); return; }
                if(decision==SpokenReply.Yes)
                {
                    if(spokenQuestion.AwaitingAnswer(now) && (lastSpeech==null || lastSpeech.Rejected==0))
                    {
                        var question=spokenQuestion.Text;
                        spokenQuestion.Clear();
                        SendFloatingQuestion(question);
                    }
                    else SpeakFloatingNotice("No entendí una respuesta clara. Puedes repetir o cancelar.");
                    return;
                }
                if(decision==SpokenReply.No)
                {
                    if(spokenQuestion.AwaitingAnswer(now))spokenQuestion.Repeat(now);
                    SpeakFloatingNotice("De acuerdo. Dime la pregunta de nuevo."); return;
                }
                var corrected=SpokenQuestion.Correction(recognized);
                technicalClarification|=TechnicalSpeech.IsAmbiguousVpn(corrected);
                if(corrected!=recognized)recognized=TechnicalSpeech.Normalize(corrected,desktopConfig.Words);
                if(!SpokenQuestion.IsInformational(recognized) && !VoiceSession.IsFollowUp(recognized))
                { SpeakFloatingNotice("Puedes decir sí, no, cancelar o repetir la pregunta."); return; }
                spokenQuestion.Clear();
            }
            if(TryDesktop(recognized,true,true) || TryPersonal(recognized,true)) return;
            if(SpokenQuestion.Normalize(recognized)=="termina la conversación" ||
                SpokenQuestion.Normalize(recognized)=="cancela conversación")
            {voiceSession.Clear();ReportFloatingFailure("Conversación terminada.");return;}
            if(toolsRouter.AwaitsApproval("voz",DateTime.Now))
            { SpeakFloatingNotice("Para la acción pendiente, di confirma o cancela."); return; }
            var reply=conversation.Respond(recognized,settings.Memory);
            if(reply.Intent!=Intent.Unknown && (!sttNeedsReview || reply.Intent==Intent.Status || reply.Intent==Intent.Hello))
            {
                life.VoiceRecognized(DateTime.UtcNow);
                Respond(recognized,true,reply);
                return;
            }
            if(Regex.IsMatch(recognized,@"\b(?:ag[eé]ndame|programa|registre|registra|ap[uú]ntame|an[oó]tame)\b",RegexOptions.IgnoreCase))
            {
                StageToolPrompt(recognized);
                SpeakFloatingNotice("Revisa la transcripción en Herramientas. Desde allí puedes pedir una propuesta.");
                return;
            }
            bool followUpQuestion=VoiceSession.IsFollowUp(recognized);
            if(reply.Intent!=Intent.Unknown || !SpokenQuestion.IsInformational(recognized) && !followUpQuestion)
            { SpeakFloatingNotice("No pude entender la pregunta. Puedes repetirla."); return; }
            if(!VoiceOnlineAllowed())return;
            if(technicalClarification || sttNeedsReview && !(lastSpeech!=null && !lastSpeech.Confidence.HasValue && lastSpeech.Rejected==0 && SpokenQuestion.ClearWhisper(recognized)))
            {
                if(!settings.Intelligence.ConfirmVoiceOnline)
                { SpeakFloatingNotice("No entendí con certeza. Repite la pregunta completa."); return; }
                if(MemoryBook.LooksSensitive(recognized))
                { SpeakFloatingNotice("Retira los posibles datos sensibles antes de enviar."); return; }
                spokenQuestion.Ask(recognized,now);
                string spoken=Regex.Replace(SpokenQuestion.ForSpeech(recognized).Trim('¿','?','.'),@"\bVPN\b","uve pe ene",RegexOptions.IgnoreCase);
                SpeakFloatingNotice("¿Me preguntaste: "+spoken+"? Di sí, no o cancela.");
                return;
            }
            SendFloatingQuestion(recognized);
        }
        bool VoiceOnlineAllowed()
        {
            if(!settings.Intelligence.DirectVoiceOnline)
            { SpeakFloatingNotice("Las consultas a DeepSeek por voz están desactivadas."); return false; }
            if(!settings.Intelligence.Enabled || !settings.Intelligence.Verified || !vault.Exists)
            { SpeakFloatingNotice("DeepSeek no está disponible. Puedo responder consultas locales."); return false; }
            return true;
        }
        void SendFloatingQuestion(string recognized)
        {
            if(!VoiceOnlineAllowed() || busyConversation)return;
            if(MemoryBook.LooksSensitive(recognized))
            { SpeakFloatingNotice("Retira los posibles datos sensibles antes de enviar."); return; }
            double now=clock.Elapsed.TotalSeconds;
            bool followUpQuestion=VoiceSession.IsFollowUp(recognized);
            var speechConfig=settings.Intelligence;
            if(followUpQuestion && (!speechConfig.VoiceTopicContext || voiceSession.Topic(now,speechConfig.VoiceTopicSeconds)==null))
            {SpeakFloatingNotice("¿Sobre qué tema? Puedes decir la pregunta completa.");return;}
            var normalized=SpokenQuestion.Normalize(followUpQuestion?
                voiceSession.Resolve(recognized,now,speechConfig.VoiceTopicSeconds):recognized);
            if(lastOnlineVoiceQuestion==normalized && now-lastOnlineVoiceTime<8)
            { SpeakFloatingNotice("Acabo de consultar esa pregunta."); return; }
            lastOnlineVoiceQuestion=normalized;
            lastOnlineVoiceTime=now;
            if(settings.Intelligence.AutoOpenVoiceChat)OpenPanel();
            RunOnline(recognized,false,null,true);
        }
        void SpeakFloatingNotice(string text)
        {
            if(floatingActive) Play(text);
            SetDialogueStatus(null,text);
        }
    }
}

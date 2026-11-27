using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        readonly List<ChatLine> session=new List<ChatLine>();
        CredentialVault vault;
        MemoryBook memoryBook;
        UsageLedger usageLedger;
        DeepSeekProvider deepSeek;
        ConversationService chatService;
        CancellationTokenSource turn, transcription;
        MicrophoneCapture capture;
        double captureUntil, nextDialogueCue, nextChunk;
        readonly StringBuilder streamBuffer=new StringBuilder();
        TextBlock liveReply, providerStatus;
        StackPanel transcriptPanel;
        Button microphoneButton;
        ScrollViewer transcriptScroll;
        TextBox entry;
        string connection="Sin consulta online";
        string contextLabels="Ninguna categoría enviada";
        bool speaking, busyConversation, receivingText;
        string lastSpoken;

        void InitIntelligence()
        {
            vault=new CredentialVault(store.Folder);
            memoryBook=new MemoryBook(store.Folder);
            usageLedger=new UsageLedger(store.Folder);
        }
        void ConversationPage(Panel page)
        {
            var title=new DockPanel();
            page.Children.Add(title);
            var avatar=new Face(expressions,settings.Appearance) { Width=78,Height=62 };
            DockPanel.SetDock(avatar,Dock.Right);
            title.Children.Add(avatar);
            conversationFace=avatar;
            var heading=new StackPanel();
            title.Children.Add(heading);
            Heading(heading,"Conversar con Bali");
            providerStatus=Label("");
            heading.Children.Add(providerStatus);
            response=Label("Escribe un comando o usa el micrófono.");
            page.Children.Add(response);
            transcriptPanel=new StackPanel { Margin=new Thickness(10,6,10,6) };
            transcriptScroll=new ScrollViewer { Content=transcriptPanel,Height=210,Background=canvas,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
            page.Children.Add(transcriptScroll);
            RefreshTranscript();
            entry=new TextBox { FontSize=16,Padding=new Thickness(8),MaxLength=3000,Margin=new Thickness(0,9,0,5) };
            System.Windows.Automation.AutomationProperties.SetName(entry,"Mensaje o transcripción");
            page.Children.Add(entry);
            entry.KeyDown+=(s,e)=>{ if(e.Key==Key.Enter){ e.Handled=true; SendLocal(); } };
            var actions=new WrapPanel();
            page.Children.Add(actions);
            ActionButton(actions,"Enviar local",SendLocal);
            ActionButton(actions,"Consultar a DeepSeek",SendOnline);
            var mic=ActionButton(actions,settings.Intelligence.ToggleCapture?"Iniciar / detener micrófono":"Mantén para hablar",()=>
            {
                if(settings.Intelligence.ToggleCapture)
                {
                    if(capture==null) StartListening(); else FinishListening();
                }
                else if(capture==null) StartListening();
                else FinishListening();
            });
            microphoneButton=mic;
            mic.PreviewMouseLeftButtonDown+=(s,e)=>
            {
                if(settings.Intelligence.ToggleCapture) return;
                e.Handled=true;
                if(StartListening()) mic.CaptureMouse();
            };
            mic.PreviewMouseLeftButtonUp+=(s,e)=>
            {
                if(settings.Intelligence.ToggleCapture || !mic.IsMouseCaptured) return;
                e.Handled=true; FinishListening(); mic.ReleaseMouseCapture();
            };
            mic.LostMouseCapture+=(s,e)=>{ if(!settings.Intelligence.ToggleCapture && capture!=null) StopListening(); };
            mic.LostKeyboardFocus+=(s,e)=>{ if(!settings.Intelligence.ToggleCapture && capture!=null && !mic.IsMouseCaptured) StopListening(); };
            mic.PreviewKeyDown+=(s,e)=>
            {
                if(!settings.Intelligence.ToggleCapture && e.Key==Key.Space && !e.IsRepeat) { e.Handled=true; StartListening(); }
            };
            mic.PreviewKeyUp+=(s,e)=>
            {
                if(!settings.Intelligence.ToggleCapture && e.Key==Key.Space) { e.Handled=true; FinishListening(); }
            };
            ActionButton(actions,"Cancelar",CancelConversation);
            ActionButton(actions,"Leer respuesta",()=>{ if(!String.IsNullOrEmpty(lastSpoken)) Play(lastSpoken); });
            ActionButton(actions,"Detener audio",StopPlayback);
            ActionButton(actions,"Nueva conversación",()=>
            {
                CancelConversation();
                session.Clear(); lastSpoken=null;
                RefreshTranscript();
                response.Text="Conversación nueva. Los recuerdos aprobados siguen disponibles.";
            });
            ActionButton(actions,"Gestionar recuerdos",()=>MemoryDialog(ActiveInput()));
            var oldCommands=new StackPanel();
            var disclose=new Expander { Header="Comandos y preguntas locales",Foreground=ink,Content=oldCommands,Margin=new Thickness(0,9,0,3) };
            page.Children.Add(disclose);
            var commands=new WrapPanel();
            oldCommands.Children.Add(commands);
            foreach(string command in LocalConversationProvider.Commands.Concat(LocalConversationProvider.ContextCommands).Concat(LocalConversationProvider.ControlCommands))
            {
                string value=command;
                ActionButton(commands,value,()=>{ if(!busyConversation) Respond(value); });
            }
            RefreshProvider();
        }
        Face conversationFace;

        string ActiveInput()
        {
            string text=entry==null?"":entry.Text.Trim();
            return text.Length==0?(session.LastOrDefault(x=>x.Role=="user")??new ChatLine("user","")).Text:text;
        }
        void AddLine(string role,string value,bool localOnly=false)
        {
            session.Add(new ChatLine(role,value,localOnly));
            if(session.Count>24) session.RemoveAt(0);
        }
        void RefreshTranscript()
        {
            if(transcriptPanel==null) return;
            transcriptPanel.Children.Clear();
            if(session.Count==0) transcriptPanel.Children.Add(Label("Sin mensajes en esta sesión."));
            foreach(var message in session)
            {
                var line=Label((message.Role=="user"?"Tú · ":"Bali · ")+message.Text);
                line.Margin=new Thickness(0,2,0,7);
                transcriptPanel.Children.Add(line);
            }
            if(liveReply!=null) transcriptPanel.Children.Add(liveReply);
            transcriptScroll.ScrollToEnd();
        }
        void RefreshProvider()
        {
            if(providerStatus==null) return;
            var config=settings.Intelligence;
            providerStatus.Text=(config.Enabled?(config.Verified?"Local y DeepSeek a petición":"Local · DeepSeek pendiente de prueba"):"Proveedor local")+" · "+connection;
        }
        void SendLocal()
        {
            if(floatingActive || busyConversation || capture!=null || transcription!=null) return;
            string text=entry.Text.Trim();
            if(text.Length==0) return;
            if(TryComputer(text,false) || TryVisual(text,false) || TryDesktop(text,false)) { entry.Clear(); return; }
            if(TryPersonal(text,false)) { entry.Clear(); return; }
            if(conversation.Respond(text,settings.Memory).Intent==Intent.Unknown)
            {
                response.Text=settings.Intelligence.Enabled?"Puedes enviarlo con «Consultar a DeepSeek».":"Bali puede responder comandos locales. Configura DeepSeek para preguntas abiertas.";
                return;
            }
            entry.Clear();
            Respond(text);
        }
        void SendOnline()
        {
            if(floatingActive || busyConversation || capture!=null || transcription!=null) return;
            string text=entry.Text.Trim();
            if(text.Length==0) return;
            if(TryComputer(text,false) || TryVisual(text,false) || TryDesktop(text,false)) { entry.Clear(); return; }
            if(TryPersonal(text,false)) { entry.Clear(); return; }
            if(conversation.Respond(text,settings.Memory).Intent!=Intent.Unknown) { entry.Clear(); Respond(text); return; }
            if(!settings.Intelligence.Enabled || !settings.Intelligence.Verified || !vault.Exists)
            { response.Text="Configura y autoriza la prueba controlada en Inteligencia."; return; }
            entry.Clear();
            RunOnline(text,false,null);
        }
        async void RunOnline(string text,bool diagnostic,TextBlock result,bool floating=false,Action<string> proposal=null,ChatContext visual=null)
        {
            if(busyConversation || capture!=null || transcription!=null) return;
            var snapshot=settings.Intelligence.Snapshot();
            ChatContext context;
            string key;
            try
            {
                if(!snapshot.Enabled || !vault.Exists) throw new ConversationError("Habilita DeepSeek y guarda una API Key.");
                if(diagnostic) { snapshot.Output=24; snapshot.ShareHistory=snapshot.ShareState=snapshot.ShareHabits=snapshot.ShareMemories=false; }
                string query=floating && snapshot.VoiceTopicContext?voiceSession.Resolve(text,clock.Elapsed.TotalSeconds,snapshot.VoiceTopicSeconds):text;
                context=visual??(proposal==null?ContextBuilder.Build(query,snapshot,settings.Memory,session,memoryBook,floating):ToolProposal.Context(text,snapshot));
                key=vault.Read();
            }
            catch(ConversationError ex) { if(floating) SpeakFloatingNotice(ex.Message); else SetDialogueStatus(result,ex.Message); return; }
            catch(InvalidOperationException ex) { if(floating) SpeakFloatingNotice(ex.Message); else SetDialogueStatus(result,ex.Message); return; }
            catch(Exception)
            {
                if(floating) SpeakFloatingNotice("No pude abrir la credencial protegida.");
                else SetDialogueStatus(result,"No se pudo abrir la credencial protegida.");
                return;
            }
            if(chatService==null) { deepSeek=new DeepSeekProvider(); chatService=new ConversationService(usageLedger,deepSeek); }
            var owned=new CancellationTokenSource();
            turn=owned; busyConversation=true; receivingText=false;
            visualTurnRunning=visual!=null;
            var originalPanel=panel;
            StopPlayback();
            var collected=new StringBuilder();
            if(!diagnostic && proposal==null)
            {
                AddLine("user",text,visual!=null);
                if(panel!=null) liveReply=Label("Bali · ");
                RefreshTranscript();
            }
            lock(streamBuffer) streamBuffer.Clear();
            SetDialogueStatus(result,"Procesando…");
            life.InterruptInitiative(DateTime.UtcNow);
            life.Cue(Expression.Procesamiento,6,9);
            bool success=false;
            try
            {
                await Task.Run(()=>chatService.Query(key,snapshot,context,diagnostic,chunk=>
                {
                    lock(streamBuffer) { collected.Append(chunk); if(!diagnostic && proposal==null) streamBuffer.Append(chunk); }
                },owned.Token));
                success=true;
                connection="DeepSeek respondió";
                contextLabels=String.Join(", ",context.Categories);
                string answer=collected.ToString().Trim();
                if(diagnostic)
                {
                    if(owned.IsCancellationRequested || panel!=originalPanel || !settings.Intelligence.Enabled) return;
                    if(answer.Length==0) SetDialogueStatus(result,"La conexión respondió sin texto. Revisa Consumo.");
                    else
                    {
                        settings.Intelligence.Verified=true;
                        Save();
                        SetDialogueStatus(result,"Conexión confirmada. Revisa Consumo.");
                    }
                }
                else if((floating?floatingActive:panel==originalPanel) && !owned.IsCancellationRequested)
                {
                    if(proposal!=null) { proposal(answer); return; }
                    if(answer.Length==0)
                    {
                        if(floating) SpeakFloatingNotice("DeepSeek respondió sin texto.");
                        else SetDialogueStatus(null,"DeepSeek respondió sin texto.");
                    }
                    else
                    {
                        AddLine("assistant",answer,visual!=null);
                        if(visual!=null)
                        {
                            visualOnlineResult=answer;
                            if(refreshVision!=null)refreshVision();
                        }
                        lastSpoken=answer;
                        SetDialogueStatus(null,"Respuesta de DeepSeek.");
                        if(floating)
                        {
                            if(visual==null && snapshot.VoiceTopicContext && settings.Intelligence.VoiceTopicContext)
                                voiceSession.Remember(text,clock.Elapsed.TotalSeconds,VoiceSession.IsFollowUp(text));
                            var plain=SpokenQuestion.ForSpeech(answer);
                            Play(String.IsNullOrWhiteSpace(plain)?"La respuesta está disponible en Conversar.":plain);
                        }
                        else if(settings.Voice)Play(answer);
                    }
                    try { memoryBook.Used(context.Used); } catch(Exception) { }
                }
            }
            catch(ConversationError ex)
            {
                connection=owned.IsCancellationRequested?"Consulta cancelada":"Última consulta no disponible";
                if(panel==originalPanel) SetDialogueStatus(result,ex.Message);
                if(visual!=null && !owned.IsCancellationRequested)
                {
                    visualOnlineResult=ex.Message;
                    if(refreshVision!=null)refreshVision();
                }
                if(floating && !owned.IsCancellationRequested && floatingActive)
                    SpeakFloatingNotice(ex.Message);
            }
            finally
            {
                if(!success && !diagnostic && proposal==null && panel==originalPanel && !owned.IsCancellationRequested && collected.Length>0)
                    AddLine("assistant",collected.ToString()+" [respuesta incompleta]",visual!=null);
                turn=null; busyConversation=false; receivingText=false; owned.Dispose();
                visualTurnRunning=false;
                liveReply=null;
                lock(streamBuffer) streamBuffer.Clear();
                RefreshTranscript(); RefreshProvider(); RefreshIntelligence();
                if(!speaking)
                {
                    life.ReleaseVisualCue();
                    if(!success) life.Cue(Expression.Confusión,2,5);
                    else life.Cue(Expression.Escucha,1.1,5);
                }
            }
        }
        void SetDialogueStatus(TextBlock specific,string message)
        {
            var target=specific??response;
            if(target!=null) target.Text=message;
        }
        void CancelConversation()
        {
            if(toolsRouter!=null) toolsRouter.Cancel();
            ClearVision();
            StopComputer(false);
            voiceSession.Clear();
            ClearDesktopPending();
            if(desktopWork!=null)desktopWork.Cancel();
            EndFloating();
            if(turn!=null) turn.Cancel();
            StopListening();
            StopPlayback();
        }
        bool StartListening(bool floating=false)
        {
            if((floatingActive && !floating) || busyConversation || desktopWork!=null || visualWork!=null || computerWork!=null || capture!=null || transcription!=null) return false;
            if((!floating && !settings.Microphone) || (!CurrentStt.Available && !(sttDemo && compareStt && whisperStt.Available)))
            {
                if(sttDemo) lastSpeechOutcome="El reconocedor local no está disponible.";
                else if(floating) ReportFloatingFailure("El reconocedor local no está disponible.");
                else SetDialogueStatus(null,"Habilita el micrófono en Voz. El texto sigue disponible.");
                return false;
            }
            StopPlayback();
            try
            {
                var device=MicrophoneCapture.Devices().FirstOrDefault(x=>x.Name==settings.Intelligence.Microphone);
                speechDevice=device==null?"Predeterminado de Windows":device.Name;
                capture=new MicrophoneCapture(device==null?UInt32.MaxValue:device.Id,floating);
                if(floating) floatingDetector=new VoiceSilenceDetector{Sensitivity=settings.Intelligence.SttSensitivity};
                captureUntil=clock.Elapsed.TotalSeconds+(floating?settings.Intelligence.SttMax:MicrophoneCapture.Seconds);
                life.InterruptInitiative(DateTime.UtcNow);
                life.Cue(Expression.Escucha,6,9);
                SetDialogueStatus(null,"Micrófono activo.");
                return true;
            }
            catch(Exception ex)
            {
                lastSpeechOutcome=ex is InvalidOperationException?ex.Message:"No se pudo iniciar el micrófono.";
                if(floating && !sttDemo) ReportFloatingFailure(lastSpeechOutcome);
                else SetDialogueStatus(null,lastSpeechOutcome);
                return false;
            }
        }
        async void FinishListening()
        {
            if(capture==null) return;
            bool floating=floatingDetector!=null;
            bool manual=manualVoiceEnd;
            manualVoiceEnd=false;
            if(manual && floating)
            {
                try { capture.Pump(floatingDetector.Observe); }
                catch(InvalidOperationException) { StopFloatingCapture("Windows interrumpió la captura."); return; }
            }
            if(floating && !manual && !floatingDetector.HeardVoice) { StopFloatingCapture("No se detectó voz."); return; }
            bool demo=sttDemo, compare=demo && compareStt;
            lastLevels=floatingDetector;
            floatingDetector=null;
            byte[] pcm;
            try { using(var current=capture) pcm=current.Finish(); }
            catch(Exception) { capture=null; sttDemo=false; EndFloating(); life.ReleaseVisualCue(); lastSpeechOutcome="No se pudo leer el audio local."; SetDialogueStatus(null,lastSpeechOutcome); return; }
            capture=null;
            if(floating) store.Log("Voz flotante: voz detectada; micrófono liberado para transcribir.");
            var owned=new CancellationTokenSource(TimeSpan.FromSeconds(compare?32:16));
            transcription=owned;
            var originalPanel=panel;
            life.Cue(Expression.Procesamiento,6,9);
            byte[] prepared=pcm;
            lastSpeechOutcome="Transcribiendo localmente…";
            speechIntent=speechParameters="";
            SetDialogueStatus(null,lastSpeechOutcome);
            try
            {
                if(floating)
                {
                    bool enhance=settings.Intelligence.SttEnhance || lastLevels.VoiceRms>0 && lastLevels.VoiceRms<.025 && lastLevels.Clipped==0;
                    prepared=SpeechAudio.Prepare(pcm,manual?null:lastLevels.VoiceStart,enhance,lastLevels.VoiceRms);
                    store.Log(String.Format("Voz flotante: audio {0:0.00}s · nivel de voz {1:0.0} dBFS · pico {2:P0} · saturación {3:P1} · realce {4}.",
                        pcm.Length/32000.0,20*Math.Log10(Math.Max(.00001,lastLevels.VoiceRms)),lastLevels.Peak,lastLevels.Clipped,enhance));
                }
                var primary=CurrentStt;
                bool desktopApproval=floating && !demo && (DesktopRequiresReliableReply() || VisualAwaitsReply() || ComputerAwaiting);
                SpeechResult result;
                if(desktopApproval)result=await windowsStt.Recognize(prepared,true,owned.Token,true);
                else if(floating && dictationActive)
                {
                    var whisper=primary as WhisperRecognizer;
                    result=whisper!=null?await whisper.Dictate(prepared,owned.Token):await windowsStt.Dictate(prepared,owned.Token);
                }
                else result=compare?await RunSpeechDiagnostic(compareWhisperModels?(ISpeechRecognizer)whisperStt:windowsStt,prepared,owned.Token):await RecognizeLocal(prepared,floating,owned.Token);
                var other=compare?await RunSpeechDiagnostic(compareWhisperModels?whisperBase:PreferredWhisper,prepared,owned.Token):null;
                var approvalName=desktopApproval?desktopPendingApp:null;
                var approvalAnswer=SpokenQuestion.DesktopAnswer(result.Text);
                if(approvalName!=null && PreferredWhisper.Available && approvalAnswer!=SpokenReply.No && approvalAnswer!=SpokenReply.Cancel &&
                    (result.NeedsReview || result.Confidence.Value<.72 ||
                    approvalAnswer!=SpokenReply.Yes && !SpokenQuestion.NamesAppToAuthorize(result.Text,approvalName)))
                {
                    other=await RunSpeechDiagnostic(PreferredWhisper,prepared,owned.Token);
                    var confirmation=SpokenQuestion.OpeningFallback(result,other,approvalName,lastLevels.HeardVoice);
                    if(confirmation!=SpokenReply.Other)
                    {
                        other.ConfirmedDesktopOpening=confirmation==SpokenReply.Yes;
                        var windows=result;result=other;other=windows;
                    }
                }
                bool awaitingApproval=floating && !demo && toolsRouter.AwaitsApproval("voz",DateTime.Now);
                if(floating && !demo && !awaitingApproval && !desktopApproval && !dictationActive && !result.HasText)
                {
                    ISpeechRecognizer backup=primary==windowsStt?(ISpeechRecognizer)PreferredWhisper:windowsStt;
                    if(backup.Available && !result.Provider.StartsWith(backup.Name,StringComparison.Ordinal))
                        other=await RunSpeechDiagnostic(backup,prepared,owned.Token);
                    if(other!=null && other.HasText)
                    {
                        var rejected=result;
                        result=other;
                        other=rejected;
                        result.Rejection="Recuperación local después de un rechazo de "+rejected.Provider+".";
                    }
                }
                if(floating && !demo && !desktopApproval && !awaitingApproval && !dictationActive && result.HasText && result.NeedsReview &&
                    !result.Provider.StartsWith(windowsStt.Name,StringComparison.Ordinal) && windowsStt.Available &&
                    IsDesktopOpenCommand(result.Text) && ExactAuthorizedApp(result.Text)==null)
                {
                    var retry=await RunSpeechDiagnostic(windowsStt,prepared,owned.Token);
                    var matched=ExactAuthorizedApp(retry.Text);
                    if(matched!=null && !retry.NeedsReview && retry.Confidence.GetValueOrDefault()>=.75)
                    {other=result;result=retry;}
                }
                if(floating && !demo && !desktopApproval && !awaitingApproval && !dictationActive && visualSettings.Enabled &&
                    result.HasText && result.NeedsReview && !result.Provider.StartsWith(windowsStt.Name,StringComparison.Ordinal) &&
                    windowsStt.Available && VisualSpeech.Parse(result.Text)==VisualIntent.None && VisualSpeech.MayRetry(result.Text))
                {
                    var retry=await RunSpeechDiagnostic(windowsStt,prepared,owned.Token);
                    if(!retry.NeedsReview && retry.Confidence.GetValueOrDefault()>=.72 && VisualSpeech.Parse(retry.Text)!=VisualIntent.None)
                    {other=result;result=retry;}
                }
                owned.Token.ThrowIfCancellationRequested();
                if((demo || !floating)?panel!=originalPanel:!floatingActive) return;
                result.CaptureSeconds=pcm.Length/32000.0;
                if(other!=null)other.CaptureSeconds=result.CaptureSeconds;
                lastSpeech=result; alternateSpeech=other;
                transcription=null;
                life.ReleaseVisualCue();
                if(demo) { lastSpeechOutcome="Prueba local terminada."; return; }
                string recognized=result.Text;
                sttNeedsReview=result.NeedsReview;
                if(floating)
                    store.Log("Voz flotante: "+result.Provider+" · texto "+(result.HasText?"sí":"no")+" · rechazos "+result.Rejected+" · revisión "+(sttNeedsReview?"sí":"no")+".");
                if(floating && other!=null)
                    store.Log("Voz flotante: "+other.Provider+" · texto "+(other.HasText?"sí":"no")+" · rechazos "+other.Rejected+".");
                if(awaitingApproval && !result.CanConfirm &&
                    !(result.HasText && recognized.Trim(' ','.','!','¡').Equals("cancela",StringComparison.OrdinalIgnoreCase)))
                {
                    lastSpeechOutcome="No entendí una confirmación clara. Di solo confirma o cancela.";
                    SpeakFloatingNotice(lastSpeechOutcome);
                    return;
                }
                if(!result.HasText)
                {
                    if(floating && ComputerContinuation)
                    { SpeakFloatingNotice("No entendí. Puedes repetir o cancelar."); return; }
                    if(desktopApproval && VisualAwaitsReply())
                    { SpeakFloatingNotice("No entendí la confirmación. Di sí o no, o revisa el texto en Visión."); return; }
                    if(desktopApproval && DesktopAwaitsAnswer())
                    { SpeakFloatingNotice(DesktopApprovalRetry()); return; }
                    if(floating && (spokenQuestion.Active(clock.Elapsed.TotalSeconds) || DesktopAwaitsAnswer()))
                    { SpeakFloatingNotice("No entendí. Puedes repetir o cancelar."); return; }
                    lastSpeechOutcome=floating && lastLevels.VoiceRms<.025?
                        "La señal del micrófono llegó baja. Prueba más cerca del micrófono o elige otra entrada en Voz.":
                        "Los reconocedores locales no entendieron la frase. Puedes probar la escucha manual desde la bandeja.";
                    if(floating) ReportFloatingFailure(lastSpeechOutcome);
                    else SetDialogueStatus(null,lastSpeechOutcome);
                    return;
                }
                lastSpeechOutcome=sttNeedsReview?"Transcripción disponible para revisar.":"Transcripción aceptada.";
                if(floating) { RouteFloating(recognized); return; }
                if(conversation.Respond(recognized,settings.Memory).Intent!=Intent.Unknown) { life.VoiceRecognized(DateTime.UtcNow); Save(); }
                entry.Text=recognized;
                entry.CaretIndex=entry.Text.Length;
                if(!settings.Intelligence.EditTranscript && !sttNeedsReview) SendLocal();
                else { entry.Focus(); SetDialogueStatus(null,"Transcripción lista para revisar. El audio se procesó localmente."); }
            }
            catch(OperationCanceledException)
            {
                if(sttDemo || !demo) lastSpeechOutcome="Transcripción cancelada o tiempo agotado.";
                if(floating && floatingActive) { transcription=null; life.ReleaseVisualCue(); ReportFloatingFailure(lastSpeechOutcome); }
                else if(panel==originalPanel) SetDialogueStatus(null,lastSpeechOutcome);
            }
            catch(Exception ex)
            {
                lastSpeechOutcome="Error del reconocedor local ("+ex.GetType().Name+").";
                if(floating && floatingActive) { transcription=null; life.ReleaseVisualCue(); ReportFloatingFailure(lastSpeechOutcome); }
                else if(panel==originalPanel) SetDialogueStatus(null,lastSpeechOutcome);
            }
            finally
            {
                Array.Clear(pcm,0,pcm.Length);
                if(prepared!=pcm)Array.Clear(prepared,0,prepared.Length);
                if(demo)sttDemo=false;
                if(transcription==owned) { transcription=null; if(floating) EndFloating(); life.ReleaseVisualCue(); }
                owned.Dispose();
            }
        }
        void StopListening()
        {
            if(capture==null && transcription==null) return;
            if(sttDemo) { sttDemo=false; lastSpeechOutcome="Prueba cancelada."; floatingDetector=null; }
            if(capture!=null) { capture.Dispose(); capture=null; }
            if(transcription!=null) transcription.Cancel();
            life.ReleaseVisualCue();
            SetDialogueStatus(null,"Captura cancelada.");
        }
        void Play(string text)
        {
            if(capture!=null || transcription!=null) return;
            StopPlayback();
            lipStatus="Preparando voz";
            ClearSpeechTrace();
            speechPending=true;
            var signals=new SpeechSignals
            {
                Started=()=>
                {
                    speechPending=false; speaking=true;
                    lips.Start();
                    lipStatus="Reproduciendo";
                },
                Viseme=(id,following,position,duration,observed)=>
                {
                    lips.Viseme(id,following,position,duration,observed);
                    visemeCount++; lastVisemePosition=position; lastVisemeDuration=duration; nextViseme=following;
                },
                Phoneme=(phoneme,position,duration,observed)=>
                {
                    phonemeCount++;
                    lips.Phoneme(phoneme,position,duration,observed);
                },
                Progress=position=>{ wordCount++; wordPosition=position; },
                Completed=(cancelled,error)=>
                {
                    FinishSpeech(error?"Windows interrumpió la reproducción.":cancelled?"Reproducción cancelada.":"Reproducción finalizada.");
                    CompleteFloatingSpeech(!cancelled && !error);
                }
            };
            string status=speechOutput.Speak(text,settings,signals);
            if(status!="Reproduciendo con Windows.") { FinishSpeech(status); CompleteFloatingSpeech(false); }
            SetDialogueStatus(null,status);
        }
        void StopPlayback()
        {
            if(!speaking && !speechPending) return;
            EndFloating();
            FinishSpeech("Reproducción detenida.");
            speechOutput.StopSpeech();
        }
        void DialogueTick(double now)
        {
            FloatingTick(now);
            RefreshSpeechDiagnostic(now);
            if(capture!=null && now>=captureUntil) FinishListening();
            if(now>=nextDialogueCue && !speaking && !speechPending && (capture!=null || transcription!=null || busyConversation))
            {
                nextDialogueCue=now+4;
                life.Cue(capture!=null?Expression.Escucha:receivingText?Expression.Satisfacción:Expression.Procesamiento,6,9);
            }
            if(liveReply!=null && now>=nextChunk)
            {
                nextChunk=now+.09;
                string delta=null;
                lock(streamBuffer)
                {
                    if(streamBuffer.Length>0) { delta=streamBuffer.ToString(); streamBuffer.Clear(); }
                }
                if(delta!=null)
                {
                    if(!receivingText)
                    {
                        receivingText=true;
                        life.Cue(Expression.Satisfacción,6,9);
                        SetDialogueStatus(null,"Respondiendo con DeepSeek…");
                    }
                    liveReply.Text+=delta;
                    transcriptScroll.ScrollToEnd();
                }
            }
        }
    }
}

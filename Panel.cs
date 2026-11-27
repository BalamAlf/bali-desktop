using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace BaliDesktop
{
    sealed class TextBlockBridge
    {
        readonly TextBlock text;
        readonly Func<string> read;
        public TextBlockBridge(TextBlock text,Func<string> read) { this.text=text; this.read=read; Update(); }
        public void Update() { string value=read(); if (text.Text!=value) text.Text=value; }
    }
    sealed partial class BaliApp
    {
        readonly IBaliIntelligenceProvider conversation = new LocalConversationProvider();
        Face gallery;
        const int DiagnosticTab = 5, AppearanceTab = 6;
        readonly System.Collections.Generic.List<Face> appearancePreviews = new System.Collections.Generic.List<Face>();
        TextBlock galleryName, response;
        TabControl tabs;

        Brush ink, muted, surface, canvas, button;

        static Brush ColorOf(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        void OpenPanel(int tab=0)
        {
            if (panel != null) { tabs.SelectedIndex=tab; panel.Activate(); return; }
            ink=ColorOf(settings.Light?"#12283A":"#E6F4FF");
            muted=ColorOf(settings.Light?"#3A566B":"#A3BDCF");
            surface=ColorOf(settings.Light?"#FFFFFF":"#152B40");
            canvas=ColorOf(settings.Light?"#E7F1F7":"#0A1726");
            button=ColorOf(settings.Light?"#CFE5F3":"#26445D");
            panel=new Window { Title="Bali Desktop",Width=720,Height=625,MinWidth=460,MinHeight=410,Background=canvas,Foreground=ink,
                FontFamily=new FontFamily("Segoe UI"),WindowStartupLocation=WindowStartupLocation.CenterScreen,Icon=IconSource() };
            var root=new DockPanel { Margin=new Thickness(22) };
            var title=Label("Bali Desktop",31);
            title.FontWeight=FontWeights.SemiBold;
            title.Margin=new Thickness(0,0,0,15);
            DockPanel.SetDock(title,Dock.Top);
            root.Children.Add(title);
            tabs=new TabControl { Background=surface,BorderBrush=button };
            root.Children.Add(tabs);
            panel.Content=root;
            ConversationPage(Page("Conversar"));
            GeneralPage(Page("General"));
            PersonalityPage(Page("Personalidad"));
            var voicePage=Page("Voz");
            LifePage(Page("Vida artificial"));
            DiagnosticPage(Page("Diagnóstico"));
            AppearancePage(Page("Apariencia"));
            var agentPage=Page("Agentes");
            var intelligencePage=Page("Inteligencia");
            var lipPage=Page("Habla");
            AddPersonalPages();
            var visionPage=Page("Visión");
            var computerPage=Page("Control de escritorio");
            bool agentsBuilt=false, voiceBuilt=false, intelligenceBuilt=false, lipsBuilt=false;
            bool visionBuilt=false;
            bool computerBuilt=false;
            tabs.SelectionChanged+=(s,e)=>
            {
                if(e.Source!=tabs) return;
                if(tabs.SelectedIndex==3 && !voiceBuilt) { voiceBuilt=true; VoicePage(voicePage); }
                if(tabs.SelectedIndex==IntelligenceTab && !intelligenceBuilt) { intelligenceBuilt=true; IntelligencePage(intelligencePage); }
                if(tabs.SelectedIndex==IntelligenceTab) RefreshIntelligence();
                if(tabs.SelectedIndex==LipTab && !lipsBuilt) { lipsBuilt=true; LipPage(lipPage); }
                if(tabs.SelectedIndex==VisionTab && !visionBuilt) {visionBuilt=true;VisionPage(visionPage);}
                if(tabs.SelectedIndex==ComputerTab && !computerBuilt) {computerBuilt=true;ComputerPage(computerPage);}
            };
            tabs.SelectionChanged+=(s,e)=>
            {
                if(e.Source!=tabs || tabs.SelectedIndex!=AgentsTab || agentsBuilt) return;
                agentsBuilt=true;
                AgentPage(agentPage);
            };
            tabs.SelectedIndex=tab;
            tabs.SelectionChanged+=(s,e)=>
            {
                if (e.Source!=tabs || tabs.SelectedIndex==DiagnosticTab) return;
                demoIndex=-1;
                preview=null;
            };
            panel.Closed += (s,e)=>{ CancelConversation(); panel=null; gallery=null; conversationFace=null; transcriptPanel=null; transcriptScroll=null;
                liveReply=null; entry=null; microphoneButton=null; providerStatus=null; response=null;
                appearancePreviews.Clear(); agentCards.Clear(); diagnostics=null; demoIndex=-1; preview=null; };
            panel.Show();
        }
        ImageSource IconSource() { return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); }
        StackPanel Page(string name)
        {
            var stack=new StackPanel { Margin=new Thickness(19), Background=surface };
            var tab = new TabItem { Header=name, Foreground=ink,
                Content=new ScrollViewer { Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto } };
            var frame = new FrameworkElementFactory(typeof(Border),"TabFrame");
            frame.SetValue(Border.BackgroundProperty,canvas);
            frame.SetValue(Border.PaddingProperty,new Thickness(10,8,10,8));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty,"Header");
            frame.AppendChild(content);
            var template = new ControlTemplate(typeof(TabItem)) { VisualTree=frame };
            var selected = new Trigger { Property=TabItem.IsSelectedProperty, Value=true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty,button,"TabFrame"));
            template.Triggers.Add(selected);
            tab.Template=template;
            tabs.Items.Add(tab);
            return stack;
        }
        TextBlock Label(string value,int size=14) { return new TextBlock { Text=value,FontSize=size,Foreground=size>16?ink:muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,7) }; }
        void Heading(Panel page,string title) { page.Children.Add(Label(title,23)); }
        Button ActionButton(Panel page,string text,Action action)
        {
            var control=new Button { Content=text,Background=button,Foreground=ink,BorderBrush=button,Padding=new Thickness(11,6,11,6),
                Margin=new Thickness(0,4,7,6),HorizontalAlignment=HorizontalAlignment.Left,MinHeight=32 };
            control.Click+=(s,e)=>action();
            page.Children.Add(control);
            return control;
        }
        void Toggle(Panel page,string title,bool initial,Action<bool> set)
        {
            var box=new CheckBox { Content=title,IsChecked=initial,Foreground=ink,Padding=new Thickness(5,0,0,0),Margin=new Thickness(0,7,0,7) };
            box.Click+=(s,e)=>{ set(box.IsChecked==true); Save(); };
            page.Children.Add(box);
        }
        void Range(Panel page,string title,double min,double max,double initial,Action<double> set,Func<double,string> format)
        {
            var label=Label(title+" · "+format(initial));
            page.Children.Add(label);
            var slider=new Slider { Minimum=min,Maximum=max,Value=initial,Margin=new Thickness(0,0,0,12),Width=280,HorizontalAlignment=HorizontalAlignment.Left };
            slider.ValueChanged+=(s,e)=>{set(e.NewValue); label.Text=title+" · "+format(e.NewValue);};
            page.Children.Add(slider);
        }
        static string Percent(double value) { return value.ToString("P0"); }
        void Respond(string text,bool floating=false,Reply resolved=null)
        {
            var reply=resolved??conversation.Respond(text,settings.Memory);
            Interact(false);
            switch (reply.Intent)
            {
                case Intent.Hello: life.Cue(Expression.Saludo,2.5,5); break;
                case Intent.Focus: ChangeMode(Mode.Concentrado); break;
                case Intent.Free:
                case Intent.Wake: ChangeMode(Mode.Libre); break;
                case Intent.Rest: ChangeMode(Mode.Descanso); break;
                case Intent.Here:
                    ShowPet();
                    var p=System.Windows.Forms.Cursor.Position;
                    Place(new System.Drawing.Point(p.X+28,p.Y+28));
                    Save();
                    break;
                case Intent.Hide: HidePet(); break;
                case Intent.Agents: OpenPanel(AgentsTab); break;
                case Intent.Life: OpenPanel(LifeTab); break;
                case Intent.Classic: ChangeAppearance(FaceStyle.Clásico); break;
                case Intent.Minimal: ChangeAppearance(FaceStyle.Minimalista); break;
                case Intent.Unknown: life.Cue(Expression.Confusión,2,5); break;
            }
            AddLine("user",text);
            AddLine("assistant",reply.Text);
            lastSpoken=reply.Text;
            RefreshTranscript();
            if((floating && floatingActive) || settings.Voice) Play(reply.Text);
            SetDialogueStatus(null,reply.Text);
        }
        void GeneralPage(Panel page)
        {
            Heading(page,"Presencia en el escritorio");
            var modes=new WrapPanel();
            page.Children.Add(modes);
            foreach (Mode mode in Enum.GetValues(typeof(Mode)))
            {
                var captured=mode;
                ActionButton(modes,mode.ToString(),()=>ChangeMode(captured));
            }
            Range(page,"Tamaño",110,400,settings.Size,n=>{settings.Size=n; Apply();},n=>String.Format("{0:0} DIP",n));
            Range(page,"Opacidad",.35,1,settings.Opacity,n=>{settings.Opacity=n; Apply();},Percent);
            var position=new WrapPanel();
            page.Children.Add(position);
            ActionButton(position,"Restaurar posición",()=>{ShowPet(); Home(); Save();});
            ActionButton(position,"Mostrar",ShowPet);
            ActionButton(position,"Ocultar",HidePet);
            Toggle(page,"Permanecer encima",settings.Top,n=>{settings.Top=n; Apply();});
            Toggle(page,"Permitir movimiento autónomo",settings.Move,n=>{settings.Move=n; if (!n) destination=null;});
            Toggle(page,"Ignorar clics en Bali",settings.Pass,n=>{settings.Pass=n; Apply();});
            Toggle(page,"Ocultar durante descanso e inactividad",settings.AutoHide,n=>settings.AutoHide=n);
            Toggle(page,"Tema claro",settings.Light,n=>
            {
                settings.Light=n;
                Dispatcher.BeginInvoke(new Action(()=>{panel.Close(); OpenPanel(1);}));
            });
            Toggle(page,"Iniciar al entrar en Windows",settings.Startup,SetStartup);
        }
        void SetStartup(bool value)
        {
            try
            {
                using (var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (value) key.SetValue("BaliDesktop","\""+Assembly.GetExecutingAssembly().Location+"\"");
                    else key.DeleteValue("BaliDesktop",false);
                }
                settings.Startup=value;
            }
            catch (Exception e) { store.Log(e.Message); MessageBox.Show(panel,"Windows no permitió cambiar el inicio automático."); }
        }
        void PersonalityPage(Panel page)
        {
            Heading(page,"Personalidad");
            Range(page,"Curiosidad",0,1,settings.Interest,n=>settings.Interest=n,Percent);
            Range(page,"Nivel de actividad",0,1,settings.Activity,n=>settings.Activity=n,Percent);
            Range(page,"Frecuencia de interacciones",0,1,settings.Frequency,n=>settings.Frequency=n,Percent);
            Range(page,"Nivel juguetón",0,1,settings.Play,n=>settings.Play=n,Percent);
            Range(page,"Frecuencia de descanso",0,1,settings.Rest,n=>settings.Rest=n,Percent);
        }
        void VoicePage(Panel page)
        {
            Heading(page,"Voz local");
            page.Children.Add(Label(voice.Status));
            Toggle(page,"Reproducir respuestas automáticamente",settings.Voice,n=>settings.Voice=n);
            Toggle(page,"Permitir escucha en el panel de conversación",settings.Microphone,n=>{settings.Microphone=n; if (!n) CancelConversation();});
            page.Children.Add(Label("Un clic en Bali flotante inicia un turno. Otro clic lo cancela. Si DeepSeek está configurado y verificado, las preguntas abiertas pueden generar una consulta. Con los clics desactivados, usa la bandeja."));
            Toggle(page,"Consultas directas a DeepSeek mediante voz",settings.Intelligence.DirectVoiceOnline,n=>settings.Intelligence.DirectVoiceOnline=n);
            Toggle(page,"Confirmar verbalmente transcripciones dudosas",settings.Intelligence.ConfirmVoiceOnline,n=>settings.Intelligence.ConfirmVoiceOnline=n);
            Toggle(page,"Abrir automáticamente el chat al consultar por voz",settings.Intelligence.AutoOpenVoiceChat,n=>settings.Intelligence.AutoOpenVoiceChat=n);
            Toggle(page,"Conversación continua desde Bali flotante",settings.Intelligence.ContinuousVoice,n=>
            { settings.Intelligence.ContinuousVoice=n; if(!n && followUp && capture!=null) CancelConversation(); });
            Toggle(page,"Usar el último tema en preguntas de seguimiento",settings.Intelligence.VoiceTopicContext,n=>
            { settings.Intelligence.VoiceTopicContext=n; if(!n)voiceSession.Clear(); });
            Range(page,"Vigencia del tema por voz",30,300,settings.Intelligence.VoiceTopicSeconds,n=>settings.Intelligence.VoiceTopicSeconds=n,n=>n.ToString("0")+" s");
            Range(page,"Silencio para terminar de hablar",.6,2.5,settings.Intelligence.VoiceSilence,n=>settings.Intelligence.VoiceSilence=n,n=>n.ToString("0.00")+" s");
            Range(page,"Espera de otra pregunta",3,18,settings.Intelligence.VoiceIdle,n=>settings.Intelligence.VoiceIdle=n,n=>n.ToString("0")+" s");
            Toggle(page,"Pulsación única para iniciar y detener",settings.Intelligence.ToggleCapture,n=>
            {
                StopListening();
                settings.Intelligence.ToggleCapture=n;
                if(microphoneButton!=null) microphoneButton.Content=n?"Iniciar / detener micrófono":"Mantén para hablar";
            });
            Toggle(page,"Revisar y editar la transcripción antes de enviar",settings.Intelligence.EditTranscript,n=>settings.Intelligence.EditTranscript=n);
            var devices=MicrophoneCapture.Devices();
            var input=new ComboBox { ItemsSource=devices,SelectedItem=devices.FirstOrDefault(d=>d.Name==settings.Intelligence.Microphone)??devices[0],
                Width=270,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,8) };
            input.SelectionChanged+=(s,e)=>{ if(input.SelectedItem!=null) { settings.Intelligence.Microphone=((InputDevice)input.SelectedItem).Name; Save(); } };
            page.Children.Add(Label("Micrófono de Windows"));
            page.Children.Add(input);
            page.Children.Add(Label("Voz de Windows"));
            var names=speechOutput.Names;
            var select=new ComboBox { ItemsSource=names,SelectedIndex=Math.Max(0,Array.IndexOf(names,settings.VoiceName)),
                Width=265,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,10) };
            select.SelectionChanged+=(s,e)=>{settings.VoiceName=select.SelectedItem as string ?? ""; Save();};
            page.Children.Add(select);
            Range(page,"Velocidad",-10,10,settings.Rate,n=>settings.Rate=(int)Math.Round(n),n=>n.ToString("0"));
            Range(page,"Volumen",0,100,settings.Volume,n=>settings.Volume=(int)Math.Round(n),n=>n.ToString("0")+" %");
            var status=Label("");
            ActionButton(page,"Probar voz",()=>{ Play("Hola. Soy Bali, tu compañero de escritorio."); status.Text=response.Text; });
            ActionButton(page,"Detener audio",()=>{ StopPlayback(); status.Text="Reproducción detenida."; });
            page.Children.Add(status);
            SpeechSettings(page);
        }
        void LifePage(Panel page)
        {
            AdaptiveLab(page);
            ActionButton(page,"Eliminar memoria de interacciones",()=>
            {
                if (MessageBox.Show(panel,"¿Eliminar las interacciones, estadísticas y familiaridad de Bali?","Memoria local",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                try
                {
                    store.Reset();
                    settings.Memory=new Memory();
                    life=new BaliLifeEngine(settings,Environment.TickCount);
                    Save();
                }
                catch (Exception e) { MessageBox.Show(panel,e.Message); }
            });
        }
        void DiagnosticPage(Panel page)
        {
            Heading(page,"Expresiones y diagnóstico");
            gallery=new Face(expressions,settings.Appearance) { Width=153, Height=132,HorizontalAlignment=HorizontalAlignment.Left };
            page.Children.Add(gallery);
            galleryName=Label("Vista en directo",16);
            page.Children.Add(galleryName);
            var actions=new WrapPanel();
            page.Children.Add(actions);
            ActionButton(actions,"Reproducir las 15",()=>{demoIndex=0; Preview(Expression.Neutral,2.4);});
            ActionButton(actions,"Sincronización labial",()=>OpenPanel(LipTab));
            ActionButton(actions,"Detener",()=>{demoIndex=-1; preview=null; galleryName.Text="Vista en directo";});
            var select=new ComboBox { ItemsSource=Enum.GetValues(typeof(Expression)),Width=146,Margin=new Thickness(0,4,0,6) };
            select.SelectionChanged+=(s,e)=>{if (select.SelectedItem!=null) {demoIndex=-1; Preview((Expression)select.SelectedItem,5);}};
            actions.Children.Add(select);
            var text=Label("");
            page.Children.Add(text);
            diagnostics=new TextBlockBridge(text,()=>String.Format("Bali Desktop 1.8 · WPF\nVida artificial activa · {0}\nCPU {1:0.00} % del equipo · RAM {2} MiB\nContexto online: {4}\nRegistro: {3}",
                voice.Status,currentCpu,currentRam,Path.Combine(store.Folder,"bali.log"),contextLabels));
            ActionButton(page,"Abrir registro técnico",()=>{var path=Path.Combine(store.Folder,"bali.log"); if(File.Exists(path))Process.Start("notepad.exe","\""+path+"\"");});
        }
        void AppearancePage(Panel page)
        {
            Heading(page,"Estilo de Bali");
            var choices=new WrapPanel();
            page.Children.Add(choices);
            foreach (FaceStyle style in Enum.GetValues(typeof(FaceStyle)))
            {
                var card=new StackPanel { Width=230, Background=canvas, Margin=new Thickness(0,5,12,9) };
                choices.Children.Add(card);
                var sample=new Face(expressions,style) { Width=188, Height=172, IsHitTestVisible=false };
                appearancePreviews.Add(sample);
                card.Children.Add(sample);
                var choose=new RadioButton
                {
                    Content=style==FaceStyle.Minimalista ? "Bali minimalista" : "Bali clásico",
                    GroupName="BaliAppearance", Foreground=ink, Margin=new Thickness(16,0,12,16),
                    IsChecked=settings.Appearance==style
                };
                choose.Checked+=(s,e)=>ChangeAppearance(style);
                card.Children.Add(choose);
            }
        }
        void ChangeAppearance(FaceStyle style)
        {
            settings.Appearance=style;
            face.StyleVariant=style;
            if (gallery!=null) gallery.StyleVariant=style;
            if (conversationFace!=null) conversationFace.StyleVariant=style;
            Save();
        }
        void Preview(Expression value,double seconds)
        {
            preview=value;
            previewUntil=clock.Elapsed.TotalSeconds+seconds;
            if (galleryName!=null) galleryName.Text=value.ToString();
        }
        void NextPreview()
        {
            demoIndex++;
            if (demoIndex<Enum.GetValues(typeof(Expression)).Length) Preview((Expression)demoIndex,2.4);
            else {demoIndex=-1; preview=null; if (galleryName!=null) galleryName.Text="Vista en directo"; }
        }
    }
}

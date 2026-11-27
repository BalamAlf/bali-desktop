using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        const int IntelligenceTab=8;
        TextBlock usageSummary, keySummary;

        void IntelligencePage(Panel page)
        {
            var options=settings.Intelligence;
            Heading(page,"Inteligencia");
            page.Children.Add(Label("Los comandos y la voz se procesan en Windows. «Consultar a DeepSeek» envía tu mensaje y el contexto que autorices. Puede generar cargos."));
            var provider=new ComboBox { ItemsSource=new[] {"Local","DeepSeek a petición"},SelectedIndex=options.Enabled?1:0,
                Width=220,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,4,0,8) };
            page.Children.Add(provider);
            provider.SelectionChanged+=(s,e)=>
            {
                options.Enabled=provider.SelectedIndex==1;
                if(!options.Enabled && turn!=null) turn.Cancel();
                Save(); RefreshProvider();
            };
            keySummary=Label("");
            page.Children.Add(keySummary);
            var password=new PasswordBox { Width=260,MaxLength=256,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,3,0,5) };
            System.Windows.Automation.AutomationProperties.SetName(password,"API Key de DeepSeek");
            page.Children.Add(password);
            var buttons=new WrapPanel();
            page.Children.Add(buttons);
            var notice=Label("");
            ActionButton(buttons,"Guardar / sustituir clave",()=>
            {
                if(busyConversation) { notice.Text="Cancela el turno antes de cambiar la credencial."; return; }
                try
                {
                    vault.Store(password.Password);
                    options.Verified=false;
                    connection="Sin prueba para esta credencial";
                    Save();
                    notice.Text="Credencial protegida con DPAPI del usuario actual.";
                }
                catch(InvalidOperationException ex) { notice.Text=ex.Message; }
                catch(Exception) { notice.Text="Windows no pudo proteger o guardar la credencial."; }
                finally { password.Clear(); RefreshIntelligence(); RefreshProvider(); }
            });
            ActionButton(buttons,"Eliminar clave",()=>
            {
                if(busyConversation || !vault.Exists) return;
                if(MessageBox.Show(panel,"¿Eliminar la credencial local guardada?","Credencial",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                try
                {
                    vault.Delete();
                    options.Verified=false; connection="Sin credencial";
                    Save(); notice.Text="Credencial eliminada.";
                }
                catch(Exception) { notice.Text="No se pudo eliminar la credencial."; }
                RefreshIntelligence(); RefreshProvider();
            });
            ActionButton(buttons,"Prueba controlada · puede generar cargo",()=>
            {
                if(busyConversation || capture!=null || transcription!=null) { notice.Text="Hay un turno o captura en curso."; return; }
                if(!options.Enabled || !vault.Exists) { notice.Text="Selecciona DeepSeek y guarda una API Key."; return; }
                if(MessageBox.Show(panel,"Se enviará una petición breve a DeepSeek con un mensaje de diagnóstico fijo y sin recuerdos. DeepSeek puede facturarla según tu cuenta. ¿Autorizar una sola petición?",
                    "Autorizar prueba facturable",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
                RunOnline("Responde únicamente con la palabra listo.",true,notice);
            });
            ActionButton(buttons,"Cancelar prueba",()=>{ if(turn!=null) turn.Cancel(); });
            page.Children.Add(notice);
            var transportResult=Label("");
            Button diagnose=null;
            diagnose=ActionButton(page,"Diagnóstico HTTPS sin cargo",async ()=>
            {
                diagnose.IsEnabled=false;
                transportResult.Text="Comprobando transporte sin enviar la clave…";
                try { transportResult.Text=await DeepSeekDiagnostics.Inspect(vault); }
                catch(Exception) { transportResult.Text="No se pudo completar el diagnóstico local."; }
                finally { diagnose.IsEnabled=true; }
            });
            page.Children.Add(transportResult);
            var model=new ComboBox { ItemsSource=new[] {"deepseek-flash","deepseek-v4-pro"},SelectedItem=options.Model,Width=220,
                HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,3,0,8) };
            page.Children.Add(Label("Modelo conversacional"));
            page.Children.Add(model);
            model.SelectionChanged+=(s,e)=>{ if(model.SelectedItem!=null) { options.Model=(string)model.SelectedItem; Save(); } };
            var temperature=Label("Temperatura · disponible con DeepSeek V4 Pro");
            page.Children.Add(temperature);
            var heat=new Slider { Minimum=0,Maximum=2,Value=options.Temperature,Width=240,HorizontalAlignment=HorizontalAlignment.Left };
            Action refreshHeat=()=>{ heat.IsEnabled=options.Model=="deepseek-v4-pro"; temperature.Text=heat.IsEnabled?
                "Temperatura · "+options.Temperature.ToString("0.00"):"Temperatura fija del proveedor en Flash"; };
            heat.ValueChanged+=(s,e)=>{ options.Temperature=e.NewValue; refreshHeat(); };
            model.SelectionChanged+=(s,e)=>refreshHeat();
            page.Children.Add(heat); refreshHeat();
            Range(page,"Tiempo de espera",5,90,options.Timeout,n=>options.Timeout=(int)n,n=>n.ToString("0")+" s");
            Heading(page,"Contexto autorizado");
            Toggle(page,"Compartir estados y objetivos de Bali si son relevantes",options.ShareState,n=>options.ShareState=n);
            Toggle(page,"Compartir hábitos agregados si son relevantes",options.ShareHabits,n=>options.ShareHabits=n);
            Toggle(page,"Compartir últimos mensajes de esta sesión",options.ShareHistory,n=>options.ShareHistory=n);
            Toggle(page,"Compartir recuerdos autorizados que coincidan con la pregunta",options.ShareMemories,n=>options.ShareMemories=n);
            Toggle(page,"Habilitar memoria conversacional",options.Remember,n=>options.Remember=n);
            ActionButton(page,"Revisar, editar o eliminar recuerdos",()=>MemoryDialog(""));
            Heading(page,"Consumo");
            page.Children.Add(Label("El presupuesto usa una estimación a tarifa pico. El cargo real y las solicitudes hechas fuera de Bali dependen de tu cuenta de DeepSeek."));
            Range(page,"Peticiones diarias",0,100,options.Daily,n=>options.Daily=(int)n,n=>n.ToString("0"));
            Range(page,"Tokens máximos de salida",16,1024,options.Output,n=>options.Output=(int)n,n=>n.ToString("0"));
            Range(page,"Presupuesto de contexto UTF-8",1024,12000,options.Context,n=>options.Context=(int)n,n=>n.ToString("0")+" bytes");
            var budget=new TextBox { Text=options.Monthly.ToString(CultureInfo.CurrentCulture),Width=125,HorizontalAlignment=HorizontalAlignment.Left };
            page.Children.Add(Label("Presupuesto mensual estimado en dólares, de 0 a 50"));
            page.Children.Add(budget);
            var feedback=Label("");
            ActionButton(page,"Guardar presupuesto",()=>
            {
                decimal count;
                if(!Decimal.TryParse(budget.Text,NumberStyles.AllowDecimalPoint,CultureInfo.CurrentCulture,out count) || count<0 || count>50)
                { feedback.Text="Introduce un importe válido entre 0 y 50."; return; }
                options.Monthly=count; Save(); feedback.Text="Presupuesto actualizado."; RefreshIntelligence();
            });
            page.Children.Add(feedback);
            usageSummary=Label("");
            page.Children.Add(usageSummary);
            RefreshIntelligence();
            panel.Closed+=(s,e)=>{ usageSummary=null; keySummary=null; };
        }
        void RefreshIntelligence()
        {
            if(keySummary!=null) keySummary.Text=vault.Exists?"API Key · •••••••• · "+(settings.Intelligence.Verified?"verificada":"pendiente de prueba"):
                "API Key · sin configurar";
            if(usageSummary!=null)
            {
                usageSummary.Text=usageLedger.Report();
                var options=settings.Intelligence;
                if(options.Daily>0 && usageLedger.Today(DateTime.UtcNow)>=Math.Ceiling(options.Daily*.8) ||
                    options.Monthly>0 && usageLedger.Month(DateTime.UtcNow)>=options.Monthly*.8m)
                    usageSummary.Text+="\nAviso: se alcanzó al menos el 80 % de un límite local.";
            }
        }

        void MemoryDialog(string draft)
        {
            var dialog=new Window { Title="Memoria conversacional",Width=540,Height=590,Owner=panel,
                Background=surface,Foreground=ink,WindowStartupLocation=WindowStartupLocation.CenterOwner };
            var body=new StackPanel { Margin=new Thickness(18) };
            dialog.Content=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
            var list=new ComboBox { MinWidth=300,Margin=new Thickness(0,5,0,10) };
            body.Children.Add(Label("Solo se guardan recuerdos que apruebes aquí. Evita credenciales y otros datos sensibles."));
            body.Children.Add(list);
            var text=new TextBox { AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=480,Height=105 };
            body.Children.Add(text);
            var category=new ComboBox { ItemsSource=MemoryBook.Categories,SelectedIndex=1,Width=220,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,7,0,5) };
            body.Children.Add(category);
            body.Children.Add(Label("Relevancia, de 1 a 3"));
            var relevance=new Slider { Minimum=1,Maximum=3,Value=2,TickFrequency=1,IsSnapToTickEnabled=true,
                Width=180,HorizontalAlignment=HorizontalAlignment.Left };
            body.Children.Add(relevance);
            var share=new CheckBox { Content="Permitir compartir este recuerdo si el contexto lo requiere",Foreground=ink,Margin=new Thickness(0,6,0,6) };
            var approve=new CheckBox { Content="Apruebo guardar este texto en memoria local",Foreground=ink,Margin=new Thickness(0,6,0,6) };
            body.Children.Add(share); body.Children.Add(approve);
            var info=Label("");
            body.Children.Add(info);
            Action<Guid?> refresh=id=>
            {
                list.Items.Clear();
                list.Items.Add(new ComboBoxItem { Content="Nuevo recuerdo" });
                foreach(var record in memoryBook.Entries.OrderByDescending(x=>x.Date))
                    list.Items.Add(new ComboBoxItem { Content=record.Date.ToLocalTime().ToString("d")+" · "+record.Category+" · "+record.Text.Substring(0,Math.Min(42,record.Text.Length)),Tag=record });
                var chosen=list.Items.Cast<ComboBoxItem>().FirstOrDefault(x=>x.Tag is ConversationMemory && ((ConversationMemory)x.Tag).Id==id);
                list.SelectedItem=chosen??list.Items[0];
            };
            list.SelectionChanged+=(s,e)=>
            {
                var item=list.SelectedItem as ComboBoxItem;
                var old=item==null?null:item.Tag as ConversationMemory;
                text.Text=old==null?"":old.Text;
                category.SelectedItem=old==null?MemoryBook.Categories[1]:old.Category;
                relevance.Value=old==null?2:old.Relevance;
                share.IsChecked=old!=null && old.Share;
                approve.IsChecked=false;
                info.Text=old==null?"":old.Source+" · "+old.Confidence.ToString("P0")+" confianza de procedencia · relevancia "+old.Relevance;
            };
            var controls=new WrapPanel();
            body.Children.Add(controls);
            ActionButton(controls,"Guardar recuerdo",()=>
            {
                if(!settings.Intelligence.Remember) { info.Text="Habilita la memoria en Inteligencia para guardar."; return; }
                try
                {
                    var old=((ComboBoxItem)list.SelectedItem).Tag as ConversationMemory;
                    var saved=memoryBook.Save(old==null?(Guid?)null:old.Id,text.Text,(string)category.SelectedItem,
                        (int)relevance.Value,approve.IsChecked==true,share.IsChecked==true);
                    refresh(saved.Id);
                    info.Text="Recuerdo aprobado y guardado.";
                }
                catch(InvalidOperationException ex) { info.Text=ex.Message; }
                catch(Exception) { info.Text="No se pudo escribir la memoria local."; }
            });
            ActionButton(controls,"Eliminar recuerdo",()=>
            {
                var old=((ComboBoxItem)list.SelectedItem).Tag as ConversationMemory;
                if(old==null || MessageBox.Show(dialog,"¿Eliminar el recuerdo seleccionado?","Memoria",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                try { memoryBook.Delete(old.Id); refresh(null); info.Text="Recuerdo eliminado."; }
                catch(Exception) { info.Text="No se pudo eliminar el recuerdo."; }
            });
            refresh(null);
            if(!String.IsNullOrEmpty(draft)) text.Text=draft.Substring(0,Math.Min(480,draft.Length));
            dialog.ShowDialog();
        }
    }
}

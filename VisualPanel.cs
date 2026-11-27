using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Drawing=System.Drawing;
using Forms=System.Windows.Forms;

namespace BaliDesktop
{
    sealed partial class BaliApp
    {
        const int VisionTab=AgendaTab+6;
        Window visualIndicator;
        IntPtr VisualMouseActivate(IntPtr source,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(message!=0x21 || visualSettings==null || !visualSettings.Enabled && !ComputerEnabled)return IntPtr.Zero;
            RememberVisualForeground();
            RememberComputerWindow();
            handled=true;
            return new IntPtr(3);
        }
        void BeginVisualIndicator()
        {
            visualIndicator=new Window{Title="Visión de Bali",Content=new TextBlock{Text="Bali está analizando la pantalla",
                Margin=new Thickness(12),Foreground=System.Windows.Media.Brushes.White,Background=System.Windows.Media.Brushes.DarkSlateBlue},
                SizeToContent=SizeToContent.WidthAndHeight,ShowInTaskbar=false,ShowActivated=false,Topmost=true,
                WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=SystemParameters.WorkArea.Left+8,Top=SystemParameters.WorkArea.Top+8,
                IsHitTestVisible=false};
            visualIndicator.Show();
        }
        void EndVisualIndicator()
        {
            if(visualIndicator!=null){visualIndicator.Close();visualIndicator=null;}
        }
        void SetVisionPermission(Action change)
        {change();ClearVision();SaveVision();}
        void VisionPage(Panel page)
        {
            Heading(page,"Visión");
            page.Children.Add(Label("El alcance inicial es una ventana. El texto y las capturas se mantienen en memoria. DeepSeek requiere aprobación aparte."));
            Toggle(page,"Activar Visión",visualSettings.Enabled,n=>SetVisionPermission(()=>visualSettings.Enabled=n));
            Toggle(page,"Identificar ventanas",visualSettings.Identify,n=>SetVisionPermission(()=>visualSettings.Identify=n));
            Toggle(page,"Leer controles visibles por UI Automation",visualSettings.Accessible,n=>SetVisionPermission(()=>visualSettings.Accessible=n));
            Toggle(page,"Permitir capturar una ventana",visualSettings.WindowCapture,n=>SetVisionPermission(()=>visualSettings.WindowCapture=n));
            Toggle(page,"Permitir capturar una región",visualSettings.RegionCapture,n=>SetVisionPermission(()=>visualSettings.RegionCapture=n));
            Toggle(page,"Permitir capturar un monitor",visualSettings.MonitorCapture,n=>SetVisionPermission(()=>visualSettings.MonitorCapture=n));
            Toggle(page,"Permitir OCR local",visualSettings.Ocr,n=>SetVisionPermission(()=>visualSettings.Ocr=n));
            Toggle(page,"Permitir aprobar envíos de texto a DeepSeek",visualSettings.Share,n=>SetVisionPermission(()=>visualSettings.Share=n));
            var languages=visualOcr.Languages();
            page.Children.Add(Label(languages.Length==0?"Windows OCR · sin idiomas disponibles":"Windows OCR · "+String.Join(", ",languages)));
            var language=Choice(page,"Idioma OCR",new[]{"Automático"}.Concat(languages).ToArray());
            language.SelectedItem=languages.Contains(visualSettings.Language)?visualSettings.Language:"Automático";
            language.SelectionChanged+=(s,e)=>SetVisionPermission(()=>visualSettings.Language=language.SelectedIndex<=0?"":(string)language.SelectedItem);
            var exclusion=Field(page,"Excluir por proceso o texto del título");
            exclusion.MaxLength=120;
            var excluded=new ListBox{Height=75,ItemsSource=visualSettings.Exclusions.ToArray()};page.Children.Add(excluded);
            ActionButton(page,"Añadir exclusión",()=>
            {
                string value=exclusion.Text.Trim();
                if(value.Length<2 || visualSettings.Exclusions.Count>=64)return;
                SetVisionPermission(()=>{if(!visualSettings.Exclusions.Contains(value,StringComparer.OrdinalIgnoreCase))visualSettings.Exclusions.Add(value);});
                excluded.ItemsSource=visualSettings.Exclusions.ToArray();exclusion.Clear();
            });
            ActionButton(page,"Quitar exclusión seleccionada",()=>
            {
                var selected=excluded.SelectedItem as string;if(selected==null)return;
                SetVisionPermission(()=>visualSettings.Exclusions.Remove(selected));
                excluded.ItemsSource=visualSettings.Exclusions.ToArray();
            });
            var windows=new ListBox{Height=130};page.Children.Add(windows);
            var status=Label("");page.Children.Add(status);
            Action list=()=>
            {
                windows.ItemsSource=visualSettings.Enabled && visualSettings.Identify?
                    VisualWindows.List().Where(w=>!VisualWindows.Excluded(w,visualSettings.Exclusions)).ToArray():new VisualWindow[0];
                status.Text="Ventanas disponibles: "+windows.Items.Count+".";
            };
            ActionButton(page,"Actualizar ventanas disponibles",list);
            ActionButton(page,"Leer ventana seleccionada",()=>
            {
                var target=windows.SelectedItem as VisualWindow;
                if(target==null){status.Text="Selecciona una ventana.";return;}
                BeginVisual(target,"ventana",Drawing.Rectangle.Empty,false,false,"lee esta ventana",false,false,m=>status.Text=m);
            });
            ActionButton(page,"Capturar ventana seleccionada en 3 segundos",async ()=>
            {
                var target=windows.SelectedItem as VisualWindow;
                if(target==null || !visualSettings.Enabled || !visualSettings.WindowCapture){status.Text="Selecciona una ventana y permite su captura.";return;}
                await DelayVisualCapture(target,"ventana",Drawing.Rectangle.Empty,status);
            });
            var screen=Choice(page,"Monitor",Forms.Screen.AllScreens.Select(x=>x.DeviceName).ToArray());
            var coords=Field(page,"Región en píxeles: x, y, ancho, alto");
            coords.MaxLength=75;
            ActionButton(page,"Capturar monitor en 3 segundos",async ()=>
            {
                if(!visualSettings.Enabled || !visualSettings.MonitorCapture){status.Text="Activa el permiso de monitor.";return;}
                var selected=Forms.Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==(string)screen.SelectedItem);
                if(selected!=null)await DelayVisualCapture(null,"monitor",selected.Bounds,status);
            });
            ActionButton(page,"Capturar región en 3 segundos",async ()=>
            {
                if(!visualSettings.Enabled || !visualSettings.RegionCapture){status.Text="Activa el permiso de región.";return;}
                var values=coords.Text.Split(',');
                int x,y,w,h;
                if(values.Length!=4 || !Int32.TryParse(values[0],out x) || !Int32.TryParse(values[1],out y) ||
                    !Int32.TryParse(values[2],out w) || !Int32.TryParse(values[3],out h) ||
                    Math.Abs((long)x)>100000 || Math.Abs((long)y)>100000 || w<30 || w>4096 || h<25 || h>4096)
                {status.Text="Usa cuatro enteros, por ejemplo 40, 60, 640, 360.";return;}
                await DelayVisualCapture(null,"región",new Drawing.Rectangle(x,y,w,h),status);
            });
            var image=new Image{MaxHeight=260,HorizontalAlignment=HorizontalAlignment.Left};page.Children.Add(image);
            var data=Label("");page.Children.Add(data);
            var elements=new ListBox{Height=130};page.Children.Add(elements);
            var question=Field(page,"Pregunta sobre el texto observado","Explica lo que aparece en la ventana.");
            question.MaxLength=400;
            var exact=Label("");page.Children.Add(exact);
            refreshVision=()=>
            {
                if(!visualSettings.Enabled || !visualSettings.Identify)windows.ItemsSource=new VisualWindow[0];
                var current=visualContext;
                image.Source=current==null?null:current.Preview;
                elements.ItemsSource=current==null || current.Protected?null:current.Elements;
                data.Text=current==null?"Sin contexto visual.":String.Format(CultureInfo.CurrentCulture,
                    "{0} · {1} · {2:T}\nIdentificación {3:0} ms · UIA {4:0} ms · captura {5:0} ms · OCR {6:0} ms\n{7}\n{8}\nControles: {9}",
                    current.Scope,current.Method,current.Observed.ToLocalTime(),current.WindowTime.TotalMilliseconds,
                    current.AccessibleTime.TotalMilliseconds,current.CaptureTime.TotalMilliseconds,current.OcrTime.TotalMilliseconds,
                    current.Limitation,current.Protected?"Observación protegida.":current.Text,String.Join(", ",current.Controls));
                exact.Text="Último fragmento autorizado para envío:\n"+visualShared+
                    (String.IsNullOrWhiteSpace(visualOnlineResult)?"":"\n\nResultado:\n"+visualOnlineResult);
            };
            ActionButton(page,"Previsualizar texto y autorizar envío",()=>
            {
                if(!HasVisualContext(true) || !visualSettings.Share || !toolsRouter.Allowed("vision-compartir") ||
                    !settings.Intelligence.Enabled || !settings.Intelligence.Verified || !vault.Exists)
                {status.Text="Hace falta una observación vigente y permisos de DeepSeek.";return;}
                string excerpt=VisualText.ForProvider(visualContext.Text);
                if(String.IsNullOrWhiteSpace(excerpt)){status.Text="No hay texto que se pueda compartir.";return;}
                if(MessageBox.Show(panel,"Se enviará únicamente este fragmento. Puede generar consumo.\n\n"+excerpt,
                    "Confirmar envío de texto",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                visualGrant.Allow(visualContext,DateTime.UtcNow);
                SendVisual(question.Text.Trim(),false,true);
            });
            ActionButton(page,"Cancelar y borrar contexto visual",()=>{ClearVision();status.Text="Contexto borrado.";});
            refreshVision();list();
            panel.Closed+=(s,e)=>{refreshVision=null;ClearVision();};
        }
        async Task DelayVisualCapture(VisualWindow target,string scope,Drawing.Rectangle area,TextBlock status)
        {
            if(visualWork!=null || delayedVision)return;
            delayedVision=true;
            var currentPanel=panel;
            status.Text="Cambia a la ventana que deseas capturar.";
            currentPanel.Hide();
            try
            {
                await Task.Delay(3000);
                if(panel!=currentPanel || !visualSettings.Enabled)return;
                BeginVisual(target,scope,area,true,true,"lee",false,false,message=>
                {
                    if(panel!=currentPanel)return;
                    status.Text=message;
                    currentPanel.Show();
                    currentPanel.Activate();
                });
                if(visualWork==null)currentPanel.Show();
            }
            finally
            {
                delayedVision=false;
                if(visualWork==null && panel==currentPanel && !currentPanel.IsVisible)currentPanel.Show();
            }
        }
        bool delayedVision;
    }
}

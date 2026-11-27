using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BaliDesktop
{
    [DataContract] sealed class PersonalItem
    {
        [DataMember] public Guid Id=Guid.NewGuid(), Related;
        [DataMember] public string Kind, Text, Project, Tags, Repeat, Due, State="Activo";
        [DataMember] public int Priority=2, Notices;
        [DataMember] public DateTime Created=DateTime.UtcNow;
        [DataMember] public string LastNotice;
        public DateTime? Date
        {
            get { DateTime d; return DateTime.TryParseExact(Due,"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out d)?(DateTime?)d:null; }
            set { Due=value.HasValue?value.Value.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture):null; }
        }
        public override string ToString(){return Text+" · "+State+(Due==null?"":" · "+Due)+(String.IsNullOrEmpty(Project)?"":" · "+Project);}
    }
    [DataContract] sealed class PersonalApp
    {
        [DataMember] public string Name, Alias, Path;
        public override string ToString(){return Name;}
    }
    [DataContract] sealed class PersonalData
    {
        [DataMember] public List<PersonalItem> Items=new List<PersonalItem>();
        [DataMember] public List<PersonalApp> Apps=new List<PersonalApp>();
        [DataMember] public List<string> Disabled=new List<string>(), Audit=new List<string>();
        [DataMember] public bool Initialized, SpeakFocus, SpeakRest, Suggest;
    }
    sealed class PersonalStore
    {
        readonly string path;
        public PersonalData Data {get;private set;}
        public bool Available {get;private set;}
        public PersonalStore(string folder)
        {
            path=Path.Combine(folder,"asistente-personal.json");
            try
            {
                Data=File.Exists(path)?LocalJson.Read<PersonalData>(path):new PersonalData();
                Available=Data!=null && Data.Items!=null && Data.Apps!=null && Data.Disabled!=null && Data.Audit!=null;
            }
            catch(Exception) { Available=false; }
            if(!Available) Data=new PersonalData();
        }
        public void Change(Action<PersonalData> edit)
        {
            if(!Available) throw new InvalidOperationException("Datos locales no disponibles.");
            var serializer=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(PersonalData));
            using(var stream=new MemoryStream())
            {
                serializer.WriteObject(stream,Data); stream.Position=0;
                var next=(PersonalData)serializer.ReadObject(stream);
                edit(next);
                LocalJson.Write(path,next);
                Data=next;
            }
        }
        public PersonalItem[] TakeDue(DateTime now)
        {
            if(!Available || Data.Disabled.Contains("recordar")) return new PersonalItem[0];
            var due=Data.Items.Where(x=>x.Kind=="Recordatorio" && x.State=="Activo" && x.Date.HasValue && x.Date<=now).Take(20).ToArray();
            if(due.Length==0) return due;
            var ids=new HashSet<Guid>(due.Select(x=>x.Id));
            Change(copy=>
            {
                foreach(var row in copy.Items.Where(x=>ids.Contains(x.Id)))
                {
                    row.Notices++; row.LastNotice=now.ToString("s");
                    int n=row.Repeat=="Diario"?1:row.Repeat=="Semanal"?7:0;
                    if(n==0) row.State="Notificado";
                    else
                    {
                        var date=row.Date.Value;
                        var next=date.AddDays((now.Date-date.Date).Days/n*n);
                        row.Date=next<=now?next.AddDays(n):next;
                    }
                }
            });
            return due;
        }
    }
    enum ToolRisk { Consulta, Acción, Confirmación }
    sealed class ToolRequest
    {
        public string Id,Text,Kind,Project,Tags,Repeat,Operation,Path;
        public Guid Item,Related;
        public DateTime? Date;
        public int Priority=2;
    }
    sealed class ToolResult
    {
        public readonly bool Ok;
        public readonly string Message;
        public ToolResult(bool ok,string message){Ok=ok;Message=message;}
    }
    interface IBaliTool
    {
        string Id {get;}
        string Description {get;}
        string Parameters {get;}
        ToolRisk Risk {get;}
        bool Offline {get;}
        ToolResult Execute(ToolRequest request);
    }
    sealed class LocalTool : IBaliTool
    {
        readonly Func<ToolRequest,ToolResult> action;
        public string Id{get;private set;}
        public string Description{get;private set;}
        public string Parameters{get;private set;}
        public ToolRisk Risk{get;private set;}
        public bool Offline{get{return true;}}
        public LocalTool(string id,string name,string parameters,ToolRisk risk,Func<ToolRequest,ToolResult> action)
        { Id=id;Description=name;Parameters=parameters;Risk=risk;this.action=action; }
        public ToolResult Execute(ToolRequest request){return action(request);}
    }
    sealed partial class BaliToolRouter
    {
        public static readonly string[] Kinds={"Proyecto","Nota","Idea","Decisión","Preferencia","Pendiente","Recuerdo","Recordatorio"};
        readonly PersonalStore store;
        readonly Func<PersonalApp,bool> open;
        readonly Dictionary<string,IBaliTool> registry=new Dictionary<string,IBaliTool>();
        readonly Dictionary<string,DateTime> recent=new Dictionary<string,DateTime>();
        ToolRequest pending;
        string pendingOrigin;
        DateTime expires;
        public IEnumerable<IBaliTool> Tools{get{return registry.Values;}}
        public Func<string,string> DocumentQuery;
        public BaliToolRouter(PersonalStore store,Func<PersonalApp,bool> open)
        {
            this.store=store;this.open=open;
            Add("recordar","Crear recordatorio","texto, fecha, recurrencia",ToolRisk.Acción,r=>Create(r,"Recordatorio"));
            Add("pendiente","Crear pendiente","texto, fecha, proyecto, prioridad",ToolRisk.Acción,r=>Create(r,"Pendiente"));
            Add("nota","Guardar conocimiento","texto, categoría, etiquetas, relación",ToolRisk.Acción,r=>Create(r,r.Kind??"Nota"));
            Add("agenda","Consultar agenda","tipo, fecha y búsqueda",ToolRisk.Consulta,Query);
            Add("conocimiento","Consultar conocimiento","términos",ToolRisk.Consulta,Query);
            Add("abrir","Abrir aplicación registrada","nombre o alias exacto",ToolRisk.Acción,Open);
            Add("editar","Actualizar un elemento","ID, cambios",ToolRisk.Acción,Edit);
            Add("borrar","Eliminar o cancelar","ID",ToolRisk.Confirmación,Remove);
            Add("exportar","Exportar Markdown","ID y destino elegido en panel",ToolRisk.Confirmación,Export);
            Add("documentos","Consultar documento activo","términos",ToolRisk.Consulta,r=>Yes(DocumentQuery==null?"Selecciona un documento en el panel.":DocumentQuery(r.Text)));
        }
        void Add(string id,string name,string parameters,ToolRisk risk,Func<ToolRequest,ToolResult> operation)
        { registry.Add(id,new LocalTool(id,name,parameters,risk,operation)); }
        static ToolResult Yes(string text){return new ToolResult(true,text);}
        static ToolResult No(string text){return new ToolResult(false,text);}
        public bool Allowed(string id){return store.Available && !store.Data.Disabled.Contains(id);}
        public bool ReviewVoice;
        public string LastIntent{get;private set;}
        public string LastParameters{get;private set;}
        public bool AwaitsApproval(string origin,DateTime now){return pending!=null && pendingOrigin==origin && now<=expires;}
        public void Cancel(){pending=null;CancelNatural();}
        public void Cancel(string origin)
        {
            if(pending!=null && pendingOrigin==origin)pending=null;
            if(missingTimeOrigin==origin)CancelNatural();
        }
        public void Audit(string id,string origin,string permit,string outcome)
        {
            try { store.Change(x=>{x.Audit.Add(DateTime.Now.ToString("s")+" · "+id+" · "+origin+" · "+permit+" · "+outcome);
                if(x.Audit.Count>240) x.Audit.RemoveAt(0);}); }
            catch(IOException){} catch(UnauthorizedAccessException){} catch(InvalidOperationException){}
        }
        public ToolResult Execute(ToolRequest request,string origin,DateTime now,bool confirmed=false)
        {
            IBaliTool tool;
            if(request==null || !registry.TryGetValue(request.Id??"",out tool) || (origin!="voz" && origin!="panel" && origin!="texto"))
                return No("Solicitud no autorizada.");
            if(!Allowed(tool.Id)){Audit(tool.Id,origin,"denegada","desactivada");return No("La herramienta está desactivada.");}
            LastIntent=tool.Description;
            LastParameters=(request.Text??"")+(request.Date.HasValue?" · "+request.Date.Value.ToString("g"):"");
            if(request.Item!=Guid.Empty)
            {
                var target=store.Data.Items.FirstOrDefault(x=>x.Id==request.Item);
                if(target!=null)LastParameters=target.Text+" "+(request.Operation??"")+" "+LastParameters;
            }
            if((tool.Risk==ToolRisk.Confirmación || origin=="voz" && ReviewVoice && tool.Risk==ToolRisk.Acción) && !confirmed)
            {
                if(tool.Id=="exportar") return No("Confirma la exportación en el panel.");
                if(pending!=null && now<=expires) return No("Confirma o cancela primero la acción anterior.");
                pending=request; pendingOrigin=origin; expires=now.AddSeconds(45);
                Audit(tool.Id,origin,"pendiente","confirmación");
                if(ReviewVoice)
                    return No("Entendí: "+tool.Description+" "+LastParameters+". Di confirma para autorizar, o cancela.");
                return No("Di o escribe confirma para autorizar, o cancela.");
            }
            if(tool.Id=="exportar" && origin!="panel") return No("Elige el destino en el panel.");
            string signature=tool.Id+"|"+request.Item+"|"+request.Operation+"|"+request.Text+"|"+request.Repeat;
            foreach(string key in recent.Where(x=>(now-x.Value).TotalSeconds>=6).Select(x=>x.Key).ToArray()) recent.Remove(key);
            if(tool.Risk!=ToolRisk.Consulta && recent.ContainsKey(signature)) return No("La acción ya se procesó recientemente.");
            if(tool.Risk!=ToolRisk.Consulta) recent[signature]=now;
            try
            {
                var result=tool.Execute(request);
                Audit(tool.Id,origin,confirmed?"confirmada":"autorizada",result.Ok?"correcta":"rechazada");
                return result;
            }
            catch(Exception e){Audit(tool.Id,origin,"autorizada",e.GetType().Name);return No("No pude completar la acción.");}
        }
        ToolResult Create(ToolRequest r,string kind)
        {
            if(!Valid(r,kind) || store.Data.Items.Count>=2000) return No("Revisa el tipo, texto, fecha o relación.");
            if(kind=="Recordatorio" && store.Data.Items.Any(x=>x.Kind==kind && x.State=="Activo" && x.Text==r.Text.Trim() &&
                x.Date.HasValue && Math.Abs((x.Date.Value-r.Date.Value).TotalSeconds)<15 && x.Repeat==r.Repeat))
                return No("Ya existe un recordatorio equivalente.");
            var item=new PersonalItem {Kind=kind};
            Assign(item,r);
            store.Change(x=>x.Items.Add(item));
            return Yes(kind=="Recordatorio"?"Recordatorio guardado para "+item.Date.Value.ToString("dd/MM HH:mm")+".":kind+" guardado.");
        }
        bool Valid(ToolRequest r,string kind)
        {
            int length=(r.Text??"").Trim().Length;
            return Kinds.Contains(kind) && length>=2 && length<=1500 && (r.Project??"").Length<=80 && (r.Tags??"").Length<=240 &&
                (kind!="Recordatorio" || r.Date.HasValue && (r.Repeat==null || r.Repeat=="Diario" || r.Repeat=="Semanal")) &&
                (r.Related==Guid.Empty || r.Related!=r.Item && store.Data.Items.Any(x=>x.Id==r.Related));
        }
        static void Assign(PersonalItem item,ToolRequest r)
        {
            item.Text=r.Text.Trim(); item.Project=r.Project; item.Tags=r.Tags; item.Related=r.Related;
            item.Date=r.Date; item.Repeat=item.Kind=="Recordatorio"?r.Repeat:null; item.Priority=Math.Max(1,Math.Min(3,r.Priority));
        }
        public static bool Matches(PersonalItem item,string q)
        { return (item.Text+" "+item.Kind+" "+item.Project+" "+item.Tags).IndexOf(q??"",StringComparison.OrdinalIgnoreCase)>=0; }
        ToolResult Query(ToolRequest r)
        {
            var rows=store.Data.Items.Where(x=>x.State=="Activo" && Matches(x,r.Text));
            rows=rows.Where(x=>r.Id=="agenda"?(r.Kind=="Hoy"?x.Kind=="Pendiente" || x.Kind=="Recordatorio":x.Kind==(r.Kind??"Pendiente")):x.Kind!="Recordatorio");
            if(r.Date.HasValue) rows=rows.Where(x=>x.Date.HasValue && x.Date.Value.Date==r.Date.Value.Date);
            var parts=rows.OrderBy(x=>x.Date??DateTime.MaxValue).ThenByDescending(x=>x.Priority).Take(6)
                .Select(x=>x.Text.Substring(0,Math.Min(180,x.Text.Length))).ToArray();
            if(parts.Length==0 && r.Id=="agenda" && r.Kind=="Hoy")
                return Yes("No tienes pendientes ni recordatorios programados para hoy.");
            return Yes(parts.Length==0?"No hay coincidencias activas.":String.Join(". ",parts));
        }
        ToolResult Edit(ToolRequest r)
        {
            var old=store.Data.Items.Find(x=>x.Id==r.Item);
            if(old==null || (r.Operation==null && !Valid(r,old.Kind))) return No("Revisa el elemento y sus datos.");
            store.Change(data=>
            {
                var item=data.Items.Find(x=>x.Id==r.Item);
                switch(r.Operation)
                {
                    case "completar": item.State="Completado"; break;
                    case "archivar": item.State="Archivado"; break;
                    case "posponer": item.Date=r.Date??DateTime.Now.AddMinutes(10);item.State="Activo";break;
                    case null: Assign(item,r); if(item.Kind=="Recordatorio") item.State="Activo"; break;
                    default: throw new InvalidOperationException();
                }
            });
            return Yes("Elemento actualizado.");
        }
        ToolResult Remove(ToolRequest r)
        {
            var old=store.Data.Items.Find(x=>x.Id==r.Item);
            if(old==null) return No("El elemento ya no existe.");
            store.Change(data=>
            {
                if(old.Kind=="Recordatorio") data.Items.Find(x=>x.Id==old.Id).State="Cancelado";
                else
                {
                    data.Items.RemoveAll(x=>x.Id==old.Id);
                    foreach(var item in data.Items.Where(x=>x.Related==old.Id)) item.Related=Guid.Empty;
                }
            });
            return Yes(old.Kind=="Recordatorio"?"Recordatorio cancelado.":"Elemento eliminado.");
        }
        ToolResult Export(ToolRequest r)
        {
            var item=store.Data.Items.Find(x=>x.Id==r.Item);
            if(item==null || String.IsNullOrEmpty(r.Path) || !Path.IsPathRooted(r.Path) ||
                !Path.GetExtension(r.Path).Equals(".md",StringComparison.OrdinalIgnoreCase)) return No("Indica un archivo Markdown nuevo.");
            using(var output=new FileStream(r.Path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(output)) {writer.WriteLine("# "+item.Kind);writer.WriteLine();writer.WriteLine(item.Text);}
            return Yes("Exportación terminada.");
        }
        ToolResult Open(ToolRequest r)
        {
            string name=(r.Text??"").Trim();
            var apps=store.Data.Apps.Where(x=>Same(x.Name,name)||(x.Alias??"").Split(',').Any(a=>Same(a.Trim(),name))).ToArray();
            return apps.Length==1 && SafeExecutable(apps[0].Path) && open(apps[0])?
                Yes("Windows inició "+apps[0].Name+"."):No("No pude iniciar una aplicación autorizada y única con ese nombre.");
        }
        static bool Same(string a,string b){return !String.IsNullOrWhiteSpace(a) && String.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
        public static bool SafeExecutable(string path)
        {
            if(String.IsNullOrEmpty(path) || path.Length<4 || !Char.IsLetter(path[0]) || path[1]!=':' || path[2]!=(char)92 ||
                path.Substring(2).Contains(":") || !Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
            if(new DriveInfo(Path.GetPathRoot(path)).DriveType!=DriveType.Fixed) return false;
            string[] forbidden={"cmd","powershell","pwsh","wscript","cscript","mshta","rundll32","regsvr32","wt","bash","wsl","python","node","reg","msiexec"};
            return !forbidden.Contains(Path.GetFileNameWithoutExtension(path),StringComparer.OrdinalIgnoreCase);
        }
        public static bool Launch(PersonalApp app)
        {
            if(!SafeExecutable(app.Path)) return false;
            using(var p=Process.Start(new ProcessStartInfo {FileName=app.Path,UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(app.Path)}))
                return p!=null;
        }
    }
}

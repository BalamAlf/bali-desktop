using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace BaliDesktop
{
    sealed class DesktopApp
    {
        public string Name, Id, Path, Origin, Alias, Root, Project;
        public bool Portable { get { return Root!=null; } }
        public bool Packaged { get { return Path==null; } }
        public override string ToString(){return Name+" · "+Origin+(String.IsNullOrEmpty(Project)?"":" · "+Project);}
    }
    [DataContract] sealed class DesktopPermission
    {
        [DataMember] public string Id, Hash, Alias, Project;
        [DataMember] public string Name;
        [DataMember] public bool Excluded;
        [DataMember] public bool OpenByExactVoice;
    }
    [DataContract] sealed class DesktopConfig
    {
        [DataMember] public List<string> Folders=new List<string>();
        [DataMember] public List<DesktopPermission> Permissions=new List<DesktopPermission>();
        [DataMember] public List<TechnicalWord> Words=new List<TechnicalWord>();
        [DataMember] public List<DesktopRoutine> Routines=new List<DesktopRoutine>();
        [DataMember] public List<string> Maintenance=new List<string>();
        [DataMember] public bool Suggestions;
        [DataMember] public ProactiveOptions Proactive=new ProactiveOptions();
        [OnDeserializing] void Defaults(StreamingContext _)
        {Folders=new List<string>();Permissions=new List<DesktopPermission>();Words=new List<TechnicalWord>();Routines=new List<DesktopRoutine>();Maintenance=new List<string>();}
    }
    static class DesktopPaths
    {
        public static bool Local(string path)
        {
            try
            {
                if(String.IsNullOrEmpty(path) || !Regex.IsMatch(path,@"^[A-Za-z]:\\[^:]*$"))return false;
                var full=Path.GetFullPath(path);
                if(new DriveInfo(Path.GetPathRoot(full)).DriveType!=DriveType.Fixed)return false;
                var ancestors=new Stack<string>();
                for(string current=full;current!=null;current=Path.GetDirectoryName(current))ancestors.Push(current);
                foreach(var current in ancestors)
                    if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return false;
                return true;
            }
            catch(Exception){return false;}
        }
        public static bool Under(string root,string path)
        {return path.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
        public static bool Executable(string path)
        {
            return String.Equals(Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase) && Local(path) && File.Exists(path) &&
                !Regex.IsMatch(Path.GetFileNameWithoutExtension(path),@"^(?:cmd|powershell(?:_ise)?|pwsh|wscript|cscript|mshta|rundll32|regsvr32|msiexec|schtasks|reg(?:edit)?|diskpart|taskkill|bash|wsl|conhost|wt|windowsterminal|python\d*|perl|node|ruby|sh|winget|WindowsPackageManagerServer|cleanmgr|dfrgui|msconfig|mdsched|odbcad32)$|(?:unins|uninstall|setup|installer|bootstrap|update|repair|recovery)",RegexOptions.IgnoreCase);
        }
        public static IEnumerable<string> Files(string root,string pattern,int depth,CancellationToken cancel)
        {
            var pending=new Queue<KeyValuePair<string,int>>();
            if(Local(root) && Directory.Exists(root))pending.Enqueue(new KeyValuePair<string,int>(root,0));
            int directories=0;
            while(pending.Count>0 && directories++<512)
            {
                cancel.ThrowIfCancellationRequested();
                var item=pending.Dequeue();
                if(!Local(item.Key))continue;
                string[] files,folders;
                try
                {
                    files=Directory.GetFiles(item.Key,pattern);
                    folders=item.Value<depth?Directory.GetDirectories(item.Key):new string[0];
                }
                catch(IOException){continue;}
                catch(UnauthorizedAccessException){continue;}
                foreach(var file in files){cancel.ThrowIfCancellationRequested();yield return file;}
                foreach(var folder in folders)
                    if(!Regex.IsMatch(Path.GetFileName(folder),@"^(?:\.git|node_modules|\.venv|obj|packages)$",RegexOptions.IgnoreCase))
                        pending.Enqueue(new KeyValuePair<string,int>(folder,item.Value+1));
            }
        }
    }
    static class DesktopCatalog
    {
        [ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ActivationManager
        {
            [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string id,
                [MarshalAs(UnmanagedType.LPWStr)]string arguments,uint options,out uint processId);
        }
        static string DisplayName(string path)
        {
            try
            {
                string name=FileVersionInfo.GetVersionInfo(path).FileDescription;
                if(!String.IsNullOrWhiteSpace(name))return name.Trim();
            }
            catch(Exception){}
            return Path.GetFileNameWithoutExtension(path);
        }
        static void Release(object value){if(value!=null && Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
        static void Add(Dictionary<string,DesktopApp> found,string path,string name,string origin,string root=null)
        {
            if(!DesktopPaths.Executable(path) || String.IsNullOrWhiteSpace(name) ||
                Regex.IsMatch(name,@"redistributable|uninstall|desinstalar",RegexOptions.IgnoreCase))return;
            path=Path.GetFullPath(path);
            if(found.ContainsKey(path))return;
            found[path]=new DesktopApp{Id=path,Path=path,Name=name,Origin=origin,Root=root,Alias=Path.GetFileNameWithoutExtension(path)};
        }
        public static DesktopApp[] Scan(string[] roots,CancellationToken cancel)
        {
            var found=new Dictionary<string,DesktopApp>(StringComparer.OrdinalIgnoreCase);
            dynamic shell=null;
            try
            {
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                foreach(var special in new[]{Environment.SpecialFolder.Programs,Environment.SpecialFolder.CommonPrograms})
                foreach(var path in DesktopPaths.Files(Environment.GetFolderPath(special),"*.lnk",4,cancel).Take(2048))
                {
                    dynamic link=null;
                    try
                    {
                        if(!DesktopPaths.Local(path))continue;
                        link=shell.CreateShortcut(path);
                        if(String.IsNullOrWhiteSpace((string)link.Arguments))
                            Add(found,(string)link.TargetPath,Path.GetFileNameWithoutExtension(path),"Menú Inicio");
                    }
                    catch(Exception){}
                    finally{Release(link);}
                }
            }
            catch(Exception){cancel.ThrowIfCancellationRequested();}
            finally{Release(shell);}
            foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
            using(var registry=RegistryKey.OpenBaseKey(hive,view))
            {
                using(var keys=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                    if(keys!=null)foreach(var name in keys.GetSubKeyNames())
                    {
                        cancel.ThrowIfCancellationRequested();
                        using(var key=keys.OpenSubKey(name))
                        {
                            string path=key==null?null:(key.GetValue(null) as string);
                            if(!String.IsNullOrWhiteSpace(path) && DesktopPaths.Executable(path.Trim('"')))
                                Add(found,path.Trim('"'),DisplayName(path.Trim('"')),"App Paths");
                        }
                    }
                using(var keys=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    if(keys!=null)foreach(var name in keys.GetSubKeyNames())
                    {
                        cancel.ThrowIfCancellationRequested();
                        using(var key=keys.OpenSubKey(name))
                        {
                            if(key==null || Convert.ToString(key.GetValue("SystemComponent"))=="1")continue;
                            string label=key.GetValue("DisplayName") as string;
                            string icon=key.GetValue("DisplayIcon") as string;
                            if(String.IsNullOrWhiteSpace(label) || String.IsNullOrWhiteSpace(icon))continue;
                            var path=Regex.Replace(icon,@",\s*-?\d+$","").Trim('"',' ');
                            Add(found,path,label,"Registro de instalaciones");
                        }
                    }
            }
            dynamic explorer=null,folder=null,items=null;
            try
            {
                explorer=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                folder=explorer.NameSpace("shell:AppsFolder");
                if(folder!=null)
                {
                    items=folder.Items();
                    for(int i=0;i<(int)items.Count;i++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        dynamic item=items.Item(i);
                        try
                        {
                            string id=item.Path, name=item.Name;
                            if(ValidId(id) && !String.IsNullOrWhiteSpace(name) &&
                                !Regex.IsMatch(id,@"WindowsTerminal|DesktopAppInstaller",RegexOptions.IgnoreCase))
                                found[id]=new DesktopApp{Id=id,Name=name,Origin="Microsoft Store / MSIX"};
                        }
                        finally{Release(item);}
                    }
                }
            }
            catch(Exception){cancel.ThrowIfCancellationRequested();}
            finally{Release(items);Release(folder);Release(explorer);}
            foreach(string root in roots)
            foreach(string path in DesktopPaths.Files(root,"*.exe",4,cancel).Take(1024))
                if(DesktopPaths.Executable(path))Add(found,path,DisplayName(path),"Carpeta autorizada",root);
            return found.Values.OrderBy(x=>x.Name).ThenBy(x=>x.Id).ToArray();
        }
        public static bool ValidId(string value)
        {return value!=null && Regex.IsMatch(value,@"^[A-Za-z0-9.-]+_[A-Za-z0-9]+![A-Za-z0-9._-]+$");}
        public static bool IsPackagedAlias(DesktopApp packaged,DesktopApp classic)
        {
            if(!packaged.Packaged || classic.Packaged || classic.Portable || !ValidId(packaged.Id))return false;
            var match=Regex.Match(classic.Path,@"\\WindowsApps\\(?<name>[\w.-]+)_[\d.]+_[\w]+_[^\\]*?_(?<publisher>[\w]+)\\",RegexOptions.IgnoreCase);
            if(!match.Success)return false;
            var package=match.Groups["name"].Value;
            var family=package+"_"+match.Groups["publisher"].Value;
            return packaged.Id.StartsWith(family+"!",StringComparison.OrdinalIgnoreCase) &&
                package.EndsWith(Path.GetFileNameWithoutExtension(classic.Path),StringComparison.OrdinalIgnoreCase);
        }
        public static string Fingerprint(string path)
        {
            if(!DesktopPaths.Executable(path))return null;
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(stream));
        }
        public static string Normalize(string text)
        {
            var stripped=new string((text??"").ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD).Where(c=>Char.IsLetterOrDigit(c) || c==' ').ToArray());
            return Regex.Replace(stripped,@"\s+"," ").Trim();
        }
        public static DesktopApp[] Match(IEnumerable<DesktopApp> apps,string text)
        {
            string term=Normalize(text);
            if(term.Length<2)return new DesktopApp[0];
            var exact=apps.Where(x=>Normalize(x.Name)==term || Normalize(x.Alias)==term).ToArray();
            if(exact.Length>0)return exact;
            if(term=="visual studio")return apps.Where(x=>Normalize(x.Name).StartsWith(term+" ",StringComparison.Ordinal) &&
                !Normalize(x.Name).StartsWith("visual studio code",StringComparison.Ordinal)).Take(8).ToArray();
            var partial=apps.Where(x=>Normalize(x.Name).Contains(term)).Take(8).ToArray();
            if(partial.Length>0 || term.Length<4)return partial;
            return apps.Where(x=>OneEdit(term,Normalize(x.Name)) || OneEdit(term,Normalize(x.Alias))).Take(8).ToArray();
        }
        public static bool Exact(DesktopApp app,string name)
        {string text=Normalize(name);return Normalize(app.Name)==text || Normalize(app.Alias)==text;}
        public static bool CanAuthorizeByVoice(DesktopApp app,DesktopPermission permission)
        {return !app.Portable && permission==null && !String.IsNullOrWhiteSpace(app.Id);}
        public static bool CanOpenByExactVoice(DesktopPermission permission,SpeechResult speech,bool exactCommand)
        {return exactCommand && permission!=null && !permission.Excluded && permission.OpenByExactVoice &&
            speech!=null && speech.HasText && speech.Rejected==0 && !speech.Confidence.HasValue;}
        static bool OneEdit(string first,string second)
        {
            if(Math.Abs(first.Length-second.Length)>1)return false;
            if(first.Length>second.Length){var swap=first;first=second;second=swap;}
            int i=0,j=0,edits=0;
            while(i<first.Length && j<second.Length)
            {
                if(first[i]==second[j]){i++;j++;continue;}
                if(++edits>1)return false;
                if(first.Length==second.Length)i++;
                j++;
            }
            return edits+second.Length-j<=1;
        }
        public static string Launch(DesktopApp app,DesktopPermission permission)
        {
            if(permission==null || permission.Excluded)return "La aplicación requiere autorización en Aplicaciones.";
            if(app.Packaged)
            {
                if(!ValidId(app.Id))return "Identificador de aplicación inválido.";
                object manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
                try
                {
                    uint pid;
                    int status=((ActivationManager)manager).ActivateApplication(app.Id,null,0,out pid);
                    return status<0?"Windows no pudo activar "+app.Name+". Comprueba si sigue instalada.":
                        "Windows activó "+app.Name+".";
                }
                finally{Release(manager);}
            }
            if(!DesktopPaths.Executable(app.Path) || (app.Portable && (!DesktopPaths.Local(app.Root) || !DesktopPaths.Under(app.Root,app.Path))))
                return "La ubicación de la aplicación ya no es válida.";
            using(var binary=new FileStream(app.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var sha=SHA256.Create())
            {
                if(Convert.ToBase64String(sha.ComputeHash(binary))!=permission.Hash)
                    return "La aplicación cambió. Revísala y vuelve a autorizarla en Aplicaciones.";
                if(Running(app).Length>0)return app.Name+" ya tiene una ventana abierta.";
                using(var process=Process.Start(new ProcessStartInfo(app.Path){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(app.Path)}))
                    return process==null?"Windows no devolvió un proceso.":"Windows inició el proceso de "+app.Name+".";
            }
        }
        public static bool CanClose(string path,string own)
        {
            if(!DesktopPaths.Executable(path))return false;
            var baseName=Path.GetFileNameWithoutExtension(path);
            return !String.Equals(path,own,StringComparison.OrdinalIgnoreCase) &&
                !DesktopPaths.Under(Environment.GetFolderPath(Environment.SpecialFolder.Windows),path) &&
                !Regex.IsMatch(baseName,@"security|antivirus|defender|msmpeng|mssense|driver|endpoint|crowdstrike|sentinel|edr|service",RegexOptions.IgnoreCase);
        }
        public static int[] Running(DesktopApp app)
        {
            if(app.Path==null)return new int[0];
            var ids=new List<int>();
            foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app.Path)))
                using(process)try
                {
                    if(process.MainWindowHandle!=IntPtr.Zero && String.Equals(process.MainModule.FileName,app.Path,StringComparison.OrdinalIgnoreCase))
                        ids.Add(process.Id);
                }
                catch(Exception){}
            return ids.ToArray();
        }
    }
}

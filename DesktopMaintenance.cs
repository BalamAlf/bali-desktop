using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BaliDesktop
{
    sealed class TempPlan
    {
        public string Root;
        public DateTime Expires;
        public TempFile[] Files;
        public long Bytes { get { return Files.Sum(x=>x.Size); } }
    }
    sealed class TempFile
    {
        public string Path, Identity;
        public long Size;
    }
    sealed class TempResult
    {
        public int Deleted, Omitted, Errors;
        public long Bytes;
        public bool Changed;
        public override string ToString()
        {
            return Changed?"La lista cambió o venció el análisis. Vuelve a analizar antes de limpiar.":
                String.Format("Eliminé {0} archivos. Tamaño lógico estimado: {1}. Omitidos: {2}. Errores: {3}.",
                    Deleted,DesktopMetrics.Size(Bytes),Omitted,Errors);
        }
    }
    static class DesktopMaintenance
    {
        [StructLayout(LayoutKind.Sequential)] struct Info
        { public uint Attributes,CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IdHigh,IdLow; }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint mode,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle file,out Info info);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder name,uint length,uint flags);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetFileInformationByHandle(SafeFileHandle file,int kind,ref byte value,uint length);
        public static string UserRoot()
        {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Temp");}
        static SafeFileHandle Open(string path,uint access,uint share)
        {return CreateFile(path,access,share,IntPtr.Zero,3,0x00200000,IntPtr.Zero);}
        static TempFile Inspect(SafeFileHandle file,string root,string path,DateTime now)
        {
            Info info;
            if(file.IsInvalid || !GetFileInformationByHandle(file,out info) || info.Links!=1 ||
                (info.Attributes&((uint)FileAttributes.ReparsePoint|(uint)FileAttributes.Directory|(uint)FileAttributes.System|(uint)FileAttributes.ReadOnly))!=0)return null;
            var name=new StringBuilder(32768);
            var chars=GetFinalPathNameByHandle(file,name,(uint)name.Capacity,0);
            if(chars==0 || chars>=name.Capacity)return null;
            var absolute=name.ToString();
            if(!absolute.StartsWith(@"\\?\",StringComparison.Ordinal))return null;
            absolute=absolute.Substring(4);
            if(!DesktopPaths.Under(root,absolute) || !String.Equals(Path.GetDirectoryName(absolute),root,StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(absolute,path,StringComparison.OrdinalIgnoreCase))return null;
            long write=((long)info.WriteHigh<<32)|info.WriteLow;
            long accessed=((long)info.AccessHigh<<32)|info.AccessLow;
            long created=((long)info.CreationHigh<<32)|info.CreationLow;
            long cutoff=now.AddDays(-14).ToFileTimeUtc();
            if(write>cutoff || accessed>cutoff || created>cutoff)return null;
            long size=((long)info.SizeHigh<<32)|info.SizeLow;
            return new TempFile{Path=path,Size=size,Identity=String.Join("|",info.Volume,info.IdHigh,info.IdLow,created,write,size)};
        }
        public static TempPlan Analyze(string root,DateTime now,CancellationToken token)
        {
            var files=new List<TempFile>();
            if(DesktopPaths.Local(root))
            foreach(var path in DesktopPaths.Files(root,"*.tmp",0,token).Take(10000))
            {
                if(!Regex.IsMatch(Path.GetFileName(path),@"^(?:~df[0-9a-f]{4,32}|tmp[0-9a-f]{4,12})\.tmp$",RegexOptions.IgnoreCase))continue;
                if(!DesktopPaths.Local(path))continue;
                using(var file=Open(path,0x80000080,0))
                {
                    var entry=Inspect(file,root,path,now);
                    if(entry!=null)files.Add(entry);
                }
                if(files.Count>=2000)break;
            }
            return new TempPlan{Root=root,Expires=now.AddSeconds(60),Files=files.OrderBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).ToArray()};
        }
        public static TempResult Clean(TempPlan plan,DateTime now,CancellationToken token)
        {
            var result=new TempResult();
            if(plan==null || now>=plan.Expires || !DesktopPaths.Local(plan.Root)) {result.Changed=true;return result;}
            var fresh=Analyze(plan.Root,now,token);
            if(!fresh.Files.Select(x=>x.Path+"|"+x.Identity).SequenceEqual(plan.Files.Select(x=>x.Path+"|"+x.Identity)))
            { result.Changed=true;return result; }
            foreach(var row in plan.Files)
            {
                if(token.IsCancellationRequested){result.Omitted+=plan.Files.Length-result.Deleted-result.Omitted-result.Errors;break;}
                if(!DesktopPaths.Local(row.Path)){result.Omitted++;continue;}
                using(var file=Open(row.Path,0x80010080,0))
                {
                    var current=Inspect(file,plan.Root,row.Path,now);
                    if(current==null || current.Identity!=row.Identity){result.Omitted++;continue;}
                    byte delete=1;
                    if(!SetFileInformationByHandle(file,4,ref delete,1)){result.Errors++;continue;}
                    result.Deleted++;result.Bytes+=row.Size;
                }
            }
            return result;
        }
    }
    static class DesktopMetrics
    {
        [StructLayout(LayoutKind.Sequential)] struct Memory
        {public uint Length,Load;public ulong Total,Available,Page,PageFree,Virtual,VirtualFree,Extended;}
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref Memory result);
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        public static double AvailableRamFraction()
        {
            var memory=new Memory{Length=(uint)Marshal.SizeOf(typeof(Memory))};
            return GlobalMemoryStatusEx(ref memory) && memory.Total>0?memory.Available/(double)memory.Total:1;
        }
        public static string Size(long bytes){return bytes>=1073741824?String.Format("{0:0.0} gigabytes",bytes/1073741824d):String.Format("{0:0.00} megabytes",bytes/1048576d);}
        sealed class ProcessMeasure {public int Id;public string Name;public double Cpu;public long Bytes;}
        static ProcessMeasure[] Sample()
        {
            var values=new List<ProcessMeasure>();
            foreach(var p in Process.GetProcesses())using(p)try
            {values.Add(new ProcessMeasure{Id=p.Id,Name=p.ProcessName,Cpu=p.TotalProcessorTime.TotalSeconds,Bytes=p.WorkingSet64});}
            catch(Exception){}
            return values.ToArray();
        }
        public static string Read(string topic,CancellationToken cancel)
        {
            var memory=new Memory{Length=(uint)Marshal.SizeOf(typeof(Memory))};
            string ram=GlobalMemoryStatusEx(ref memory)?"RAM utilizada: "+Size((long)(memory.Total-memory.Available))+". Disponible: "+Size((long)memory.Available)+".":"No pude medir la RAM.";
            if(topic=="ram")return ram;
            if(topic=="disco")
                return String.Join(". ",DriveInfo.GetDrives().Where(x=>x.DriveType==DriveType.Fixed && x.IsReady)
                    .Select(x=>"Unidad "+x.Name[0]+": "+Size(x.AvailableFreeSpace)+" libres"));
            if(topic=="actividad")return "El equipo lleva encendido "+TimeSpan.FromMilliseconds(GetTickCount64()).TotalHours.ToString("0.0")+" horas.";
            if(topic=="batería")
            {
                var battery=System.Windows.Forms.SystemInformation.PowerStatus;
                return battery.BatteryLifePercent>=0 && battery.BatteryLifePercent<=1 &&
                    battery.BatteryChargeStatus!=System.Windows.Forms.BatteryChargeStatus.NoSystemBattery?
                    "Batería al "+battery.BatteryLifePercent.ToString("P0")+".":"Windows no informa una batería disponible.";
            }
            var first=Sample().ToDictionary(x=>x.Id);
            long i0,k0,u0,i1,k1,u1;
            bool before=GetSystemTimes(out i0,out k0,out u0);
            var wall=Stopwatch.StartNew();
            if(cancel.WaitHandle.WaitOne(450))cancel.ThrowIfCancellationRequested();
            bool after=GetSystemTimes(out i1,out k1,out u1);
            var latest=Sample();
            double elapsed=wall.Elapsed.TotalSeconds;
            long total=(k1-k0)+(u1-u0);
            var cpu=latest.Where(x=>first.ContainsKey(x.Id) && first[x.Id].Name==x.Name)
                .Select(x=>new{x.Name,Percent=Math.Max(0,(x.Cpu-first[x.Id].Cpu)/(elapsed*Environment.ProcessorCount))})
                .OrderByDescending(x=>x.Percent).Take(3).Select(x=>x.Name+" "+x.Percent.ToString("P0"));
            var topRam=latest.OrderByDescending(x=>x.Bytes).Take(3).Select(x=>x.Name+" "+Size(x.Bytes));
            if(topic=="procesos-ram")return "Mayor memoria residente: "+String.Join(", ",topRam)+".";
            return (before && after && total>0?"CPU total: "+(1-(i1-i0)/(double)total).ToString("P0")+". ":"CPU total sin dato. ")+
                "Mayor uso de CPU en la muestra: "+String.Join(", ",cpu)+"."+
                (topic=="lento"?" "+ram+" Mayor memoria residente: "+String.Join(", ",topRam)+". Una muestra no identifica por sí sola la causa.":"");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0) { OneClick(null); return; }
        string mode = args[0];
        switch (mode)
        {
            case "il": DumpIl(args[1], args[2], args.Length > 3 ? args[3] : null); break;
            case "fields": DumpFields(args[1], args[2]); break;
            case "patch": Patch(args[1], args[2]); break;
            case "patchall": PatchAll.Run(args[1], args[2]); break;
            case "stats": Stats(args[1]); break;
            case "findstr": FindStr(args[1], args[2]); break;
            case "sig": Sig(args[1], args[2], args.Length > 3 ? args[3] : null); break;
            case "types": Types(args[1], args[2]); break;
            case "findcall": FindCall(args[1], args[2]); break;
            default:
                // 不是已知子命令时，把参数当成目标路径，走一键模式
                OneClick(mode);
                break;
        }
    }

    // ---------- 一键补丁 ----------
    const string ExeName = "Plain Craft Launcher 2.exe";
    static readonly string[] KnownNames =
    {
        "Plain Craft Launcher 2.exe",
        "PCL2_CE_Release_x64.exe",
        "PCL2_CE_Release_ARM64.exe",
        "PCL2-CE.exe",
        "PCL2CE.exe",
    };

    static void OneClick(string arg)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("==============================================");
        Console.WriteLine("  PCL 一键解锁补丁（快照版 / 社区版）");
        Console.WriteLine("==============================================");

        string target = ResolveTarget(arg);
        if (target == null)
        {
            Console.WriteLine("[x] 没有找到启动器（Plain Craft Launcher 2.exe / PCL2_CE_*.exe）");
            Console.WriteLine("    请把本程序和启动器 exe 放在同一个文件夹，");
            Console.WriteLine("    或者把启动器 exe 拖到本程序 / 一键补丁.bat 上。");
            Pause();
            Environment.ExitCode = 1;
            return;
        }
        Console.WriteLine("[1/4] 目标: " + target);

        var running = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target));
        if (running.Length > 0)
        {
            Console.WriteLine("[x] 启动器正在运行，请先完全退出再试。");
            Pause();
            Environment.ExitCode = 1;
            return;
        }

        // 判断是哪种版本：2.99 快照版（可打补丁）/ 社区版（单文件打包，本身无限制）
        ModuleDefMD probe;
        try
        {
            probe = ModuleDefMD.Load(target);
        }
        catch (BadImageFormatException)
        {
            Console.WriteLine("[✓] 这是单文件打包的版本（社区版/自包含版），本身没有 MC 下载限制。");
            Console.WriteLine("    无需打补丁，直接启动即可。");
            Pause();
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[x] 无法读取该文件: " + ex.Message);
            Pause();
            Environment.ExitCode = 1;
            return;
        }

        bool restricted;
        using (probe)
        {
            restricted = IsRestricted(probe);
        }
        if (!restricted)
        {
            Console.WriteLine("[✓] 没有发现 2.99 快照版的下载限制（社区版 / 旧版 / 已打过补丁）。");
            Console.WriteLine("    无需处理，直接启动即可。");
            Pause();
            return;
        }

        string root = Path.GetDirectoryName(target);
        string backupDir = Path.Combine(root, "PCL", "PatchBackup");
        Directory.CreateDirectory(backupDir);
        string backup = Path.Combine(backupDir, Path.GetFileName(target) + ".original." + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        File.Copy(target, backup, true);
        Console.WriteLine("[2/4] 已备份: " + backup);

        string tmp = Path.Combine(root, ".pcl-unlock.tmp.exe");
        Console.WriteLine("[3/4] 正在打补丁...");
        try
        {
            PatchAll.Run(target, tmp);
        }
        catch (Exception ex)
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            Console.WriteLine("[x] 补丁失败: " + ex.Message);
            Console.WriteLine("    可能原因：不是 2.99.0.0 (414) 原版，或者已经打过补丁。");
            Console.WriteLine("    已备份的文件可原样覆盖回去。");
            Pause();
            Environment.ExitCode = 1;
            return;
        }

        File.Delete(target);
        File.Move(tmp, target);
        Console.WriteLine("[4/4] 完成！启动器已解锁（原版备份见 PCL\\PatchBackup）。");
        Pause();
    }

    static bool IsRestricted(ModuleDefMD mod)
    {
        // 2.99 快照版的特征：PageDownloadInstall.LoaderInit 里带一个名为「关闭下载」的 LoaderTask
        var t = mod.GetTypes().FirstOrDefault(x => x.FullName == "PCL.PageDownloadInstall");
        if (t == null) return false;
        var m = t.Methods.FirstOrDefault(x => x.Name == "LoaderInit" && x.HasBody);
        if (m == null) return false;
        return m.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldstr && (i.Operand as string) == "关闭下载");
    }

    static string ResolveTarget(string arg)
    {
        if (!string.IsNullOrWhiteSpace(arg))
        {
            string p = arg.Trim('"');
            if (Directory.Exists(p))
            {
                foreach (var n in KnownNames)
                {
                    string c = Path.Combine(p, n);
                    if (File.Exists(c)) return Path.GetFullPath(c);
                }
                try
                {
                    var g = Directory.GetFiles(p, "*.exe")
                        .Where(f => Path.GetFileName(f).StartsWith("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase)
                                 || Path.GetFileName(f).StartsWith("PCL2", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (g.Count == 1) return Path.GetFullPath(g[0]);
                }
                catch { }
            }
            if (File.Exists(p)) return Path.GetFullPath(p);
        }

        string dir = AppContext.BaseDirectory.TrimEnd('\\');
        foreach (var d in new[] { dir, Path.GetDirectoryName(dir) ?? dir, Environment.CurrentDirectory })
        {
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
            foreach (var n in KnownNames)
            {
                string c = Path.Combine(d, n);
                if (File.Exists(c)) return Path.GetFullPath(c);
            }
            try
            {
                var hit = Directory.GetFiles(d, "PCL2_CE*.exe").FirstOrDefault()
                       ?? Directory.GetFiles(d, "Plain Craft Launcher*.exe").FirstOrDefault();
                if (hit != null) return Path.GetFullPath(hit);
            }
            catch { }
        }
        return null;
    }

    static void Pause()
    {
        if (Console.IsInputRedirected) return;
        Console.WriteLine();
        Console.Write("按回车键退出...");
        try { Console.ReadLine(); } catch { }
    }

    static void FindCall(string path, string pattern)
    {
        var mod = ModuleDefMD.Load(path);
        var rx = new Regex(pattern);
        var seen = new HashSet<string>();
        foreach (var t in AllTypes(mod))
        foreach (var m in t.Methods)
        {
            if (!m.HasBody) continue;
            foreach (var i in m.Body.Instructions)
            {
                if (i.Operand is IMethod im && rx.IsMatch(im.FullName) && seen.Add(im.FullName))
                    Console.WriteLine(im.FullName);
            }
        }
    }

    static void Types(string path, string filter)
    {
        var mod = ModuleDefMD.Load(path);
        foreach (var t in AllTypes(mod).Where(t => Regex.IsMatch(t.FullName, filter)))
            Console.WriteLine(t.FullName);
    }

    static void Sig(string path, string typeFilter, string nameFilter)
    {
        var mod = ModuleDefMD.Load(path);
        foreach (var t in AllTypes(mod).Where(t => Regex.IsMatch(t.FullName, typeFilter)))
        foreach (var m in t.Methods)
        {
            if (nameFilter != null && !Regex.IsMatch(m.Name, nameFilter)) continue;
            Console.WriteLine($"{t.FullName}::{m.FullName}  PARAMS=({string.Join(", ", m.Parameters.Select(p => p.Name))})");
        }
    }

    static void FindStr(string path, string needle)
    {
        var mod = ModuleDefMD.Load(path);
        foreach (var t in AllTypes(mod))
        foreach (var m in t.Methods)
        {
            if (!m.HasBody) continue;
            if (m.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldstr && i.Operand is string s && s.Contains(needle)))
            {
                Console.WriteLine($"### {t.FullName}::{m.Name}");
                DumpMethod(m);
            }
        }
    }

    // ---------- shared ----------
    static IEnumerable<TypeDef> AllTypes(ModuleDef mod)
    {
        foreach (var t in mod.GetTypes())
        {
            yield return t;
            foreach (var n in t.NestedTypes) yield return n;
        }
    }

    static void DumpIl(string asmPath, string typeFilter, string methodFilter)
    {
        var mod = ModuleDefMD.Load(asmPath);
        Console.WriteLine($"Module: {mod.Name} types={mod.Types.Count}");
        var types = AllTypes(mod).Where(t => Regex.IsMatch(t.FullName, typeFilter)).ToList();
        Console.WriteLine($"Matched types: {types.Count}");
        foreach (var t in types)
        {
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                if (methodFilter != null && !Regex.IsMatch(m.Name, methodFilter)) continue;
                if (methodFilter == null && !m.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldc_I4_1)) continue;
                DumpMethod(m);
            }
        }
    }

    static void DumpMethod(MethodDef m)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {m.DeclaringType.FullName}::{m.FullName} bodyIL={m.Body.Instructions.Count}");
        for (int vi = 0; vi < m.Body.Variables.Count; vi++)
            Console.WriteLine($"   LOCAL V_{vi}: {m.Body.Variables[vi].Type.FullName}");
        foreach (var instr in m.Body.Instructions)
        {
            string operand = "";
            if (instr.Operand is string s) operand = "\"" + (s.Length > 70 ? s.Substring(0, 70) + "..." : s) + "\"";
            else if (instr.Operand is IMethod md) operand = md.FullName;
            else if (instr.Operand is IField fd) operand = fd.FullName;
            else if (instr.Operand != null) operand = instr.Operand.ToString();
            Console.WriteLine($"  IL_{instr.Offset:X4}: {instr.OpCode.Name,-10} {operand}");
        }
    }

    static void DumpFields(string asmPath, string typeFilter)
    {
        var mod = ModuleDefMD.Load(asmPath);
        foreach (var t in AllTypes(mod).Where(t => Regex.IsMatch(t.FullName, typeFilter)))
        {
            Console.WriteLine($"=== {t.FullName}");
            foreach (var f in t.Fields)
                Console.WriteLine($"  {f.Attributes,-40} {f.FieldType.FullName} {f.Name}");
            foreach (var p in t.Properties)
                Console.WriteLine($"  prop {p.PropertySig?.RetType.FullName} {p.Name} get={p.GetMethod?.Name} set={p.SetMethod?.Name}");
            foreach (var m in t.Methods.Where(x => x.IsGetter || x.Name.StartsWith("get_")))
                Console.WriteLine($"  getter {m.Name} -> {m.ReturnType.FullName} params={m.Parameters.Count}");
        }
    }

    static void Stats(string path)
    {
        var mod = ModuleDefMD.Load(path);
        Console.WriteLine($"FILE {path}");
        Console.WriteLine($"  asm={mod.Assembly?.FullName} entry={mod.EntryPoint}");
        Console.WriteLine($"  types={mod.GetTypes().Count()} methods={mod.GetTypes().Sum(t => t.Methods.Count)}");
        foreach (var r in mod.Resources) Console.WriteLine($"  res: {r.Name} ({r.ResourceType})");
    }

    // ---------- patch ----------
    static void Patch(string inPath, string outPath)
    {
        var mod = ModuleDefMD.Load(inPath);
        var type = AllTypes(mod).FirstOrDefault(t => t.FullName == "PCL.PageDownloadInstall")
                   ?? throw new Exception("type PCL.PageDownloadInstall not found");
        var method = type.Methods.FirstOrDefault(m => m.Name == "LoaderInit" && m.HasBody)
                     ?? throw new Exception("LoaderInit not found");
        var md = AllTypes(mod).FirstOrDefault(t => t.FullName == "PCL.ModDownload")
                 ?? throw new Exception("PCL.ModDownload not found");
        var field = md.Fields.FirstOrDefault(f => f.Name == "DlClientListLoader")
                    ?? throw new Exception("DlClientListLoader field not found");
        Console.WriteLine($"Field: {field.FieldType.FullName} {field.FullName}");

        var instrs = method.Body.Instructions;
        int start = -1, end = -1;
        for (int i = 0; i < instrs.Count; i++)
        {
            if (start < 0 && instrs[i].OpCode.Code == Code.Ldstr && (instrs[i].Operand as string) == "关闭下载")
                start = i;
            if (start >= 0 && instrs[i].OpCode.Code == Code.Newobj &&
                instrs[i].Operand is IMethod nm && nm.DeclaringType?.Name?.StartsWith("LoaderTask`2") == true)
            {
                end = i;
                break;
            }
        }
        if (start < 0 || end < 0 || end < start) throw new Exception($"pattern not found start={start} end={end}");
        Console.WriteLine($"Replacing IL[{start}..{end}] ({end - start + 1} instructions)");
        for (int k = start; k <= end; k++)
            Console.WriteLine($"   - {instrs[k].OpCode.Name} {instrs[k].Operand}");

        var ldsfld = Instruction.Create(OpCodes.Ldsfld, field);
        for (int k = end; k >= start; k--) instrs.RemoveAt(k);
        instrs.Insert(start, ldsfld);
        method.Body.OptimizeMacros();
        method.Body.UpdateInstructionOffsets();

        mod.Write(outPath);
        Console.WriteLine($"Written: {outPath}");

        // verify
        var mod2 = ModuleDefMD.Load(outPath);
        var t2 = AllTypes(mod2).First(t => t.FullName == "PCL.PageDownloadInstall");
        var m2 = t2.Methods.First(m => m.Name == "LoaderInit");
        Console.WriteLine("--- patched IL ---");
        DumpMethod(m2);
    }
}







using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

static class PatchAll
{
    public static void Run(string inPath, string outPath)
    {
        var mod = ModuleDefMD.Load(inPath);
        var types = mod.GetTypes().ToList();

        PatchLoaderInit(types);
        PatchModifyCard(types);
        PatchResourceInstall(types);
        PatchModpackInstall(types);

        mod.Write(outPath);
        Console.WriteLine("written " + outPath);
    }

    // ---------- A: 下载页的 LoaderInit ----------
    static void PatchLoaderInit(List<TypeDef> types)
    {
        var t = types.First(x => x.FullName == "PCL.PageDownloadInstall");
        var m = t.Methods.First(x => x.Name == "LoaderInit" && x.HasBody);
        var md = types.First(x => x.FullName == "PCL.ModDownload");
        var field = md.Fields.First(f => f.Name == "DlClientListLoader");
        var ins = m.Body.Instructions;

        int start = -1, end = -1;
        for (int i = 0; i < ins.Count; i++)
        {
            if (start < 0 && ins[i].OpCode.Code == Code.Ldstr && (ins[i].Operand as string) == "关闭下载")
                start = i;
            if (start >= 0 && ins[i].OpCode.Code == Code.Newobj && ins[i].Operand is IMethod nm &&
                nm.DeclaringType?.Name?.StartsWith("LoaderTask`2") == true)
            {
                end = i;
                break;
            }
        }
        if (start < 0 || end < 0) throw new Exception("[A] pattern not found (already patched?)");

        for (int k = end; k >= start; k--) ins.RemoveAt(k);
        ins.Insert(start, Instruction.Create(OpCodes.Ldsfld, field));
        Console.WriteLine("[A] LoaderInit -> DlClientListLoader");
    }

    // ---------- C: 修改版本页的 Minecraft 卡片 ----------
    static void PatchModifyCard(List<TypeDef> types)
    {
        var t = types.First(x => x.FullName == "PCL.PageDownloadModify");
        var m = t.Methods.First(x => x.Name == "LoadInstanceFinished");
        var ins = m.Body.Instructions;
        for (int i = 0; i < ins.Count - 1; i++)
        {
            if (ins[i].OpCode.Code == Code.Callvirt && ins[i].Operand is IMethod mm && mm.Name == "get_CardMinecraft"
                && ins[i + 1].OpCode.Code == Code.Ldc_I4_2)
            {
                ins[i + 1].OpCode = OpCodes.Ldc_I4_0;
                Console.WriteLine("[C] CardMinecraft.Visibility -> Visible");
                return;
            }
        }
        throw new Exception("[C] pattern not found");
    }

    // ---------- D: 资源详情页整合包的「安装」入口 ----------
    static void PatchResourceInstall(List<TypeDef> types)
    {
        var t = types.First(x => x.FullName == "PCL.PageDownloadResourceDetail");
        var m = t.Methods.First(x => x.Name == "InitializeStackResourceVersions");
        var ins = m.Body.Instructions;
        int idx = -1;
        for (int i = 0; i < ins.Count; i++)
        {
            if (ins[i].OpCode.Code == Code.Callvirt && ins[i].Operand is IMethod mm && mm.Name == "ToListItem" && mm.DeclaringType?.Name == "ResourceVersion")
            {
                idx = i;
                break;
            }
        }
        if (idx < 5) throw new Exception("[D] ToListItem not found");

        var callToList = ins[idx];
        var saveHandlerLdftn = ins[idx - 4];
        int start = idx - 5;
        if (ins[start].OpCode.Code != Code.Ldarg_0 || ins[start + 1] != saveHandlerLdftn)
            throw new Exception("[D] unexpected prologue");

        var hasDupLocal = ResolveLocal(m.Body, ins[idx - 1]);
        var ctorHandler = (IMethod)ins[idx - 3].Operand;
        var targetField = t.Fields.First(f => f.Name == "TargetResourceType");
        var installClick = t.Methods.First(x => x.Name == "Install_Click");
        var saveClick = t.Methods.First(x => x.Name == "Save_Click");

        var lElse = Instruction.Create(OpCodes.Nop);
        var lCall = callToList;

        var code = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, targetField),
            Instruction.Create(OpCodes.Ldc_I4_2),
            Instruction.Create(OpCodes.Bne_Un, lElse),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldftn, installClick),
            Instruction.Create(OpCodes.Newobj, ctorHandler),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldftn, saveClick),
            Instruction.Create(OpCodes.Newobj, ctorHandler),
            Instruction.Create(OpCodes.Ldloc, hasDupLocal),
            Instruction.Create(OpCodes.Br, lCall),

            lElse
        };

        for (int i = 0; i < code.Count; i++) ins.Insert(start + i, code[i]);
        Console.WriteLine("[D] install button branch restored");
    }

    // ---------- B: 整合包安装主流程 ----------
    static void PatchModpackInstall(List<TypeDef> types)
    {
        var t = types.First(x => x.FullName == "PCL.ModModpack");
        var caller = t.Methods.First(x => x.Name == "ModpackInstall" && x.Parameters.Count == 3);
        var ins = caller.Body.Instructions;

        int idx = -1;
        for (int i = 0; i < ins.Count; i++)
        {
            if (ins[i].OpCode.Code == Code.Ldstr && (ins[i].Operand as string)?.Contains("不再提供 MC 下载功能") == true)
            {
                idx = i;
                break;
            }
        }
        if (idx < 0) { Console.WriteLine("[B] throw block not found (already patched?)"); return; }
        if (ins[idx + 1].OpCode.Code != Code.Newobj || ins[idx + 2].OpCode.Code != Code.Throw)
            throw new Exception("[B] unexpected throw block");

        // 依赖：OpenZip、ZipArchive.Dispose 的现有引用
        IMethod openZip = null, dispose = null;
        foreach (var i in ins)
        {
            if (i.Operand is IMethod mm)
            {
                if (mm.Name == "OpenZip") openZip = mm;
                if (mm.Name == "Dispose" && mm.DeclaringType?.Name == "ZipArchive") dispose = mm;
            }
        }
        if (openZip == null) throw new Exception("[B] OpenZip ref missing");
        if (dispose == null) throw new Exception("[B] ZipArchive.Dispose ref missing");

        var comboType = caller.ReturnType;
        var zipType = caller.Body.Variables[1].Type;
        var ctType = caller.Body.Variables[5].Type;

        var helper = new MethodDefUser(
            "InstallPackByType",
            MethodSig.CreateStatic(comboType,
                mod0(caller).CorLibTypes.String, mod0(caller).CorLibTypes.String,
                mod0(caller).CorLibTypes.String, mod0(caller).CorLibTypes.String,
                mod0(caller).CorLibTypes.Int32),
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig);
        helper.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
        for (int pi = 0; pi < 5; pi++) helper.ParamDefs.Add(new ParamDefUser("p" + pi, (ushort)(pi + 1)));
        helper.Body = new CilBody();

        var body = helper.Body;
        var vA = new Local(zipType);
        var vR = new Local(comboType);
        var vCt = new Local(ctType);
        body.Variables.Add(vA);
        body.Variables.Add(vR);
        body.Variables.Add(vCt);

        var curse = t.Methods.First(x => x.Name == "InstallPackCurseForge");
        var hmcl = t.Methods.First(x => x.Name == "InstallPackHMCL");
        var mmc = t.Methods.First(x => x.Name == "InstallPackMMC");
        var mcbbs = t.Methods.First(x => x.Name == "InstallPackMCBBS");
        var modrinth = t.Methods.First(x => x.Name == "InstallPackModrinth");
        var pack = t.Methods.First(x => x.Name == "InstallPackLauncherPack");
        var compress = t.Methods.First(x => x.Name == "InstallPackCompress");

        var end = Instruction.Create(OpCodes.Nop);
        var li = new List<Instruction>
        {
            // Archive = OpenZip(File, default)
            Instruction.Create(OpCodes.Ldarg, helper.Parameters[0]),
            Instruction.Create(OpCodes.Ldloca, vCt),
            Instruction.Create(OpCodes.Initobj, ctType.ToTypeDefOrRef()),
            Instruction.Create(OpCodes.Ldloc, vCt),
            Instruction.Create(OpCodes.Call, openZip),
            Instruction.Create(OpCodes.Stloc, vA),
        };

        void Case(int value, MethodDef target, int argCount)
        {
            var next = Instruction.Create(OpCodes.Nop);
            li.Add(Instruction.Create(OpCodes.Ldarg, helper.Parameters[4]));
            li.Add(Instruction.Create(OpCodes.Ldc_I4, value));
            li.Add(Instruction.Create(OpCodes.Bne_Un, next));
            li.Add(Instruction.Create(OpCodes.Ldarg, helper.Parameters[0]));
            li.Add(Instruction.Create(OpCodes.Ldloc, vA));
            li.Add(Instruction.Create(OpCodes.Ldarg, helper.Parameters[1]));
            if (argCount >= 4) li.Add(Instruction.Create(OpCodes.Ldarg, helper.Parameters[2]));
            if (argCount >= 5) li.Add(Instruction.Create(OpCodes.Ldarg, helper.Parameters[3]));
            li.Add(Instruction.Create(OpCodes.Call, target));
            li.Add(Instruction.Create(OpCodes.Stloc, vR));
            li.Add(Instruction.Create(OpCodes.Br, end));
            li.Add(next);
        }

        Case(0, curse, 5);
        Case(1, hmcl, 3);
        Case(2, mmc, 3);
        Case(3, mcbbs, 4);
        Case(4, modrinth, 5);
        Case(9, pack, 3);
        // else
        li.Add(Instruction.Create(OpCodes.Ldarg, helper.Parameters[0]));
        li.Add(Instruction.Create(OpCodes.Ldloc, vA));
        li.Add(Instruction.Create(OpCodes.Call, compress));
        li.Add(Instruction.Create(OpCodes.Stloc, vR));

        li.Add(end);
        li.Add(Instruction.Create(OpCodes.Ldloc, vA));
        li.Add(Instruction.Create(OpCodes.Callvirt, dispose));
        li.Add(Instruction.Create(OpCodes.Ldloc, vR));
        li.Add(Instruction.Create(OpCodes.Ret));

        foreach (var i in li) body.Instructions.Add(i);
        body.UpdateInstructionOffsets();
        t.Methods.Add(helper);

        // 原地改写 throw 块（保留原指令对象，避免破坏分支/异常处理边界）
        ins[idx].OpCode = OpCodes.Ldarg_0;
        ins[idx].Operand = null;
        ins[idx + 1].OpCode = OpCodes.Ldloc;
        ins[idx + 1].Operand = caller.Body.Variables[2];
        ins[idx + 2].OpCode = OpCodes.Ldarg_1;
        ins[idx + 2].Operand = null;

        var rest = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_2),
            Instruction.Create(OpCodes.Ldloc, caller.Body.Variables[4]),
            Instruction.Create(OpCodes.Call, helper),
            Instruction.Create(OpCodes.Stloc_0),
            Instruction.Create(OpCodes.Ldloc_0),
            Instruction.Create(OpCodes.Ret),
        };
        for (int i = 0; i < rest.Count; i++) ins.Insert(idx + 3 + i, rest[i]);
        Console.WriteLine("[B] modpack dispatch restored (helper added)");
    }

    static ModuleDef mod0(MethodDef m) => m.Module;

    static Local ResolveLocal(CilBody body, Instruction instr)
    {
        switch (instr.OpCode.Code)
        {
            case Code.Ldloc_0: return body.Variables[0];
            case Code.Ldloc_1: return body.Variables[1];
            case Code.Ldloc_2: return body.Variables[2];
            case Code.Ldloc_3: return body.Variables[3];
            case Code.Ldloc:
            case Code.Ldloc_S:
                if (instr.Operand is Local l) return l;
                if (instr.Operand is int ix) return body.Variables[ix - 1];
                break;
        }
        throw new Exception("cannot resolve local: " + instr.OpCode.Name);
    }
}



# 逆向分析笔记

目标：`Plain Craft Launcher 2.exe`，2.99.0.0（快照版 414），VB.NET / .NET Framework 4.8，
665 个类型、6339 个方法，无混淆、无强名称。

## 1. 限制是怎么写的

源码 `Modules/ModSecret.vb`：

```vb
Public Const RestrictMcDownload As Boolean = True
```

引用点（源码 grep `RestrictMcDownload`）：

```
ModSecret.vb:18                       Public Const RestrictMcDownload As Boolean = True
PageDownloadInstall.xaml.vb:17        If RestrictMcDownload Then ... Throw ...
ModModpack.vb:11                      If RestrictMcDownload Then Hint(...) : Return
ModModpack.vb:88                      If RestrictMcDownload Then Throw New Exception(...)
PageDownloadModify.xaml.vb:315        CardMinecraft.Visibility = If(RestrictMcDownload, Collapsed, Visible)
PageDownloadCompDetail.xaml.vb:275    If Not RestrictMcDownload AndAlso TargetResourceType = ModPack Then
FormMain.xaml.vb:1079                 If RestrictMcDownload AndAlso ex.Message = "..." Then
```

Roslyn 把常量折叠掉之后，未命中的分支整段消失（例如 `PageDownloadInstall::LoaderInit`
只剩 30 条 IL，且只有「构造抛异常的 LoaderTask」这一条路）。

## 2. 各站点的处理

### 2.1 PageDownloadInstall::LoaderInit

原始 IL（受限版）末尾：

```
IL_001A: ldstr   "关闭下载"
IL_001F: ldsfld  $IR3-3          ; 闭包缓存
...
IL_0043: ldnull
IL_0044: ldc.i4.2
IL_0045: newobj  LoaderTask`2<...>::.ctor
...
IL_0058: call    MyPageRight::PageLoaderInit<String, DlClientListResult>(...)
```

补丁：把 `IL_001A..IL_0045` 整段替换成一条

```
ldsfld PCL.ModDownload::DlClientListLoader
```

即源码里非限制分支的写法：

```vb
PageLoaderInit(LoadMinecraft, PanLoad, PanAllBack, Nothing, DlClientListLoader, AddressOf LoadMinecraft_OnFinish)
```

### 2.2 ModModpack::ModpackInstall(File, InstanceName, Logo)

原始 IL：Try/Catch 跑完归档解析后，`Select Case PackType` **没有生成**，
`leave` 全部跳到同一个 `ldstr + newobj Exception + throw`。

补丁保留这三条指令对象（它们是 `leave` 目标和异常处理边界，删了 dnlib 会拒绝写回），
原地改成：

```
ldarg.0                 ; File
ldloc   V_2             ; ArchiveBaseFolder
ldarg.1                 ; InstanceName
ldarg.2                 ; Logo
ldloc   V_4             ; PackType
call    InstallPackByType
stloc.0                 ; 返回值局部变量
ldloc.0
ret
```

`InstallPackByType` 是新增的静态方法（`InstallPackByType(String, String, String, String, Int32)`），
内部重新 `OpenZip` 一次并按 PackType 分发，最后 Dispose——因为原方法里 `Using` 的 finally
在 `leave` 到 throw 点之前就已经执行，此时 Archive 已释放。

### 2.3 PageDownloadModify::LoadInstanceFinished

`callvirt get_CardMinecraft()` 后面紧跟的 `ldc.i4.2`（Collapsed）改成 `ldc.i4.0`（Visible）。
同方法里 BtnBack / PanMinecraft 也是 `ldc.i4.2`，所以必须按「前一条是 get_CardMinecraft」定位，
不能按值批量替换。

### 2.4 PageDownloadResourceDetail::InitializeStackResourceVersions

原始 IL（只剩 else 分支）：

```
IL_0220: ldloc.s V_7            ; Version
IL_0222: ldarg.0
IL_0223: ldftn   Save_Click
IL_0229: newobj  MyListItem/ClickEventHandler
IL_022E: ldnull                 ; SaveAsButtonHandler = Nothing
IL_022F: ldloc.1                ; HasDuplicates
IL_0230: callvirt ResourceVersion::ToListItem(ClickEventHandler, ClickEventHandler, Boolean)
```

补丁在 IL_0222 前插入条件分支：

```
ldarg.0
ldfld   PCLCS.ResourceTypes PCL.PageDownloadResourceDetail::TargetResourceType
ldc.i4.2                        ; ResourceTypes.ModPack
bne.un  else
  ; then：Install_Click + Save_Click
  ...
  br call
else:
  ; 原样：Save_Click + null
call:
```

`ResourceTypes` 是 `PCLCS` 里的 flags 枚举：Mod=1, ModPack=2, ResourcePack=4, Shader=8, DataPack=16, Plugin=32。

注意：`ldloc.1` 是宏指令（无 operand），取局部变量要做 `Ldloc_0/1/2/3` → `Variables[n]` 的映射。

## 3. 踩过的坑

1. **不能删除作为分支目标 / 异常处理边界的指令**，否则 dnlib 写回时报
   `Found some other method's instruction or a removed instruction`。改为原地改写 opcode。
2. **宏指令没有 operand**：`ldloc.1`、`ldarg.0`、`ldc.i4.2` 都是隐含操作数，别直接 `(Local)instr.Operand`。
3. **新增方法要补 ParamDefs**，否则 `instruction.Create(OpCodes.Ldarg, parameter)` 拿不到 Parameter。
4. 方法体内的 `Using` finally 会先于 `leave` 目标执行，补丁点拿不到原来的 `ZipArchive`。

## 4. 版本适配

补丁按方法体模式匹配（找 `ldstr "关闭下载"`、`ldstr "...不再提供 MC 下载功能..."`、
`get_CardMinecraft` 后的 `ldc.i4.2`、`ToListItem` 调用点），换版本时如果方法体结构变了会
抛 `pattern not found`，需要重新对照反汇编调整。

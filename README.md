# pcl-unlock

把 **PCL 2.99.0.0（快照版 414）** 里被编译期删掉的「MC 下载 / 整合包安装」相关代码，用 dnlib 重新接回去的补丁工具。

> 仅供个人学习与自用。修改后的启动器请勿二次分发；PCL 的版权归其作者所有。

## 背景

PCL 2.99.0.0 在源码里用编译期常量关闭了 MC 下载：

```vb
' Modules/ModSecret.vb
Public Const RestrictMcDownload As Boolean = True
```

因为它是 `Const`，**会内联进每一个引用点**。编译后受限分支只剩「报错」那一条，另一条分支的代码被编译器整段删除——所以改配置没用，只能改 IL。

## 恢复的 4 处

| # | 方法 | 改动 |
| --- | --- | --- |
| 1 | `PCL.PageDownloadInstall::LoaderInit` | 「抛异常的空任务」→ 真实加载器 `PCL.ModDownload::DlClientListLoader`（下载 → 原版游戏） |
| 2 | `PCL.ModModpack::ModpackInstall(String, String, String)` | 无条件 `throw` → 调用新增的 `InstallPackByType`，按 PackType 分发 CurseForge / HMCL / MMC / MCBBS / Modrinth / 带启动器压缩包 / 普通压缩包 |
| 3 | `PCL.PageDownloadModify::LoadInstanceFinished` | `CardMinecraft.Visibility` 由 `Collapsed`(2) 改回 `Visible`(0) |
| 4 | `PCL.PageDownloadResourceDetail::InitializeStackResourceVersions` | 重建 `TargetResourceType = ModPack` 分支：整合包版本列表恢复「安装」入口 |

外加新增方法：

```
PCL.ModModpack::InstallPackByType(String file, String baseFolder, String instanceName, String logo, Int32 packType)
    A = OpenZip(file)
    switch packType: 0→InstallPackCurseForge 1→InstallPackHMCL 2→InstallPackMMC
                     3→InstallPackMCBBS 4→InstallPackModrinth 9→InstallPackLauncherPack
                     else→InstallPackCompress
    A.Dispose(); return
```

`PCL.FormMain::FileDrag` 里那条异常提示判断因为不再命中，无需改动；无参的 `ModpackInstall()` 本来就没有调用者。

## 一键使用（推荐）

1. 从 [Release](https://github.com/ZhaoJun233/pcl-unlock/releases/latest) 下载 **`一键补丁.bat`**（2 KB）。
   `PclPatch.exe` 如果不在旁边，bat 会自动从 Release 下载。
2. 关掉 PCL，把 `一键补丁.bat` 放到 `Plain Craft Launcher 2.exe` 同目录，双击。
   （也可以直接把启动器 exe 拖到 bat 上。）
3. 看到 `[4/4] 完成！` 就好了，原版自动备份在 `PCL\PatchBackup\`。

```
[1/4] 目标: D:\Games\PCL\Plain Craft Launcher 2.exe
[2/4] 已备份: D:\Games\PCL\PCL\PatchBackup\Plain Craft Launcher 2.exe.original.20261020_171408
[3/4] 正在打补丁...
[A] LoaderInit -> DlClientListLoader
[C] CardMinecraft.Visibility -> Visible
[D] install button branch restored
[B] modpack dispatch restored (helper added)
[4/4] 完成！启动器已解锁（原版备份见 PCL\PatchBackup）。
```

回退：把 `PCL\PatchBackup\` 里的 `.original.*` 改名回 `Plain Craft Launcher 2.exe` 覆盖即可。

## 命令行用法

**直接下 Release 里的 `PclPatch.exe`**（self-contained 单文件，win-x64，不需要装 .NET）：

```powershell
PclPatch.exe patchall "Plain Craft Launcher 2.exe" "Plain Craft Launcher 2.patched.exe"
```

从源码构建：

```powershell
dotnet build src -c Release
src\bin\Release\net10.0\PclPatch.exe patchall "Plain Craft Launcher 2.exe" "out.exe"
```

输出应包含 4 行：

```
[A] LoaderInit -> DlClientListLoader
[C] CardMinecraft.Visibility -> Visible
[D] install button branch restored
[B] modpack dispatch restored (helper added)
```

- 输入必须是 **2.99.0.0 (414)** 的原版 exe，其它版本方法体布局不同，补丁会直接报错（pattern not found）。
- 输出文件改名为 `Plain Craft Launcher 2.exe` 覆盖回去即可；**覆盖前务必备份原版**。
- 补丁只做 IL 层面的重接线，不改动任何资源，也不联网；对同一份原版 exe 的输出是确定性的（可复现字节级一致的产物）。

其它模式（调试用）：

```
PclPatch.exe                                       # 一键模式：自动找同目录/上级目录的启动器
PclPatch.exe "D:\Games\PCL\Plain Craft Launcher 2.exe"  # 一键模式 + 指定目标
PclPatch.exe il       <exe> <类型正则> [方法正则]   # 反汇编
PclPatch.exe findstr  <exe> <字符串>                # 找包含某字符串的方法
PclPatch.exe findcall <exe> <正则>                  # 找调用了某方法的签名
PclPatch.exe types    <exe> <正则>                  # 列类型
PclPatch.exe stats    <exe>                         # 结构统计
```

## 注意

- PCL 的自动更新会覆盖补丁，更新后需要重新打一次。
- 整合包安装那条是**重新接线**的（原逻辑被编译器删除），属于重建而非还原，遇到某类整合包安装异常请先回退再反馈。
- 仅在自己拥有 / 有权修改的启动器上使用。

## License

MIT

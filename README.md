# pcl-unlock

给 PCL 启动器用的自动补丁工具：把被编译期常量关掉的「MC 下载 / 整合包安装」相关代码，用 dnlib 重新接回去。

- **一键**：把 `oneclick-patch.bat` 放到启动器同目录，双击即可（旁边没有 `PclPatch.exe` 会自动下载）
- **安全**：先探测再动手，不是目标构建就只提示、不动文件；打补丁前自动备份
- **可回退**：备份在 `PCL\PatchBackup\`，改名回原文件名覆盖即可

## 它做什么

PCL 在源码里用编译期常量关闭了 MC 下载：

```vb
' Modules/ModSecret.vb
Public Const RestrictMcDownload As Boolean = True
```

常量会内联进每一个引用点，编译后受限分支只剩「报错」那一条，另一条分支的代码被编译器整段删除 —— 所以改配置没用，只能改 IL。

重建的 4 处：

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

## 兼容性

工具不绑定固定版本，运行时按方法体特征匹配：

| 目标 | 行为 |
| --- | --- |
| 含限制代码的构建 | 打 4 处补丁（先自动备份） |
| 不含限制的构建（社区版 / 自编译 / 已打过补丁） | 提示「无需处理」后退出，不改文件 |
| 单文件打包的构建 | 提示「本身没有限制」后退出，不改文件 |
| 结构不匹配 | 报错退出，原文件保持不动 |

## 一键使用

1. 从 [Release](https://github.com/ZhaoJun233/pcl-unlock/releases/latest) 下载 **`oneclick-patch.bat`**（2 KB；仓库根目录的 `一键补丁.bat` 是同一份，GitHub 附件名不支持中文所以 Release 里用了英文名）
2. 关掉启动器，把 bat 放到启动器 exe 同目录，双击（也可以直接把 exe 拖到 bat 上）

```
[1/4] 目标: D:\Games\PCL\Plain Craft Launcher 2.exe
[2/4] 已备份: D:\Games\PCL\PCL\PatchBackup\Plain Craft Launcher 2.exe.original.20261020_172013
[3/4] 正在打补丁...
[A] LoaderInit -> DlClientListLoader
[C] CardMinecraft.Visibility -> Visible
[D] install button branch restored
[B] modpack dispatch restored (helper added)
[4/4] 完成！启动器已解锁（原版备份见 PCL\PatchBackup）。
```

已经在别处打过补丁、或者本来就是社区版时，会看到：

```
[✓] 没有发现下载限制（社区版 / 旧版 / 已打过补丁）。
    无需处理，直接启动即可。
```

## 命令行用法

直接下 Release 里的 `PclPatch.exe`（self-contained 单文件，win-x64，不需要装 .NET）：

```powershell
PclPatch.exe patchall "<启动器>.exe" "out.exe"
```

从源码构建：

```powershell
dotnet build src -c Release
src\bin\Release\net10.0\PclPatch.exe patchall "<启动器>.exe" "out.exe"
```

- 输出文件改名成启动器原文件名覆盖回去即可；**覆盖前务必备份**。
- 补丁只做 IL 层面的重接线，不改动任何资源，也不联网；同一输入多次运行产物字节级一致。

其它模式（调试用）：

```
PclPatch.exe                                            # 一键模式：自动找同目录/上级目录的启动器
PclPatch.exe "D:\Games\PCL\Plain Craft Launcher 2.exe"  # 一键模式 + 指定目标
PclPatch.exe il       <exe> <类型正则> [方法正则]        # 反汇编
PclPatch.exe findstr  <exe> <字符串>                     # 找包含某字符串的方法
PclPatch.exe findcall <exe> <正则>                       # 找调用了某方法的签名
PclPatch.exe types    <exe> <正则>                       # 列类型
PclPatch.exe stats    <exe>                              # 结构统计
```

## 注意

- 启动器的自动更新会覆盖补丁，更新后需要重跑一次。
- 「整合包安装」那条是**重新接线**的（原逻辑已被编译器删除），属于重建而非还原，遇到某类整合包安装异常请先回退再反馈。
- 仅在自己拥有 / 有权修改的启动器上使用，修改后的启动器请勿二次分发；PCL 版权归其作者所有。

## License

MIT

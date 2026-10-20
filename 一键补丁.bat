@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title PCL 2.99 一键解锁补丁

if not exist "%~dp0PclPatch.exe" (
    where curl >nul 2>nul
    if errorlevel 1 (
        echo [x] 找不到 PclPatch.exe，系统里也没有 curl，无法自动下载。
        echo     请手动从 Release 下载 PclPatch.exe，放到本文件同目录。
        echo.
        pause
        exit /b 1
    )
    echo [*] 未找到 PclPatch.exe，正在从 GitHub Release 下载...
    curl -L --fail --progress-bar -o "%~dp0PclPatch.exe" "https://github.com/ZhaoJun233/pcl-unlock/releases/latest/download/PclPatch.exe"
    if errorlevel 1 (
        echo.
        echo [x] 下载失败，请检查网络，或手动下载 PclPatch.exe 放到本文件同目录。
        echo.
        pause
        exit /b 1
    )
    echo.
)

"%~dp0PclPatch.exe" %*

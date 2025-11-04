@echo off
REM 构建脚本 - MapTeleport Mod

REM 设置 DLL 目录环境变量
set DUCKOV_DLL_DIR=C:\SteamLibrary\steamapps\common\Escape from Duckov\Duckov_Data\Managed

REM 检查目录是否存在
if not exist "%DUCKOV_DLL_DIR%" (
    echo 错误: DLL 目录不存在: %DUCKOV_DLL_DIR%
    echo 请检查游戏安装路径
    exit /b 1
)

echo ========================================
echo 开始构建 MapTeleport Mod
echo ========================================
echo DLL 目录: %DUCKOV_DLL_DIR%
echo.

REM 构建项目
echo 正在构建项目...
dotnet build MapTeleport.csproj -c Release

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo 构建失败！
    exit /b 1
)

echo.
echo ========================================
echo 构建成功！
echo ========================================
echo 输出文件: bin\Release\MapTeleport.dll
echo.


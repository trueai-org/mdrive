@echo off
SET SVC_NAME=MDriveSyncService
SET SVC_DISPLAY_NAME=MDrive Sync Client API Service
SET SVC_PATH=E:\gits\MDriveSync\src\MDriveSync.Client.API\bin\Release\net8.0\publish\MDriveSync.Client.API.exe

:menu
cls
echo.
echo 请选择操作：
echo 1. 安装服务并启动
echo 2. 卸载服务
echo 3. 退出
echo.
set /p choice=请选择操作编号:

if "%choice%"=="1" goto install
if "%choice%"=="2" goto uninstall
if "%choice%"=="3" goto end
goto menu

:install
echo 正在安装服务...
sc create %SVC_NAME% binPath= "%SVC_PATH%" DisplayName= "%SVC_DISPLAY_NAME%" start= auto
echo 服务安装成功，正在启动服务...
net start %SVC_NAME%
echo 服务已启动。
goto end

:uninstall
echo 正在卸载服务...
sc delete %SVC_NAME%
echo 服务已卸载。
goto end

:end
echo.
echo 操作完成，按任意键退出。
pause >nul

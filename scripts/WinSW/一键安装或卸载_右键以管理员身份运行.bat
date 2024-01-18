@echo off
SET WIN_SW_PATH=%~dp0WinSW-x64.exe
SET CONFIG_PATH=%~dp0WinSW-x64.xml

:menu
cls
echo.
echo 请选择操作：
echo 1. 安装服务并启动
echo 2. 卸载服务
echo 3. 启动服务
echo 4. 停止服务
echo 5. 退出
echo.
set /p choice=请选择操作编号:

if "%choice%"=="1" goto install
if "%choice%"=="2" goto uninstall
if "%choice%"=="3" goto start
if "%choice%"=="4" goto stop
if "%choice%"=="5" goto end
goto menu

:install
echo 正在安装服务...
"%WIN_SW_PATH%" install "%CONFIG_PATH%"
if %ERRORLEVEL% neq 0 (
    echo 无法安装服务，请检查您是否拥有足够的权限。
    goto end
)
echo 服务安装成功，正在启动服务...
"%WIN_SW_PATH%" start "%CONFIG_PATH%"
if %ERRORLEVEL% neq 0 (
    echo 无法启动服务，请检查您是否拥有足够的权限。
    goto end
)
echo 服务已启动。
goto end

:uninstall
echo 正在停止服务...
"%WIN_SW_PATH%" stop "%CONFIG_PATH%"
if %ERRORLEVEL% neq 0 (
    echo 无法停止服务，请检查您是否拥有足够的权限。
    goto end
)
echo 正在卸载服务...
"%WIN_SW_PATH%" uninstall "%CONFIG_PATH%"
if %ERRORLEVEL% neq 0 (
    echo 无法卸载服务，请检查您是否拥有足够的权限。
    goto end
)
echo 服务已卸载。
goto end

:start
echo 正在启动服务...
"%WIN_SW_PATH%" start "%CONFIG_PATH%"
goto end

:stop
echo 正在停止服务...
"%WIN_SW_PATH%" stop "%CONFIG_PATH%"
goto end

:end
echo.
echo 操作完成，按任意键退出。
pause >nul


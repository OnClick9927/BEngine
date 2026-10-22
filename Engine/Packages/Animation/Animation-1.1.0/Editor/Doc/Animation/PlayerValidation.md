# Windows Player 动画验收

此流程创建临时工程和本机内容分发目录，执行实际 Player 游戏循环。`--validate` 只检查启动流程，可能在游戏的 Start 回调之前退出，不能替代本验收。

从仓库根目录执行，使用可用的 Python 3 运行时提供本机 HTTP 服务。每次使用新的输出目录；下例的 58000 端口应处于空闲状态。

```powershell
$runRoot = Join-Path (Get-Location) ('Temp/Engine/Validation/player-' + [Guid]::NewGuid().ToString('N'))
$content = Join-Path $runRoot 'content'
New-Item -ItemType Directory -Path (Join-Path $content 'main') -Force | Out-Null
```

在另一个终端启动 `python -m http.server 58000 --bind 127.0.0.1 --directory <上述 content/main 的绝对路径>`。资源 URL 指向 `main/latest.yaml` 所在目录。

```powershell
dotnet build Engine/src/Hosts/BEngine.Editor.Host/BEngine.Editor.Host.csproj -c Release --disable-build-servers -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false
dotnet run --project Temp/Validation/AnimationEnhancements/AnimationEnhancements.csproj -c Release --disable-build-servers -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false -- --player-fixture --content-url=http://127.0.0.1:58000/
$project = Get-Content Temp/Engine/Validation/FullEnhancement/animation-player-project.txt -Raw
$hostDll = 'Temp/Engine/Build/bin/BEngine.Editor.Host/release/BEngine.Editor.Host.dll'
dotnet $hostDll --build-content-update $project $content windows v1
$output = Join-Path $runRoot 'player'
dotnet $hostDll --build-player $project $output windows
```

每一步必须退出成功。然后运行普通 Player，启用已有的 AOT 自动检查/进入开关：

```powershell
$env:BENGINE_PLAYER_LOG_PATH = Join-Path $output 'acceptance.log'
$env:BENGINE_PLAYER_PERSISTENT_DATA_PATH = Join-Path $runRoot 'cache'
$env:BENGINE_AOT_AUTO_CHECK = '1'
$env:BENGINE_AOT_AUTO_CONFIRM_UPDATE = '1'
$env:BENGINE_AOT_AUTO_CONTINUE = '1'
$executable = Get-ChildItem $output -Filter '*.exe' | Select-Object -First 1
$process = Start-Process $executable.FullName -WorkingDirectory $output -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(45000)) { Stop-Process -Id $process.Id; throw 'Player acceptance timed out.' }
if ($process.ExitCode -ne 0) { throw 'Player exited with an error.' }
$log = Get-Content $env:BENGINE_PLAYER_LOG_PATH -Raw
if (!$log.Contains('ANIMATION_PACKAGED_PLAYER_OK') -or !$log.Contains('ANIMATOR_PACKAGED_LAYERS_OK') -or $log.Contains('level=Error') -or $log.Contains('BENGINE_FAIL')) {
    throw 'Player behavior assertions did not pass.'
}
```

断言覆盖打包 Sprite 的真实像素、对象/布尔/颜色轨道、加权切线、子状态机、Timeline 跳播和恢复、嵌套 2D 混合、叠加参考姿态、同步层和遮罩。

通过 `--build-content-update <project> <content> windows v2` 发布下一版，并保留缓存重新运行 Player，可验证内容版本切换。使用新的日志文件，检查 `previous=v1;active=v2` 和两个行为标记。测试结束后停止本机 HTTP 服务。

2026-10-06 的实际运行记录见仓库 `Temp/Validation/Reports/EngineCompletionChecklist.md`。本流程验证 Windows；不代表移动端或 Web 已通过。

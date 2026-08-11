Option Explicit

Dim shell, batchPath, command
Set shell = CreateObject("WScript.Shell")
batchPath = WScript.Arguments(0)
command = "cmd.exe /d /s /c " & Chr(34) & Chr(34) & batchPath & Chr(34) & " --background" & Chr(34)
shell.Run command, 0, False

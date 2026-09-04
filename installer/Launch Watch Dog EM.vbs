Option Explicit

Dim shell, http, fileSystem, installDirectory, url, attempt
Set shell = CreateObject("WScript.Shell")
Set fileSystem = CreateObject("Scripting.FileSystemObject")
installDirectory = fileSystem.GetParentFolderName(WScript.ScriptFullName)
shell.CurrentDirectory = installDirectory
url = "http://localhost:5080"
If IsRunning(url) Then
    shell.Run url, 1, False
    WScript.Quit 0
End If

' The metering server runs independently as a Windows Service. Starting it here
' is only a recovery path if it was manually stopped after Windows started.
shell.Run "sc.exe start WatchDogEM", 0, True

For attempt = 1 To 40
    WScript.Sleep 500
    If IsRunning(url) Then
        shell.Run url, 1, False
        WScript.Quit 0
    End If
Next

MsgBox "The Watch Dog EM Server is not running. Restart Windows or ask an administrator to start the 'Watch Dog EM Server' service.", 16, "Watch Dog EM"
WScript.Quit 1

Function IsRunning(address)
    On Error Resume Next
    Set http = CreateObject("MSXML2.XMLHTTP")
    http.Open "GET", address, False
    http.Send
    IsRunning = (Err.Number = 0 And http.Status >= 200 And http.Status < 500)
    Err.Clear
    On Error GoTo 0
End Function

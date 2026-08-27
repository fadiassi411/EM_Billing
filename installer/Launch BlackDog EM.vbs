Option Explicit

Dim shell, http, fileSystem, installDirectory, appPath, url, command, attempt
Set shell = CreateObject("WScript.Shell")
Set fileSystem = CreateObject("Scripting.FileSystemObject")
installDirectory = fileSystem.GetParentFolderName(WScript.ScriptFullName)
shell.CurrentDirectory = installDirectory
url = "http://localhost:5080"
appPath = installDirectory & "\MallEnergyBilling.Web.exe"

If IsRunning(url) Then
    shell.Run url, 1, False
    WScript.Quit 0
End If

command = Chr(34) & appPath & Chr(34) & " --urls " & url
shell.Run command, 0, False

For attempt = 1 To 40
    WScript.Sleep 500
    If IsRunning(url) Then
        shell.Run url, 1, False
        WScript.Quit 0
    End If
Next

MsgBox "BlackDog EM could not start. Restart Windows and try again.", 16, "BlackDog EM"
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

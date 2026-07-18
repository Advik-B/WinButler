' Re-anchor the Velopack MSI's install folder under Program Files.
'
' vpk 1.2.0's WiX template parents INSTALLFOLDER directly at TARGETDIR, so Windows
' Installer resolves it against ROOTDRIVE — the local drive with the most free space —
' and WinButler lands somewhere like D:\WinButler instead of Program Files. Inserting
' the standard ProgramFiles64Folder directory row and re-parenting INSTALLFOLDER under
' it makes the default (and the UI dialog's default) C:\Program Files\WinButler.
' The template's VELOPACK_INSTALLDIR msiexec property still overrides it.
'
' Usage: cscript //nologo patch-msi-installdir.vbs <path-to-msi>
Option Explicit
Dim inst, db, view

If WScript.Arguments.Count <> 1 Then
    WScript.Echo "Usage: cscript patch-msi-installdir.vbs <path-to-msi>"
    WScript.Quit 1
End If

Set inst = CreateObject("WindowsInstaller.Installer")
Set db = inst.OpenDatabase(WScript.Arguments(0), 1) ' 1 = transacted read/write

Sub Exec(sql)
    Set view = db.OpenView(sql)
    view.Execute
    view.Close
End Sub

Exec "INSERT INTO Directory (Directory, Directory_Parent, DefaultDir) VALUES ('ProgramFiles64Folder', 'TARGETDIR', 'PFiles')"
Exec "UPDATE Directory SET Directory_Parent = 'ProgramFiles64Folder' WHERE Directory = 'INSTALLFOLDER'"

db.Commit
WScript.Echo "Patched: INSTALLFOLDER now resolves under Program Files."

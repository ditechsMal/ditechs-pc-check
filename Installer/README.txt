DITECHS PC CHECK
===============

See how healthy this PC is, in plain English.

It reads a handful of things about this PC, then shows you a short report card: what is
fine, what is worth a look, and what is worth sorting out. It takes about ten seconds.


IMPORTANT: WHAT IT DOES NOT DO
------------------------------
It never changes anything.

  * It does not change any setting.
  * It does not delete anything.
  * It does not install anything.
  * It does not ask for administrator rights, and it never shows a Windows
    permission prompt.
  * It never connects to the internet. Nothing about this PC is sent anywhere.

If you are not comfortable installing software, you do not need to: the report says
everything on screen, and you can press "Copy this report" and email it to us instead.


--------------------------------------------------------------------
INSTALLING IT
--------------------------------------------------------------------
Right-click  Install.cmd  and choose "Run as administrator"
        (or just double-click it - it asks for permission itself).

Only this INSTALLER needs permission, because it copies the program into
C:\Program Files. The program itself never does.

The installer:
  * copies the app into  C:\Program Files\DitechsPcCheck
  * adds a Start Menu shortcut and a Desktop shortcut
  * adds "Ditechs PC Check" to Apps & features, so it can be removed from
    there like any other program
  * checks that .NET Framework 4.8 is present (it is on every Windows 10 and
    11 machine) and stops with a clear message if it is not

To remove it later:  Apps & features -> Ditechs PC Check -> Uninstall
                 or: run Install.cmd with -Uninstall

Pushing it out to several PCs? The installer runs silently:

    powershell -NoProfile -ExecutionPolicy Bypass -File Install-DitechsPcCheck.ps1 -Quiet

Other switches:
    -Uninstall            remove the app
    -NoDesktopShortcut    skip the Desktop icon
    -SourcePath <path>    install a copy of the exe from another location


--------------------------------------------------------------------
USING IT
--------------------------------------------------------------------
Double-click "Ditechs PC Check". It opens, checks a few things, and shows the report.
There is nothing to configure.

Three buttons along the bottom:

    Copy this report          puts the whole report on your clipboard, so you can
                              paste it into an email to us
    Save as a text file       saves it as a file you can attach
    Book a free health check  opens ditechs.co.uk/contact

One honest note: the report can only see a backup if it is one it recognises
(OneDrive, File History, or a backup program it knows). If it says it could not find
one and you know you have a backup, you can ignore that line.


--------------------------------------------------------------------
NEEDS
--------------------------------------------------------------------
  * 64-bit Windows 10 or Windows 11
  * Nothing else - no administrator rights, no internet connection


--------------------------------------------------------------------
Ditechs - computer, laptop and network help in Great Yarmouth, Gorleston and
Lowestoft. ditechs.co.uk

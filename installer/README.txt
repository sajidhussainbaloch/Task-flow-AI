ZayFlow AI Installer Guide
==========================

Files in this folder:
- installer.iss        -> Inno Setup installer script
- publish.ps1          -> Publishes the app into the ../publish folder
- build-installer.bat  -> Runs publish.ps1 quickly

How to build the installer:
1. Install .NET 8 SDK
2. Install Inno Setup Compiler
3. Run build-installer.bat
4. Open installer.iss in Inno Setup
5. Click Build -> Compile

Output:
- Published app files: ../publish
- Final installer exe: ./installer-output

How another user installs it:
1. Download the final setup exe
2. Double-click it
3. Follow the wizard
4. Launch ZayFlow AI from Start Menu or desktop shortcut

Important:
- The installer opens a normal setup wizard
- It includes an uninstaller
- The target PC does not need Visual Studio
- The target PC does not need the .NET runtime when using the self-contained publish
- The user still needs their own Groq API key inside the app settings
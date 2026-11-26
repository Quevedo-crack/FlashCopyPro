Unicode true
!include MUI2.nsh
!include x64.nsh
Name "FlashCopy Pro"
OutFile "FlashCopyPro-Setup-x64.exe"
InstallDir "$PROGRAMFILES64\FlashCopy Pro"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
VIProductVersion "1.1.0.0"
VIAddVersionKey "ProductName" "FlashCopy Pro"
VIAddVersionKey "FileVersion" "1.1.0"
VIAddVersionKey "FileDescription" "Instalador de FlashCopy Pro"
!define MUI_ICON "app.ico"
!define MUI_UNICON "app.ico"
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchUser
!define MUI_FINISHPAGE_RUN_TEXT "Abrir FlashCopy Pro"
!define UK "Software\Microsoft\Windows\CurrentVersion\Uninstall\FlashCopyPro"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Spanish"
Function LaunchUser
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\FlashCopy.exe"'
FunctionEnd
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "FlashCopy Pro necesita Windows de 64 bits."
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd
Section "FlashCopy Pro (obligatorio)" S0
  SectionIn RO
  nsExec::Exec 'taskkill /f /im FlashCopy.exe'
  SetOutPath "$INSTDIR"
  File "FlashCopy\out\FlashCopy.exe"
  File "app.ico"
  WriteUninstaller "$INSTDIR\Desinstalar.exe"
  CreateShortCut "$SMPROGRAMS\FlashCopy Pro.lnk" "$INSTDIR\FlashCopy.exe"
  CreateShortCut "$SENDTO\FlashCopy Pro.lnk" "$INSTDIR\FlashCopy.exe" "/copy"
  WriteRegStr HKLM "${UK}" "DisplayName" "FlashCopy Pro"
  WriteRegStr HKLM "${UK}" "DisplayIcon" "$INSTDIR\FlashCopy.exe"
  WriteRegStr HKLM "${UK}" "DisplayVersion" "1.1.0"
  WriteRegStr HKLM "${UK}" "Publisher" "FlashCopy"
  WriteRegStr HKLM "${UK}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UK}" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
  WriteRegDWORD HKLM "${UK}" "NoModify" 1
  WriteRegDWORD HKLM "${UK}" "EstimatedSize" 400
SectionEnd
Section "Menú del clic derecho del Explorador" S1
  ExecWait '"$INSTDIR\FlashCopy.exe" /register'
SectionEnd
Section "Sustituir el pegado de Windows (Ctrl+V) e iniciar con Windows" S2
  ExecWait '"$INSTDIR\FlashCopy.exe" /handler-on'
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\FlashCopy.exe"'
SectionEnd
Section /o "Menú clásico completo (sin 'Mostrar más opciones')" S3
  WriteRegStr HKCU "Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32" "" ""
SectionEnd
Section "Acceso directo en el escritorio" S4
  CreateShortCut "$DESKTOP\FlashCopy Pro.lnk" "$INSTDIR\FlashCopy.exe"
SectionEnd
Section "Uninstall"
  SetRegView 64
  nsExec::Exec 'taskkill /f /im FlashCopy.exe'
  ExecWait '"$INSTDIR\FlashCopy.exe" /unregister'
  Delete "$INSTDIR\FlashCopy.exe"
  Delete "$INSTDIR\app.ico"
  Delete "$INSTDIR\Desinstalar.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\FlashCopy Pro.lnk"
  Delete "$SENDTO\FlashCopy Pro.lnk"
  Delete "$DESKTOP\FlashCopy Pro.lnk"
  DeleteRegKey HKLM "${UK}"
SectionEnd

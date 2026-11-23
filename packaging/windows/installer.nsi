; SPDX-License-Identifier: GPL-3.0-or-later
Unicode true
!include "MUI2.nsh"
!include "x64.nsh"

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "../../artifacts/win-x64"
!endif
!ifndef OUTPUT_FILE
  !define OUTPUT_FILE "../../artifacts/PdfImageExtractor-Setup-x64.exe"
!endif
!ifndef UNINSTALL_INCLUDE
  !define UNINSTALL_INCLUDE "../../artifacts/uninstall-files.nsh"
!endif

Name "PDF Image Extractor"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\PdfImageExtractor"
InstallDirRegKey HKCU "Software\PdfImageExtractor" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
!define MUI_ICON "../../src/PdfExtractor.App/Assets/app.ico"
!define MUI_UNICON "../../src/PdfExtractor.App/Assets/app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\PdfImageExtractor.exe"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "../../LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "This application requires 64-bit Windows."
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext current
FunctionEnd

Section "Application"
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}/*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\PDF Image Extractor"
  CreateShortcut "$SMPROGRAMS\PDF Image Extractor\PDF Image Extractor.lnk" "$INSTDIR\PdfImageExtractor.exe"
  CreateShortcut "$DESKTOP\PDF Image Extractor.lnk" "$INSTDIR\PdfImageExtractor.exe"
  WriteRegStr HKCU "Software\PdfImageExtractor" "InstallDir" "$INSTDIR"
  ; Drop files onto either the EXE or shortcut: Explorer passes their paths as arguments.
  ; Register an Open With handler without replacing the user's default PDF reader.
  WriteRegStr HKCU "Software\Classes\Applications\PdfImageExtractor.exe" "FriendlyAppName" "PDF Image Extractor"
  WriteRegStr HKCU "Software\Classes\Applications\PdfImageExtractor.exe\SupportedTypes" ".pdf" ""
  WriteRegStr HKCU "Software\Classes\Applications\PdfImageExtractor.exe\shell\open\command" "" '$\"$INSTDIR\PdfImageExtractor.exe$\" $\"%1$\"'
  WriteRegStr HKCU "Software\Classes\.pdf\OpenWithProgids" "PdfImageExtractor.PDF" ""
  WriteRegStr HKCU "Software\Classes\PdfImageExtractor.PDF" "" "PDF Image Extractor"
  WriteRegStr HKCU "Software\Classes\PdfImageExtractor.PDF\shell\open\command" "" '$\"$INSTDIR\PdfImageExtractor.exe$\" $\"%1$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PdfImageExtractor" "DisplayName" "PDF Image Extractor"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PdfImageExtractor" "DisplayVersion" "1.0.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PdfImageExtractor" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PdfImageExtractor" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PdfImageExtractor" "DisplayIcon" "$INSTDIR\PdfImageExtractor.exe"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PdfImageExtractor" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PdfImageExtractor" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetRegView 64
  SetShellVarContext current
  Delete "$DESKTOP\PDF Image Extractor.lnk"
  RMDir /r "$SMPROGRAMS\PDF Image Extractor"
  DeleteRegKey HKCU "Software\PdfImageExtractor"
  DeleteRegKey HKCU "Software\Classes\Applications\PdfImageExtractor.exe"
  DeleteRegValue HKCU "Software\Classes\.pdf\OpenWithProgids" "PdfImageExtractor.PDF"
  DeleteRegKey HKCU "Software\Classes\PdfImageExtractor.PDF"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PdfImageExtractor"
  ; Delete the exact installed files; custom images in this directory must survive.
  !include "${UNINSTALL_INCLUDE}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd

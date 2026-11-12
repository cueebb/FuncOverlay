; =========================================================================
; FuncOverlay - Nullsoft Scriptable Install System (NSIS) Script
; Automated installer compiler for GitHub Actions workflow
; Target: FuncOverlay-Setup-x64.exe
; =========================================================================

!define PRODUCT_NAME "FuncOverlay"
!define PRODUCT_VERSION "1.0.0-dev"
!define PRODUCT_PUBLISHER "FuncOverlay"
!define PRODUCT_WEB_SITE "https://github.com/FuncOverlay/plugins"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\FuncOverlay.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKCU"

; MUI 2.0
!include "MUI2.nsh"

; MUI Settings
!define MUI_ABORTWARNING

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

; Finish Page: option to launch FuncOverlay immediately
!define MUI_FINISHPAGE_RUN "$INSTDIR\FuncOverlay.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch FuncOverlay"
!insertmacro MUI_PAGE_FINISH

; Uninstaller Pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; Language
!insertmacro MUI_LANGUAGE "English"

; Installer General Attributes
Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "..\artifacts\FuncOverlay-Setup-x64.exe"
InstallDir "$LOCALAPPDATA\FuncOverlay"
InstallDirRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_DIR_REGKEY}" ""
ShowInstDetails show
ShowUnInstDetails show
RequestExecutionLevel user

Section "MainSection" SEC01
  SetOutPath "$INSTDIR"
  SetOverwrite ifnewer

  ; Copy all published .NET 8 application binaries and dependencies
  File /r "..\publish\*.*"

  ; Create the required plugins directory: {FuncOverlay_folder}\plugins
  CreateDirectory "$INSTDIR\plugins"

  ; Create explanatory README inside plugins folder
  FileOpen $0 "$INSTDIR\plugins\README.txt" w
  FileWrite $0 "FuncOverlay Plugins Directory$\r$\n"
  FileWrite $0 "================================$\r$\n"
  FileWrite $0 "Extract plugin folders into this directory:$\r$\n"
  FileWrite $0 "{FuncOverlay_folder}\plugins\{plugin_folder}\$\r$\n"
  FileWrite $0 "Each plugin folder must contain metadata.json and either main.py or plugin.dll.$\r$\n"
  FileClose $0

  ; Create Start Menu Shortcuts
  CreateDirectory "$SMPROGRAMS\FuncOverlay"
  CreateShortCut "$SMPROGRAMS\FuncOverlay\FuncOverlay.lnk" "$INSTDIR\FuncOverlay.exe"
  CreateShortCut "$SMPROGRAMS\FuncOverlay\Uninstall FuncOverlay.lnk" "$INSTDIR\uninst.exe"
  CreateShortCut "$SMPROGRAMS\FuncOverlay\Plugins Directory.lnk" "$WINDIR\explorer.exe" "$INSTDIR\plugins"

  ; Register in Windows Add/Remove Programs (Registry)
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayName" "$(^Name)"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninst.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\FuncOverlay.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\FuncOverlay.exe"
SectionEnd

Section -Post
  WriteUninstaller "$INSTDIR\uninst.exe"
SectionEnd

Section Uninstall
  ; Remove Start Menu Shortcuts
  Delete "$SMPROGRAMS\FuncOverlay\FuncOverlay.lnk"
  Delete "$SMPROGRAMS\FuncOverlay\Uninstall FuncOverlay.lnk"
  Delete "$SMPROGRAMS\FuncOverlay\Plugins Directory.lnk"
  RMDir "$SMPROGRAMS\FuncOverlay"

  ; Remove Installed App Files
  Delete "$INSTDIR\*.*"
  RMDir /r "$INSTDIR\plugins"
  RMDir "$INSTDIR"

  ; Clean Registry Keys
  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_DIR_REGKEY}"
SectionEnd

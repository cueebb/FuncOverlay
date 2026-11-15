; =========================================================================
; FuncOverlay - Nullsoft Scriptable Install System (NSIS) Script
; Automated installer compiler for GitHub Actions workflow
; Target: FuncOverlay-Setup-x64.exe
; =========================================================================

Unicode True

!define PRODUCT_NAME "FuncOverlay"
!define PRODUCT_VERSION "1.0.0-dev"
!define PRODUCT_PUBLISHER "FuncOverlay"
!define PRODUCT_WEB_SITE "https://github.com/FuncOverlay/plugins"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\FuncOverlay.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKCU"

; MUI 2.0 & Modern Folder Picker
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"

; MUI Settings
!define MUI_ABORTWARNING
!define MUI_ICON "..\FuncOverlay\app.ico"
!define MUI_UNICON "..\FuncOverlay\app.ico"

Var DirDialog
Var DirTextCtrl
Var DirBrowseBtn

; Pages
!insertmacro MUI_PAGE_WELCOME
Page custom CustomDirectoryPageCreate CustomDirectoryPageLeave
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

Function CustomDirectoryPageCreate
  !insertmacro MUI_HEADER_TEXT "Choose Install Location" "Choose the exact folder in which to install FuncOverlay."

  nsDialogs::Create 1018
  Pop $DirDialog
  ${If} $DirDialog == "error"
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 24u "Setup will install FuncOverlay directly into the folder below. Click Browse to select any destination folder, or Next to install."
  Pop $0

  ${NSD_CreateGroupBox} 0 34u 100% 42u "Destination Folder"
  Pop $0

  ${NSD_CreateDirRequest} 10u 50u 225u 14u "$INSTDIR"
  Pop $DirTextCtrl

  ${NSD_CreateBrowseButton} 240u 49u 52u 15u "Browse..."
  Pop $DirBrowseBtn
  ${NSD_OnClick} $DirBrowseBtn OnBrowseDestinationFolder

  nsDialogs::Show
FunctionEnd

Function OnBrowseDestinationFolder
  Pop $0

  ${NSD_GetText} $DirTextCtrl $R0
  ${If} $R0 == ""
    StrCpy $R0 "$INSTDIR"
  ${EndIf}

  ; Modern Windows Vista/10/11 IFileOpenDialog (FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST = 0x860)
  !define CLSID_FileOpenDialog "{DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7}"
  !define IID_IFileDialog      "{42F85136-DB7E-439C-85F1-E4075D135FC8}"
  !define IID_IShellItem       "{43826D1E-E718-42EE-BC55-A1E261C37BFE}"

  StrCpy $R4 "cancel"
  System::Call "ole32::CoCreateInstance(g '${CLSID_FileOpenDialog}', p 0, i 1, g '${IID_IFileDialog}', *p .r0) i.r1"
  ${If} $1 == 0
    System::Call "$0->9(i 0x860) i.r1" ; IFileDialog::SetOptions (FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST)
    System::Call "$0->17(w 'Select Destination Folder')" ; IFileDialog::SetTitle
    System::Call "$0->18(w 'Select Folder')" ; IFileDialog::SetOkButtonLabel

    ${If} $R0 != ""
      System::Call "shell32::SHCreateItemFromParsingName(w r10, p 0, g '${IID_IShellItem}', *p .r5) i.r6"
      ${If} $6 == 0
        System::Call "$0->12(p r5)" ; IFileDialog::SetFolder
        System::Call "$5->2()"     ; IShellItem::Release
      ${EndIf}
    ${EndIf}

    System::Call "$0->3(p $HWNDPARENT) i.r1" ; IModalWindow::Show (modal to installer window)
    ${If} $1 == 0
      System::Call "$0->20(*p .r2) i.r1" ; IFileDialog::GetResult
      ${If} $1 == 0
        System::Call "$2->5(i 0x80058000, *p .r3) i.r1" ; IShellItem::GetDisplayName(SIGDN_FILESYSPATH)
        ${If} $1 == 0
          System::Call "kernel32::lstrcpynW(t .r4, p r3, i ${NSIS_MAX_STRLEN})"
          StrCpy $R4 $4
          System::Call "ole32::CoTaskMemFree(p r3)"
        ${EndIf}
        System::Call "$2->2()" ; IShellItem::Release
      ${EndIf}
    ${EndIf}
    System::Call "$0->2()" ; IFileDialog::Release

    ; Fallback if CoCreateInstance succeeded but user cancelled -> $R4 stays "cancel"
  ${Else}
    ; Fallback to legacy folder picker only if COM FileOpenDialog unavailable
    nsDialogs::SelectFolderDialog "Select Destination Folder" "$R0"
    Pop $R4
    ${If} $R4 == "error"
      StrCpy $R4 "cancel"
    ${EndIf}
  ${EndIf}

  ${If} $R4 != "cancel"
  ${AndIf} $R4 != ""
    StrCpy $INSTDIR "$R4"
    ${NSD_SetText} $DirTextCtrl "$INSTDIR"
  ${EndIf}
FunctionEnd

Function CustomDirectoryPageLeave
  ${NSD_GetText} $DirTextCtrl $0
  ${If} $0 == ""
    MessageBox MB_ICONEXCLAMATION|MB_OK "Please choose a valid installation folder."
    Abort
  ${EndIf}
  StrCpy $INSTDIR "$0"
FunctionEnd

Section "MainSection" SEC01
  SetOutPath "$INSTDIR"
  SetOverwrite ifnewer

  ; Copy all published .NET 8 application binaries and dependencies (explicitly skipping any plugins directory)
  File /r /x plugins "..\publish\*.*"

  ; Create the empty user plugins directory: {FuncOverlay_folder}\plugins
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
